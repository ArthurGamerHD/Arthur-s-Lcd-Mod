using System.Collections.Generic;

namespace LcdMod.Client.GridData
{
    /// <summary>
    /// Matches detailed inventory notifications with their trailing generic notification.
    /// </summary>
    internal sealed class InventoryEventTracker<TInventory>
    {
        readonly HashSet<TInventory> _awaitingContentsChanged = new HashSet<TInventory>();

        public void RecordDetailedChange(TInventory inventory)
        {
            _awaitingContentsChanged.Add(inventory);
        }

        public bool CompleteContentsChange(TInventory inventory)
        {
            return _awaitingContentsChanged.Remove(inventory);
        }

        public void Forget(TInventory inventory)
        {
            _awaitingContentsChanged.Remove(inventory);
        }

        public void Clear()
        {
            _awaitingContentsChanged.Clear();
        }
    }
}
