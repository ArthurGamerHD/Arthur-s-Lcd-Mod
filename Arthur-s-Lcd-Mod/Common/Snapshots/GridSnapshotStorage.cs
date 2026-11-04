using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using ProtoBuf;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace LcdMod.Common.Snapshots
{
    [ProtoContract]
    public sealed class GridSnapshotEntry
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public byte[] Data;
    }

    [ProtoContract]
    public sealed class GridSnapshotTable
    {
        [ProtoMember(1)] public List<GridSnapshotEntry> Entries = new List<GridSnapshotEntry>();
    }

    /// <summary>
    /// Grid snapshots kept in a screen block's mod storage, one per key (a surface and what the snapshot is for), as
    /// the bytes of Acrux's GridSnapshot. Parsed tables are cached per block until the stored string changes, so a
    /// caller gets the same array back until the snapshot is replaced.
    /// </summary>
    public static class GridSnapshotStorage
    {
        static readonly Dictionary<long, CachedTable> Cache = new Dictionary<long, CachedTable>();

        sealed class CachedTable
        {
            public string Raw;
            public GridSnapshotTable Table;
        }

        public static void Clear()
        {
            Cache.Clear();
        }

        public static string KeyFor(int surfaceIndex, string purpose)
        {
            return surfaceIndex + ":" + purpose;
        }

        public static byte[] Get(IMyEntity block, string key)
        {
            GridSnapshotTable table = Load(block);
            if (table == null)
                return null;
            for (int i = 0; i < table.Entries.Count; i++)
            {
                GridSnapshotEntry entry = table.Entries[i];
                if (entry != null && entry.Key == key)
                    return entry.Data;
            }
            return null;
        }

        public static int TotalBytes(IMyEntity block, string exceptKey)
        {
            GridSnapshotTable table = Load(block);
            int total = 0;
            if (table == null)
                return 0;
            for (int i = 0; i < table.Entries.Count; i++)
            {
                GridSnapshotEntry entry = table.Entries[i];
                if (entry != null && entry.Data != null && entry.Key != exceptKey)
                    total += entry.Data.Length;
            }
            return total;
        }

        // Empty or null data removes the key.
        public static void Set(IMyEntity block, string key, byte[] data)
        {
            if (block == null || string.IsNullOrEmpty(key))
                return;
            GridSnapshotTable table = Load(block) ?? new GridSnapshotTable();
            table.Entries.RemoveAll(entry => entry == null || entry.Key == key);
            if (data != null && data.Length > 0)
                table.Entries.Add(new GridSnapshotEntry { Key = key, Data = data });

            if (block.Storage == null)
                block.Storage = new MyModStorageComponent();
            CachedTable cached = new CachedTable { Table = table };
            if (table.Entries.Count == 0)
                block.Storage.Remove(Constants.GridSnapshotGuid);
            else
            {
                cached.Raw = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(table));
                block.Storage[Constants.GridSnapshotGuid] = cached.Raw;
            }
            Cache[block.EntityId] = cached;
        }

        static GridSnapshotTable Load(IMyEntity block)
        {
            if (block == null)
                return null;
            string raw = null;
            if (block.Storage != null)
                block.Storage.TryGetValue(Constants.GridSnapshotGuid, out raw);
            CachedTable cached;
            if (Cache.TryGetValue(block.EntityId, out cached) && ReferenceEquals(cached.Raw, raw))
                return cached.Table;
            cached = new CachedTable { Raw = raw, Table = Deserialize(raw) };
            Cache[block.EntityId] = cached;
            return cached.Table;
        }

        static GridSnapshotTable Deserialize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            try
            {
                return MyAPIGateway.Utilities.SerializeFromBinary<GridSnapshotTable>(Convert.FromBase64String(raw));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(GridSnapshotStorage));
                return null;
            }
        }
    }
}
