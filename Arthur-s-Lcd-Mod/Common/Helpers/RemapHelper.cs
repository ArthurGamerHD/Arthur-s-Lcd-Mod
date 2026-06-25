using System;
using System.Collections.Generic;
using LcdMod.Common.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Models;

using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace LcdMod.Common.Helpers
{
    public static class RemapHelper
    {
        public static void PinBlock(long entityId)
        {
            if (entityId == 0)
                return;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out entity))
                return;

            PinBlock(entity as IMyTerminalBlock);
        }

        public static void PinBlock(IMyTerminalBlock block)
        {
            try
            {
                if (block == null || block.EntityId == 0)
                    return;

                if (block.Storage == null)
                    block.Storage = new MyModStorageComponent();

                string value;
                if (block.Storage.TryGetValue(Constants.StorageRemapGuid, out value) && !string.IsNullOrEmpty(value))
                    return;

                block.Storage[Constants.StorageRemapGuid] = block.EntityId.ToString();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(RemapHelper));
            }
        }

        public static void PinBlocks(IEnumerable<long> entityIds)
        {
            if (entityIds == null)
                return;

            foreach (var entityId in entityIds)
                PinBlock(entityId);
        }

        public static void PinBlocks(ScreenProviderConfig providerConfig)
        {
            if (providerConfig == null || providerConfig.Surfaces == null)
                return;

            var entityIds = new List<long>();
            for (int i = 0; i < providerConfig.Surfaces.Count; i++)
                ComponentConfigEntityReferences.CollectPinnedEntityIds(providerConfig.Surfaces[i], entityIds);

            PinBlocks(entityIds);
        }

        public static void RemapGrid(IMyCubeGrid grid)
        {
            RemapGrid(grid, null);
        }

        public static void RemapGrid(IMyCubeGrid grid, Dictionary<long, long> remap)
        {
            try
            {
                if (grid == null || grid.MarkedForClose)
                    return;

                var blocks = new List<IMySlimBlock>();
                if (remap == null)
                    remap = new Dictionary<long, long>();

                grid.GetBlocks(blocks);

                for (int i = 0; i < blocks.Count; i++)
                {
                    var fatBlock = blocks[i].FatBlock as IMyTerminalBlock;
                    if (fatBlock == null)
                        continue;

                    long knownId;
                    if (!TryGetKnownId(fatBlock, out knownId))
                        continue;

                    if (knownId != fatBlock.EntityId)
                        remap[knownId] = fatBlock.EntityId;

                    SaveKnownId(fatBlock, fatBlock.EntityId);
                }

                if (remap.Count == 0)
                    return;

                for (int i = 0; i < blocks.Count; i++)
                {
                    var entity = blocks[i].FatBlock as IMyEntity;
                    if (entity == null || entity.Storage == null)
                        continue;

                    string currentValue;
                    string legacyValue;
                    bool hasCurrentConfig =
                        entity.Storage.TryGetValue(Constants.StorageGuid, out currentValue)
                        && !string.IsNullOrEmpty(currentValue);
                    bool hasLegacyConfig =
                        entity.Storage.TryGetValue(Constants.V0StorageGuid, out legacyValue)
                        && !string.IsNullOrEmpty(legacyValue);

                    if (!hasCurrentConfig && !hasLegacyConfig)
                        continue;

                    var providerConfig = ScreenProviderConfigStorage.TryLoad(entity);
                    if (providerConfig == null)
                        continue;

                    if (!RemapConfig(providerConfig, grid.EntityId, remap))
                        continue;

                    ScreenProviderConfigStorage.Save(entity, providerConfig);
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(RemapHelper));
            }
        }

        static bool TryGetKnownId(IMyTerminalBlock block, out long knownId)
        {
            knownId = 0;
            if (block == null || block.Storage == null)
                return false;

            string value;
            if (!block.Storage.TryGetValue(Constants.StorageRemapGuid, out value) || string.IsNullOrEmpty(value))
                return false;

            return long.TryParse(value, out knownId) && knownId != 0;
        }

        static void SaveKnownId(IMyTerminalBlock block, long knownId)
        {
            if (block.Storage == null)
                block.Storage = new MyModStorageComponent();

            block.Storage[Constants.StorageRemapGuid] = knownId.ToString();
        }

        static bool RemapConfig(ScreenProviderConfig providerConfig, long gridEntityId, Dictionary<long, long> remap)
        {
            bool changed = false;

            if (providerConfig.Parent != gridEntityId)
            {
                providerConfig.Parent = gridEntityId;
                changed = true;
            }

            if (providerConfig.Surfaces == null)
                return changed;

            for (int i = 0; i < providerConfig.Surfaces.Count; i++)
                changed |= ComponentConfigEntityReferences.RemapEntityReferences(providerConfig.Surfaces[i], remap);

            return changed;
        }
    }
}
