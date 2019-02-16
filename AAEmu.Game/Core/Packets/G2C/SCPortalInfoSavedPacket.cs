﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPortalInfoSavedPacket : GamePacket
    {
        private readonly Portal _portal;
        
        public SCPortalInfoSavedPacket(Portal portal) : base(0x05c, 1) // 0x058
        {
            _portal = portal;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_portal);
            return stream;
        }
    }
}
