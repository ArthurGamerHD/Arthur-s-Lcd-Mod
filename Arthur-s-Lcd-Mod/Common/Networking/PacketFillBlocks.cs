using System;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    /// <summary>
    /// Client request from the cargo screen "fill" buttons: top up the given target blocks (weapons or
    /// reactors) from the given source containers. <see cref="Kind"/> is a <c>FillKind</c> value.
    /// Server-authoritative.
    /// </summary>
    [ProtoContract]
    public class PacketFillBlocks : NetworkPackage
    {
        [ProtoMember(1)] public long[] SourceIds { get; set; }
        [ProtoMember(2)] public long[] TargetIds { get; set; }
        [ProtoMember(3)] public int Kind { get; set; }

        public override PackageCode Code => PackageCode.FillBlocks;

        // ReSharper disable once UnusedMember.Global
        public PacketFillBlocks() // Needed for Protobuf
        {
            SourceIds = Array.Empty<long>();
            TargetIds = Array.Empty<long>();
        }

        public PacketFillBlocks(long[] sourceIds, long[] targetIds, int kind)
        {
            SourceIds = sourceIds ?? Array.Empty<long>();
            TargetIds = targetIds ?? Array.Empty<long>();
            Kind = kind;
        }
    }
}
