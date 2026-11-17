using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;

namespace LcdMod.Client.Apps.Radar
{
    // Antennas, beacons and laser antennas per grid, so a radar scan does not enumerate every block of every grid.
    internal sealed class RadarSignalBlockCache
    {
        sealed class Entry
        {
            public IMyCubeGrid Grid;
            public readonly List<IMyFunctionalBlock> Blocks = new List<IMyFunctionalBlock>();
            public bool Dirty = true;
            public bool Seen;
            public Action<IMySlimBlock> BlockChanged;
        }

        readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();
        readonly List<IMySlimBlock> _slimBuffer = new List<IMySlimBlock>();
        readonly List<long> _stale = new List<long>();

        public void BeginScan()
        {
            foreach (var entry in _entries.Values)
                entry.Seen = false;
        }

        public List<IMyFunctionalBlock> Get(IMyCubeGrid grid)
        {
            Entry entry;
            if (!_entries.TryGetValue(grid.EntityId, out entry) || entry.Grid != grid)
            {
                if (entry != null)
                    Unsubscribe(entry);

                entry = new Entry { Grid = grid };
                var captured = entry;
                entry.BlockChanged = slim =>
                {
                    if (IsSignalBlock(slim?.FatBlock))
                        captured.Dirty = true;
                };
                grid.OnBlockAdded += entry.BlockChanged;
                grid.OnBlockRemoved += entry.BlockChanged;
                _entries[grid.EntityId] = entry;
            }

            entry.Seen = true;
            if (entry.Dirty)
                Rebuild(entry);
            return entry.Blocks;
        }

        public void EndScan()
        {
            _stale.Clear();
            foreach (var kv in _entries)
            {
                if (!kv.Value.Seen || kv.Value.Grid.Closed)
                    _stale.Add(kv.Key);
            }

            foreach (var id in _stale)
            {
                Unsubscribe(_entries[id]);
                _entries.Remove(id);
            }
        }

        public void Clear()
        {
            foreach (var entry in _entries.Values)
                Unsubscribe(entry);
            _entries.Clear();
        }

        void Rebuild(Entry entry)
        {
            entry.Dirty = false;
            entry.Blocks.Clear();
            _slimBuffer.Clear();
            entry.Grid.GetBlocks(_slimBuffer, slim => IsSignalBlock(slim.FatBlock));
            foreach (var slim in _slimBuffer)
                entry.Blocks.Add((IMyFunctionalBlock)slim.FatBlock);
            _slimBuffer.Clear();
        }

        static void Unsubscribe(Entry entry)
        {
            entry.Grid.OnBlockAdded -= entry.BlockChanged;
            entry.Grid.OnBlockRemoved -= entry.BlockChanged;
        }

        static bool IsSignalBlock(IMyCubeBlock block) =>
            block is IMyRadioAntenna || block is IMyBeacon || block is IMyLaserAntenna;
    }
}
