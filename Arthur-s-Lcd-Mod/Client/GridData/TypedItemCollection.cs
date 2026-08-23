using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using LcdMod.Common.Mvvm;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI.Ingame;

namespace LcdMod.Client.GridData
{
    /// <summary>
    /// Observable item totals backed directly by live game inventories.
    /// Expensive authoritative scans are resumed one inventory at a time so the scheduler
    /// can bound work by inventory count instead of elapsed wall-clock time.
    /// </summary>
    public sealed class TypedItemCollection
    {
        readonly HashSet<MyInventoryBase> _inventories = new HashSet<MyInventoryBase>();
        readonly List<MyInventoryBase> _inventoryOrder = new List<MyInventoryBase>();
        readonly InventoryEventTracker<MyInventoryBase> _eventTracker =
            new InventoryEventTracker<MyInventoryBase>();
        readonly HashSet<MyInventoryBase> _pendingChangedInventories =
            new HashSet<MyInventoryBase>();
        readonly HashSet<MyInventoryBase> _processingChangedInventories =
            new HashSet<MyInventoryBase>();
        readonly HashSet<MyItemType> _pendingItemTypes = new HashSet<MyItemType>();
        readonly HashSet<MyItemType> _processingItemTypes = new HashSet<MyItemType>();
        readonly HashSet<MyItemType> _aggregateItemTypes = new HashSet<MyItemType>();
        readonly Dictionary<MyItemType, MyFixedPoint> _scanAmounts =
            new Dictionary<MyItemType, MyFixedPoint>();

        bool _pendingFullRecalculation;
        bool _scheduled;
        bool _scheduledUrgent;
        bool _workActive;
        bool _workFullRecalculation;
        int _workInventoryIndex;
        int _scheduleGeneration;
        int _consecutiveFailures;

        public readonly ObservableDictionary<MyItemType, MyFixedPoint> BySubtype =
            new ObservableDictionary<MyItemType, MyFixedPoint>();

        public int Count => BySubtype.Count;
        public event Action<MyInventoryBase> InventoryChanged;

        public MyFixedPoint GetAmount(MyItemType itemType)
        {
            MyFixedPoint amount;
            return BySubtype.TryGetValue(itemType, out amount) ? amount : MyFixedPoint.Zero;
        }

        public void AddInventory(MyInventoryBase inventory)
        {
            if (inventory == null || !_inventories.Add(inventory))
                return;

            _inventoryOrder.Add(inventory);
            _pendingChangedInventories.Add(inventory);
            RequestFullRecalculation(false);
        }

        public void RemoveInventory(MyInventoryBase inventory)
        {
            if (inventory == null || !_inventories.Remove(inventory))
                return;

            _inventoryOrder.Remove(inventory);
            _eventTracker.Forget(inventory);
            _pendingChangedInventories.Add(inventory);
            RequestFullRecalculation(true);
        }

        public void ScheduleRecalculation(MyInventoryBase inventory, MyItemType itemType)
        {
            if (inventory == null || !_inventories.Contains(inventory))
                return;

            _eventTracker.RecordDetailedChange(inventory);
            _pendingChangedInventories.Add(inventory);
            if (!_pendingFullRecalculation)
                _pendingItemTypes.Add(itemType);
            SchedulePendingRecalculation(true, 0);
        }

        public void ScheduleInventoryRecalculation(MyInventoryBase inventory)
        {
            if (inventory == null || !_inventories.Contains(inventory))
                return;

            var hadDetailedChange = _eventTracker.CompleteContentsChange(inventory);
            _pendingChangedInventories.Add(inventory);
            if (!hadDetailedChange)
            {
                _pendingFullRecalculation = true;
                _pendingItemTypes.Clear();
            }

            SchedulePendingRecalculation(true, 0);
        }

        /// <summary>
        /// Starts an authoritative reconciliation. The actual scan is a resumable job and
        /// advances by one inventory per scheduler work unit.
        /// </summary>
        public void ReconcileNextInventory()
        {
            if (_inventoryOrder.Count == 0)
                return;

            for (var i = 0; i < _inventoryOrder.Count; i++)
                _pendingChangedInventories.Add(_inventoryOrder[i]);
            RequestFullRecalculation(false);
        }

        void RequestFullRecalculation(bool urgent)
        {
            _pendingFullRecalculation = true;
            _pendingItemTypes.Clear();
            SchedulePendingRecalculation(urgent, 0);
        }

        void SchedulePendingRecalculation(bool urgent, int delayFrames)
        {
            if (_scheduled)
            {
                _scheduledUrgent |= urgent;
                return;
            }

            _scheduled = true;
            _scheduledUrgent = urgent;
            _scheduleGeneration++;
            InventoryWorkScheduler.Enqueue(this, _scheduleGeneration, urgent, delayFrames);
        }

        internal InventoryRecalculationStep RunScheduledRecalculation(int generation)
        {
            if (!_scheduled || generation != _scheduleGeneration)
                return InventoryRecalculationStep.Stale;

            try
            {
                if (!_workActive)
                    BeginScheduledWork();

                if (!_workActive)
                {
                    CompleteScheduledWork();
                    return InventoryRecalculationStep.Completed;
                }

                {
                    ScanCurrentInventory();
                }

                _workInventoryIndex++;
                if (_workInventoryIndex < _inventoryOrder.Count)
                    return InventoryRecalculationStep.MoreWork;

                ApplyCompletedScan();
                _consecutiveFailures = 0;
                foreach (var inventory in _processingChangedInventories)
                    RaiseInventoryChanged(inventory);
                CompleteScheduledWork();
                return InventoryRecalculationStep.Completed;
            }
            catch (Exception error)
            {
                RestoreFailedWork();
                _consecutiveFailures++;
                LogRecalculationError(null, error);
                _scheduled = false;
                _scheduledUrgent = false;
                SchedulePendingRecalculation(true, GetRetryDelayFrames(_consecutiveFailures));
                return InventoryRecalculationStep.Completed;
            }
        }

