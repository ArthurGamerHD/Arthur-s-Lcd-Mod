using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace LcdMod.Common.Images
{
    /// <summary>
    /// Images embedded in a grid's mod storage: a small manifest (id, owner, size) and a payload table,
    /// each under its own registered Guid. Parsed tables are cached per grid until the stored strings change.
    /// </summary>
    public static class GridImageStorage
    {
        static readonly Dictionary<long, CachedStore> Cache = new Dictionary<long, CachedStore>();

        sealed class CachedStore
        {
            public string ManifestRaw;
            public string PayloadRaw;
            public GridImageManifest Manifest;
            public GridImagePayloadTable Payloads;
        }

        public static void Clear()
        {
            Cache.Clear();
        }

        public static void Forget(long gridId)
        {
            Cache.Remove(gridId);
        }

        public static GridImageManifestEntry FindEntry(IMyCubeGrid grid, string id)
        {
            CachedStore store = Load(grid, false);
            return store == null ? null : FindEntry(store.Manifest, id);
        }

        public static void GetEntries(IMyCubeGrid grid, List<GridImageManifestEntry> entries)
        {
            CachedStore store = Load(grid, false);
            if (store != null)
                entries.AddRange(store.Manifest.Entries);
        }

        public static bool TryGetImage(IMyCubeGrid grid, string id, out GridImageManifestEntry entry, out GridImagePayload payload)
        {
            entry = null;
            payload = null;
            CachedStore store = Load(grid, true);
            if (store == null)
                return false;

            entry = FindEntry(store.Manifest, id);
            if (entry == null)
                return false;

            for (int i = 0; i < store.Payloads.Records.Count; i++)
            {
                GridImagePayloadRecord record = store.Payloads.Records[i];
                if (record != null && record.Id == id)
                {
                    payload = record.Payload;
                    return payload != null;
                }
            }

            return false;
        }

        public static int GetTotalPayloadBytes(IMyCubeGrid grid)
        {
            CachedStore store = Load(grid, false);
            if (store == null)
                return 0;

            int total = 0;
            for (int i = 0; i < store.Manifest.Entries.Count; i++)
                total += store.Manifest.Entries[i].PayloadBytes;

            return total;
        }

        public static bool Add(IMyCubeGrid grid, GridImageManifestEntry entry, GridImagePayload payload)
        {
            CachedStore store = Load(grid, true);
            if (store == null || entry == null || payload == null || FindEntry(store.Manifest, entry.Id) != null)
                return false;

            store.Manifest.Entries.Add(entry.Clone());
            store.Payloads.Records.Add(new GridImagePayloadRecord { Id = entry.Id, Payload = payload });
            Write(grid, store);
            return true;
        }

        public static int RemoveExcept(IMyCubeGrid grid, ICollection<string> keep)
        {
            CachedStore store = Load(grid, true);
            if (store == null)
                return 0;

            int removed = store.Manifest.Entries.RemoveAll(entry => entry == null || !keep.Contains(entry.Id));
            store.Payloads.Records.RemoveAll(record => record == null || !keep.Contains(record.Id));
            if (removed > 0)
                Write(grid, store);

            return removed;
        }

        /// <summary>
        /// Collects image ids referenced by the screen configs of every block on the grid by scanning
        /// the serialized configs for sprite names, so any app that stores an image sprite is covered.
        /// </summary>
        public static void CollectReferencedIds(IMyCubeGrid grid, HashSet<string> ids)
        {
            if (grid == null)
                return;

            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, block => block.FatBlock != null && block.FatBlock.Storage != null);
            for (int i = 0; i < blocks.Count; i++)
            {
                string value;
                if (!blocks[i].FatBlock.Storage.TryGetValue(Constants.StorageGuid, out value) ||
                    string.IsNullOrEmpty(value))
                {
                    continue;
                }

                try
                {
                    CollectReferencedIds(Convert.FromBase64String(value), ids);
                }
                catch (FormatException)
                {
                }
            }
        }

        public static void CollectReferencedIds(byte[] serialized, HashSet<string> ids)
        {
            GridImageCodec.CollectSpriteIds(serialized, ids);
        }

        static GridImageManifestEntry FindEntry(GridImageManifest manifest, string id)
        {
            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                GridImageManifestEntry entry = manifest.Entries[i];
                if (entry != null && entry.Id == id)
                    return entry;
            }

            return null;
        }

        static CachedStore Load(IMyCubeGrid grid, bool withPayloads)
        {
            if (grid == null)
                return null;

            string manifestRaw = null;
            string payloadRaw = null;
            if (grid.Storage != null)
            {
                grid.Storage.TryGetValue(Constants.GridImageManifestGuid, out manifestRaw);
                grid.Storage.TryGetValue(Constants.GridImagePayloadGuid, out payloadRaw);
            }

            CachedStore store;
            if (!Cache.TryGetValue(grid.EntityId, out store) || !ReferenceEquals(store.ManifestRaw, manifestRaw))
            {
                store = new CachedStore
                {
                    ManifestRaw = manifestRaw,
                    Manifest = Deserialize<GridImageManifest>(manifestRaw) ?? new GridImageManifest()
                };
                Cache[grid.EntityId] = store;
            }

            if (withPayloads && (store.Payloads == null || !ReferenceEquals(store.PayloadRaw, payloadRaw)))
            {
                store.PayloadRaw = payloadRaw;
                store.Payloads = Deserialize<GridImagePayloadTable>(payloadRaw) ?? new GridImagePayloadTable();
            }

            return store;
        }

        static void Write(IMyCubeGrid grid, CachedStore store)
        {
            IMyEntity entity = grid;
            if (entity.Storage == null)
                entity.Storage = new MyModStorageComponent();

            if (store.Manifest.Entries.Count == 0)
            {
                entity.Storage.Remove(Constants.GridImageManifestGuid);
                entity.Storage.Remove(Constants.GridImagePayloadGuid);
                store.ManifestRaw = null;
                store.PayloadRaw = null;
                return;
            }

            store.ManifestRaw = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(store.Manifest));
            store.PayloadRaw = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(store.Payloads));
            entity.Storage[Constants.GridImageManifestGuid] = store.ManifestRaw;
            entity.Storage[Constants.GridImagePayloadGuid] = store.PayloadRaw;
        }

        static T Deserialize<T>(string raw) where T : class
        {
            if (string.IsNullOrEmpty(raw))
                return null;

            try
            {
                return MyAPIGateway.Utilities.SerializeFromBinary<T>(Convert.FromBase64String(raw));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(GridImageStorage));
                return null;
            }
        }
    }
}
