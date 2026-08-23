using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace LcdMod.Client.GridData
{
    /// <summary>
    /// Applies a deterministic per-frame inventory-scan budget across all grids and scoped views.
    /// Each resumable collection step scans at most one inventory.
    /// </summary>
    internal static class InventoryWorkScheduler
    {
        const int MAX_INVENTORY_SCANS_PER_FRAME = 8;
        const int MAX_DEQUEUES_PER_FRAME = 64;
        const int URGENT_BURST_SIZE = 4;

        struct WorkItem
        {
            public TypedItemCollection Collection;
            public int Generation;
            public long DueFrame;
            public bool Urgent;
        }

        static readonly Queue<WorkItem> Urgent = new Queue<WorkItem>();
        static readonly Queue<WorkItem> Background = new Queue<WorkItem>();

        internal static void Enqueue(
            TypedItemCollection collection,
            int generation,
            bool urgent,
            int delayFrames)
        {
            if (collection == null)
                return;

            var currentFrame = MyAPIGateway.Session != null
                ? MyAPIGateway.Session.GameplayFrameCounter
                : 0L;
            var work = new WorkItem
            {
                Collection = collection,
                Generation = generation,
                DueFrame = currentFrame + Math.Max(0, delayFrames),
                Urgent = urgent
            };
            (urgent ? Urgent : Background).Enqueue(work);
        }

        internal static void RunFrame()
        {
            if (Urgent.Count == 0 && Background.Count == 0)
                return;

            var currentFrame = MyAPIGateway.Session != null
                ? MyAPIGateway.Session.GameplayFrameCounter
                : long.MaxValue;
            var inventoryScans = 0;
            var dequeued = 0;
            var urgentBurst = 0;

            {
                while (inventoryScans < MAX_INVENTORY_SCANS_PER_FRAME && dequeued < MAX_DEQUEUES_PER_FRAME)
                {
                    WorkItem work;
                    if (urgentBurst < URGENT_BURST_SIZE && TryDequeueDue(Urgent, currentFrame, out work))
                    {
                        urgentBurst++;
                    }
                    else if (TryDequeueDue(Background, currentFrame, out work))
                    {
                        urgentBurst = 0;
                    }
                    else if (TryDequeueDue(Urgent, currentFrame, out work))
                    {
                        urgentBurst = 1;
                    }
                    else
                    {
                        break;
                    }

                    dequeued++;
                    if (work.Collection == null)
                        continue;

                    var result = work.Collection.RunScheduledRecalculation(work.Generation);
                    if (result == InventoryRecalculationStep.Stale)
                        continue;

                    if (result == InventoryRecalculationStep.MoreWork)
                    {
                        inventoryScans++;
                        work.DueFrame = currentFrame;
                        (work.Urgent ? Urgent : Background).Enqueue(work);
                        continue;
                    }

                    // A completed active scan consumed its final inventory. Jobs with no scan
                    // (for example an empty collection) are intentionally free.
                    inventoryScans++;
                }
            }
        }

        static bool TryDequeueDue(Queue<WorkItem> queue, long currentFrame, out WorkItem work)
        {
            var candidates = queue.Count;
            while (candidates-- > 0)
            {
                work = queue.Dequeue();
                if (work.DueFrame <= currentFrame)
                    return true;
                queue.Enqueue(work);
            }

            work = default(WorkItem);
            return false;
        }

        internal static void Clear()
        {
            Urgent.Clear();
            Background.Clear();
        }
    }
}
