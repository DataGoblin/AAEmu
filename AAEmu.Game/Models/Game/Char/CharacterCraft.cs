﻿using System;
using System.Collections.Generic;
using System.Media;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterCraft
{
    private int _count { get; set; }
    private Craft _craft { get; set; }
    private uint _doodadId { get; set; }

    public Character Owner { get; set; }
    public bool IsCrafting { get; set; }

    public CharacterCraft(Character owner) => Owner = owner;

    public void Craft(Craft craft, int count, uint doodadId)
    {
        _craft = craft;
        _count = count;
        _doodadId = doodadId;

        // Check if we have enough materials
        var hasMaterials = true;

        foreach (var craftMaterial in craft.CraftMaterials)
        {
            if (Owner.Inventory.GetItemsCount(craftMaterial.ItemId) < craftMaterial.Amount)
                hasMaterials = false;
        }

        // Check if we have permission to actually use the doodad (mostly sanity check since the client already checks this before you can craft)
        var hasPermission = true;
        var doodad = WorldManager.Instance.GetDoodad(doodadId);
        if ((doodad != null) && (doodad.FuncPermission != DoodadFuncPermission.Any && (Owner != null)))
        {
            switch (doodad.FuncPermission)
            {
                case DoodadFuncPermission.Any:
                case DoodadFuncPermission.Permission1:
                case DoodadFuncPermission.Permission2:
                case DoodadFuncPermission.OwnerOnly:
                case DoodadFuncPermission.Permission4:
                case DoodadFuncPermission.OwnerRaidMembers:
                    break;
                case DoodadFuncPermission.SameAccount:
                    if (doodad.OwnerType == DoodadOwnerType.Character)
                        hasPermission = WorldManager.Instance.GetCharacterById(doodad.OwnerId).AccountId == Owner.AccountId;
                    break;
                case DoodadFuncPermission.ZoneResidents:
                    hasPermission = false;
                    var zoneGroup = ZoneManager.Instance.GetZoneByKey(doodad.Transform.ZoneId)?.GroupId ?? 0;
                    var playerHouses = new Dictionary<uint, House>();
                    if (HousingManager.Instance.GetByAccountId(playerHouses, Owner.AccountId) > 0)
                    {
                        foreach (var (houseId, playerHouse) in playerHouses)
                        {
                            var houseZoneGroup = ZoneManager.Instance.GetZoneByKey(playerHouse.Transform.ZoneId)?.GroupId ?? 0;
                            if (houseZoneGroup == zoneGroup)
                            {
                                hasPermission = true;
                                break;
                            }
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            Owner.SendMessage($"Crafting using @DOODAD_NAME({doodad.TemplateId}) - {doodad.TemplateId} (objId: {doodad.ObjId}) with current permission {doodad.FuncPermission} = {hasPermission}");
        }


        if (hasMaterials && hasPermission)
        {
            IsCrafting = true;

            var caster = SkillCaster.GetByType(SkillCasterType.Unit);
            caster.ObjId = Owner.ObjId;

            var target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
            target.ObjId = doodadId;

            var skill = new Skill(SkillManager.Instance.GetSkillTemplate(craft.SkillId));
            skill.Use(Owner, caster, target);
        }
    }

    public void EndCraft()
    {
        _count--;
        IsCrafting = false;

        if (_craft == null)
        {
            CancelCraft();
            return;
        }

        if (Owner.Inventory.FreeSlotCount(SlotType.Inventory) < _craft.CraftProducts.Count)
        {
            CancelCraft();
            return;
        }

        foreach (var product in _craft.CraftProducts)
        {
            // Check if we're crafting a tradepack, if so, try to remove currently equipped backpack slot
            if (ItemManager.Instance.IsAutoEquipTradePack(product.ItemId) == false)
            {
                Owner.Inventory.Bag.AcquireDefaultItem(ItemTaskType.CraftActSaved, product.ItemId, product.Amount, -1, Owner.Id);
                // Owner.Inventory.Bag.AcquireDefaultItem(Items.Actions.ItemTaskType.CraftPickupProduct, product.ItemId, product.Amount, -1, Owner.Id);
            }
            else
            {
                if (!Owner.Inventory.TryEquipNewBackPack(ItemTaskType.CraftPickupProduct, product.ItemId, product.Amount, -1, Owner.Id))
                {
                    CancelCraft();
                    return;
                }
            }
        }

        foreach (var material in _craft.CraftMaterials)
        {
            Owner.Inventory.Bag.ConsumeItem(ItemTaskType.CraftActSaved, material.ItemId, material.Amount, null);
        }

        Owner.Quests.OnCraft(_craft); // TODO added for quest Id=6024

        if (_count > 0)
        {
            var newCraft = new CraftTask(Owner, _craft.Id, _doodadId, _count);
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(_craft.SkillId);
            var timeToGlobalCooldown = Owner.GlobalCooldown - DateTime.UtcNow;
            var nextCraftDelay = timeToGlobalCooldown.TotalMilliseconds > skillTemplate.CooldownTime
                ? timeToGlobalCooldown
                : TimeSpan.FromMilliseconds(skillTemplate.CooldownTime);
            TaskManager.Instance.Schedule(newCraft, nextCraftDelay, null, 1);
            // Owner.SendMessage($"Continue craft: {_craft.Id} for {_count} more times TaskId: {newCraft.Id}, cooldown: {nextCraftDelay.TotalMilliseconds}ms");
            // Craft(_craft, _count, _doodadId);
        }
        else
        {
            CancelCraft();
        }
    }

    public void CancelCraft()
    {
        IsCrafting = false;
        _craft = null;
        _count = 0;
        _doodadId = 0;

        // Also cancel the related skill ? I don't think this really does anything for crafts, but can't hurt I guess
        if (Owner != null)
        {
            if (Owner.SkillTask != null)
                Owner.SkillTask.Skill.Cancelled = true;
            Owner.InterruptSkills();
        }

        // Might want to send a packet here, I think there is a packet when crafting fails. Not sure yet..
    }
}
