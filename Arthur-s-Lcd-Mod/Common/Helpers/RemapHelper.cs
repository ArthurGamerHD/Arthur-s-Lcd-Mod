using System;
using System.Collections.Generic;
using LcdMod.Common.Config;
using LcdMod.Common.Config.Interfaces;
using LcdMod.Common.Config.Models;

using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;

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
            if (providerConfig == null || providerConfig.Screens == null)
                return;

            for (int i = 0; i < providerConfig.Screens.Count; i++)
            {
                var screen = providerConfig.Screens[i];
                if (screen == null)
                    continue;

                if (screen.OreScannerReferenceId != 0)
                    PinBlock(screen.OreScannerReferenceId);

                var withBlocks = screen as ScreenConfigWithBlocks;
                if (withBlocks != null)
                    PinBlocks(withBlocks.SelectedBlocks);

                var withReference = screen as IConfigWithReferenceBlock;
                if (withReference != null && withReference.ReferenceBlock != 0)
                    PinBlock(withReference.ReferenceBlock);
            }
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

                    string value;
                    if (!entity.Storage.TryGetValue(Constants.StorageGuid, out value) || string.IsNullOrEmpty(value))
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

            if (providerConfig.Screens == null)
                return changed;

            for (int i = 0; i < providerConfig.Screens.Count; i++)
            {
                var screen = providerConfig.Screens[i];
                if (screen == null)
                    continue;

                long oreScannerReferenceId = screen.OreScannerReferenceId;
                if (TryRemap(oreScannerReferenceId, remap, out oreScannerReferenceId))
                {
                    screen.OreScannerReferenceId = oreScannerReferenceId;
                    changed = true;
                }

                var withBlocks = screen as ScreenConfigWithBlocks;
                if (withBlocks != null)
                {
                    var selectedBlocks = withBlocks.SelectedBlocks;
                    if (RemapArray(ref selectedBlocks, remap))
                    {
                        withBlocks.SelectedBlocks = selectedBlocks;
                        changed = true;
                    }
                }

                var withReference = screen as IConfigWithReferenceBlock;
                if (withReference != null)
                {
                    long referenceBlock = withReference.ReferenceBlock;
                    if (TryRemap(referenceBlock, remap, out referenceBlock))
                    {
                        withReference.ReferenceBlock = referenceBlock;
                        changed = true;
                    }
                }
            }

            return changed;
        }

        static bool RemapArray(ref long[] values, Dictionary<long, long> remap)
        {
            if (values == null || values.Length == 0)
                return false;

            bool changed = false;
            var remapped = new List<long>(values.Length);

            for (int i = 0; i < values.Length; i++)
            {
                long value = values[i];
                long mapped;
                if (TryRemap(value, remap, out mapped))
                {
                    value = mapped;
                    changed = true;
                }

                if (value != 0 && !remapped.Contains(value))
                    remapped.Add(value);
            }

            if (!changed && remapped.Count == values.Length)
                return false;

            values = remapped.ToArray();
            return true;
        }

        static bool TryRemap(long value, Dictionary<long, long> remap, out long mapped)
        {
            if (value != 0 && remap != null && remap.TryGetValue(value, out mapped))
                return true;

            mapped = value;
            return false;
        }
    }
}
