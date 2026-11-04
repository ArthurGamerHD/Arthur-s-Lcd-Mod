using System;
using Generated;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    // A client asks the server to keep (or, with no data, drop) a screen's grid snapshot.
    [ProtoContract]
    [NetworkPayload(23)]
    public sealed partial class PacketStoreGridSnapshot
    {
        [ProtoMember(1)] public long BlockId;
        [ProtoMember(2)] public string Key;
        [ProtoMember(3)] public byte[] Data;

        public PacketStoreGridSnapshot()
        {
            Data = Array.Empty<byte>();
        }
    }

    // The server tells every client a screen's snapshot changed, so their copy of the block's storage matches.
    [ProtoContract]
    [NetworkPayload(24)]
    public sealed partial class PacketSyncGridSnapshot
    {
        [ProtoMember(1)] public long BlockId;
        [ProtoMember(2)] public string Key;
        [ProtoMember(3)] public byte[] Data;

        public PacketSyncGridSnapshot()
        {
            Data = Array.Empty<byte>();
        }
    }
}
