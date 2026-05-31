using System;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    /// <summary>
    /// Client request to consolidate/sort the given cargo containers (server-authoritative).
    /// <see cref="Mode"/> is the <c>InventorySortMode</c> value chosen on the screen.
    /// </summary>
    [ProtoContract]
    public class PacketSortInventory : NetworkPackage
    {
        [ProtoMember(1)] public long[] ContainerIds { get; set; }
        [ProtoMember(2)] public int Mode { get; set; }

        public override PackageCode Code => PackageCode.SortInventory;

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
