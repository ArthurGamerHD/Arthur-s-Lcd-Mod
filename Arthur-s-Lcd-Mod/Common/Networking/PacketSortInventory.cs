using System;
using Generated;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    /// <summary>
    /// Client request to consolidate/sort the given cargo containers (server-authoritative).
    /// <see cref="Mode"/> is the <c>InventorySortMode</c> value chosen on the screen.
    /// </summary>
    [ProtoContract]
    [NetworkPayload(4)]
    public partial class PacketSortInventory
    {
        [ProtoMember(1)] public long[] ContainerIds { get; set; }
        [ProtoMember(2)] public int Mode { get; set; }

        // ReSharper disable once UnusedMember.Global
        public PacketSortInventory() // Needed for Protobuf
        {
            ContainerIds = Array.Empty<long>();
        }

        public PacketSortInventory(long[] containerIds, int mode)
        {
            ContainerIds = containerIds ?? Array.Empty<long>();
            Mode = mode;
        }
    }
}
