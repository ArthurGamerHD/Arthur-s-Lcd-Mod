using System;
using Generated;
using LcdMod.Common.Helpers;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    /// <summary>
    /// Client request from the cargo screen "fill" buttons: top up the given target blocks (weapons or
    /// reactors) from the given source containers. <see cref="Kind"/> is a <c>FillKind</c> value and
    /// <see cref="Settings"/> carries the player's per-reactor/per-ammo targets. Server-authoritative.
    /// </summary>
    [ProtoContract]
    [NetworkPayload(8)]
    public partial class PacketFillBlocks
    {
        [ProtoMember(1)] public long[] SourceIds { get; set; }
        [ProtoMember(2)] public long[] TargetIds { get; set; }
        [ProtoMember(3)] public int Kind { get; set; }
        [ProtoMember(4)] public FillSettings Settings { get; set; }

        public PacketFillBlocks() 
        {
            SourceIds = Array.Empty<long>();
            TargetIds = Array.Empty<long>();
        }

        public PacketFillBlocks(long[] sourceIds, long[] targetIds, int kind, FillSettings settings)
        {
            SourceIds = sourceIds ?? Array.Empty<long>();
            TargetIds = targetIds ?? Array.Empty<long>();
            Kind = kind;
            Settings = settings;
        }
    }
}
