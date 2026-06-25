using System.Collections.Generic;

namespace LcdMod.Common.Config.Components
{
    public static class ComponentConfigEntityReferences
    {
        public static void CollectPinnedEntityIds(IComponentContainer config, ICollection<long> entityIds)
        {
            if (config == null || entityIds == null || config.Components == null)
                return;

            for (int i = 0; i < config.Components.Count; i++)
            {
                var entry = config.Components[i];
                if (entry == null)
                    continue;

                var blocks = entry.Value as BlockSelectionConfigComponent;
                if (blocks != null)
                    AddValues(blocks.SelectedBlocks, entityIds);

                var reference = entry.Value as BlockReferenceConfigComponent;
                if (reference != null && reference.EntityId != 0)
                    entityIds.Add(reference.EntityId);

                var tabs = entry.Value as TabContainerConfigComponent;
                if (tabs == null || tabs.Apps == null)
                    continue;

                for (int appIndex = 0; appIndex < tabs.Apps.Count; appIndex++)
                    CollectPinnedEntityIds(tabs.Apps[appIndex], entityIds);
            }
        }

        public static bool RemapEntityReferences(IComponentContainer config, Dictionary<long, long> remap)
        {
            if (config == null || config.Components == null)
                return false;

            bool changed = false;

            for (int i = 0; i < config.Components.Count; i++)
            {
                var entry = config.Components[i];
                if (entry == null)
                    continue;

                var blocks = entry.Value as BlockSelectionConfigComponent;
                if (blocks != null)
                {
                    var selectedBlocks = blocks.SelectedBlocks;
                    if (RemapArray(ref selectedBlocks, remap))
                    {
                        blocks.SelectedBlocks = selectedBlocks;
                        changed = true;
                    }
                }

                var reference = entry.Value as BlockReferenceConfigComponent;
                if (reference != null)
                {
                    long entityId = reference.EntityId;
                    if (TryRemap(entityId, remap, out entityId))
                    {
                        reference.EntityId = entityId;
                        changed = true;
                    }
                }

                var tabs = entry.Value as TabContainerConfigComponent;
                if (tabs == null || tabs.Apps == null)
                    continue;

                for (int appIndex = 0; appIndex < tabs.Apps.Count; appIndex++)
                    changed |= RemapEntityReferences(tabs.Apps[appIndex], remap);
            }

            return changed;
        }

        static void AddValues(long[] values, ICollection<long> entityIds)
        {
            if (values == null)
                return;

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != 0)
                    entityIds.Add(values[i]);
            }
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