        void BeginScheduledWork()
        {
            _workFullRecalculation = _pendingFullRecalculation;
            _pendingFullRecalculation = false;

            _processingItemTypes.Clear();
            _processingItemTypes.UnionWith(_pendingItemTypes);
            _pendingItemTypes.Clear();

            _processingChangedInventories.Clear();
            _processingChangedInventories.UnionWith(_pendingChangedInventories);
            _pendingChangedInventories.Clear();
            foreach (var inventory in _processingChangedInventories)
                _eventTracker.Forget(inventory);

            _scanAmounts.Clear();
            _workInventoryIndex = 0;
            _workActive = _workFullRecalculation || _processingItemTypes.Count > 0;
        }

        void ScanCurrentInventory()
        {
            if (_workInventoryIndex < 0 || _workInventoryIndex >= _inventoryOrder.Count)
                return;

            var inventory = _inventoryOrder[_workInventoryIndex];
            var items = inventory.GetItems();
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items[itemIndex];
                if (item.Content == null)
                    continue;

                var itemType = (MyItemType)item.Content;
                if (!_workFullRecalculation && !_processingItemTypes.Contains(itemType))
                    continue;

                MyFixedPoint currentAmount;
                _scanAmounts.TryGetValue(itemType, out currentAmount);
                _scanAmounts[itemType] = MyFixedPoint.AddSafe(currentAmount, item.Amount);
            }
        }

        void ApplyCompletedScan()
        {
            if (_workFullRecalculation)
            {
                _aggregateItemTypes.Clear();
                foreach (var itemType in BySubtype.Keys)
                    _aggregateItemTypes.Add(itemType);
                foreach (var itemType in _scanAmounts.Keys)
                    _aggregateItemTypes.Add(itemType);

                foreach (var itemType in _aggregateItemTypes)
                {
                    MyFixedPoint amount;
                    _scanAmounts.TryGetValue(itemType, out amount);
                    SetAggregateAmount(itemType, amount);
                }
                _aggregateItemTypes.Clear();
                return;
            }

            foreach (var itemType in _processingItemTypes)
            {
                MyFixedPoint amount;
                _scanAmounts.TryGetValue(itemType, out amount);
                SetAggregateAmount(itemType, amount);
            }
        }

        void CompleteScheduledWork()
        {
            _workActive = false;
            _workFullRecalculation = false;
            _workInventoryIndex = 0;
            _scanAmounts.Clear();
            _processingItemTypes.Clear();
            _processingChangedInventories.Clear();
            _scheduled = false;
            _scheduledUrgent = false;

            if (_pendingFullRecalculation ||
                _pendingItemTypes.Count > 0 ||
                _pendingChangedInventories.Count > 0)
            {
                SchedulePendingRecalculation(true, 0);
            }
        }

        void RestoreFailedWork()
        {
            _pendingFullRecalculation = true;
            _pendingItemTypes.Clear();
            _pendingChangedInventories.UnionWith(_processingChangedInventories);
            _workActive = false;
            _workFullRecalculation = false;
            _workInventoryIndex = 0;
            _scanAmounts.Clear();
            _processingItemTypes.Clear();
            _processingChangedInventories.Clear();
        }

        void SetAggregateAmount(MyItemType itemType, MyFixedPoint amount)
        {
            if (amount <= MyFixedPoint.Zero)
            {
                BySubtype.Remove(itemType);
                return;
            }

            BySubtype[itemType] = amount;
        }

        void RaiseInventoryChanged(MyInventoryBase inventory)
        {
            var handlers = InventoryChanged;
            if (handlers == null)
                return;

            foreach (Action<MyInventoryBase> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(inventory);
                }
                catch (Exception error)
                {
                    LogRecalculationError(inventory, error);
                }
            }
        }

        static int GetRetryDelayFrames(int failureCount)
        {
            if (failureCount <= 1)
                return 1;
            if (failureCount == 2)
                return 10;
            if (failureCount == 3)
                return 60;
            return 300;
        }

        static void LogRecalculationError(MyInventoryBase inventory, Exception error)
        {
            var key = "TypedItemCollection.Recalculate." +
                      (inventory != null ? inventory.GetHashCode().ToString() : "collection") + "." +
                      error.GetType().FullName;
            LogHelper.LogOnce(
                key,
                "Inventory item recalculation failed and will be retried: " + error);
        }

        public void Clear()
        {
            _scheduleGeneration++;
            _scheduled = false;
            _scheduledUrgent = false;
            _pendingFullRecalculation = false;
            _workActive = false;
            _workFullRecalculation = false;
            _workInventoryIndex = 0;
            _consecutiveFailures = 0;
            _inventories.Clear();
            _inventoryOrder.Clear();
            _eventTracker.Clear();
            _pendingChangedInventories.Clear();
            _processingChangedInventories.Clear();
            _pendingItemTypes.Clear();
            _processingItemTypes.Clear();
            _aggregateItemTypes.Clear();
            _scanAmounts.Clear();
            BySubtype.Clear();
            InventoryChanged = null;
        }
    }

    internal enum InventoryRecalculationStep
    {
        Stale,
        Completed,
        MoreWork
    }
}
