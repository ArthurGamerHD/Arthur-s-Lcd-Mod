using System;
using System.Collections.Generic;
using ProtoBuf;
using VRage.Game;
using VRageMath;

namespace LcdMod.Common.Market
{
    [Flags]
    public enum NpcMarketStationKnowledgeFlags : byte
    {
        None = 0,
        HostOwner = 1,
        OtherFactionMember = 2
    }

    [ProtoContract]
    public sealed class NpcMarketStationDto
    {
        [ProtoMember(1)]
        public long StationId;

        [ProtoMember(2)]
        public string Name;

        [ProtoMember(3)]
        public long NpcFactionId;

        [ProtoMember(4)]
        public Vector3D Position;

        [ProtoMember(5)]
        public MyStationTypeEnum StationType;

        [ProtoMember(6)]
        public bool IsDeepSpaceStation;

        [ProtoMember(7)]
        public NpcMarketStationKnowledgeFlags KnowledgeFlags;

        [ProtoMember(8)]
        public int KnownByMemberCount;

        [ProtoMember(9)]
        public List<NpcMarketOfferDto> Offers;
    }
}
