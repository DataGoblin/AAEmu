﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpandSlotsPacket : GamePacket
    {
        public CSExpandSlotsPacket() : base(0x041, 1) // TODO 1.0 opcode: 0x03f
        {
        }

        public override void Read(PacketStream stream)
        {
            var slotType = stream.ReadInt32();
            var autoUseAAPoint = stream.ReadBoolean();

            _log.Debug("ExpandSlots, slotType -> {0}, autoUseAAPoint -> {1}", slotType, autoUseAAPoint);

            Connection.ActiveChar.Inventory.ExpandSlot((SlotType)slotType);
        }
    }
}
