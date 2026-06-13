using System.Collections.Generic;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.GridData
{
    /// <summary>
    /// Per-block snapshot of a refinery/assembler input (index 0) and output (index 1) inventories,
    /// already aggregated and sorted by amount (descending). Built and cached by <see cref="GridLogic"/>.
    /// </summary>
    public sealed class ProductionBlockItems
    {
        public ProductionBlockItems(long entityId, string name)
        {
            EntityId = entityId;
            Name = name;
        }

        public long EntityId { get; private set; }
        public string Name { get; private set; }
        public readonly List<KeyValuePair<MyItemType, double>> Input = new List<KeyValuePair<MyItemType, double>>();
        public readonly List<KeyValuePair<MyItemType, double>> Output = new List<KeyValuePair<MyItemType, double>>();
    }
}
