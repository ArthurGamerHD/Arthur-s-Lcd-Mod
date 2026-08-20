using System;
using Generated;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    /// <summary>
    /// Client request from the container action dialog: move the chosen item types between the
    /// source container and the targets (Send / Receive / Balance). Server-authoritative.
    /// </summary>
    [ProtoContract]
    [NetworkPayload(5)]
    public partial class PacketTransferItems
    {
        [ProtoMember(1)] public long SourceId { get; set; }
        [ProtoMember(2)] public long[] TargetIds { get; set; }
        [ProtoMember(3)] public string[] TypeKeys { get; set; }
        [ProtoMember(4)] public int Mode { get; set; }

        // ReSharper disable once UnusedMember.Global
        public PacketTransferItems() // Needed for Protobuf
        {
            TargetIds = Array.Empty<long>();
            TypeKeys = Array.Empty<string>();
        }

        public PacketTransferItems(long sourceId, long[] targetIds, string[] typeKeys, int mode)
        {
            SourceId = sourceId;
            TargetIds = targetIds ?? Array.Empty<long>();
            TypeKeys = typeKeys ?? Array.Empty<string>();
            Mode = mode;
        }
    }
}
