using System;
using System.Collections.Generic;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;

namespace LcdMod.Client.Ftue
{
    internal static class FtueTipSlotScheduler
    {
        sealed class PendingActivation
        {
            public FtueTip Tip;
            public Func<bool> Activate;
        }

        sealed class SlotState
        {
            public FtueTip ActiveTip;
            public readonly List<PendingActivation> Pending = new List<PendingActivation>();
        }

        sealed class SurfaceSlots
        {
            public readonly SlotState Top = new SlotState();
            public readonly SlotState Bottom = new SlotState();
        }

        readonly static Dictionary<InteractiveSurfaceScript, SurfaceSlots> Slots =
            new Dictionary<InteractiveSurfaceScript, SurfaceSlots>();

        public static bool ShowOrSchedule(
            InteractiveSurfaceScript surface,
            HintPlacement placement,
            FtueTip tip,
            Func<bool> activate)
        {
            if (surface == null || tip == null || activate == null || tip.IsCompleted)
                return false;

            var surfaceSlots = GetOrCreateSurfaceSlots(surface);
            var slot = GetSlot(surfaceSlots, placement);
            if (slot.ActiveTip != null && !ReferenceEquals(slot.ActiveTip, tip))
            {
                QueueOrReplace(slot, tip, activate);
                return false;
            }

            RemovePending(slot, tip);
            if (TryActivate(surface, slot, tip, activate))
                return true;

            if (slot.ActiveTip == null)
                ActivateNext(surface, placement, surfaceSlots, slot);
            CleanupSurfaceIfEmpty(surface, surfaceSlots);
            return false;
        }

        public static bool IsActive(
            InteractiveSurfaceScript surface,
            HintPlacement placement,
            FtueTip tip)
        {
            SurfaceSlots surfaceSlots;
            return surface != null &&
                   tip != null &&
                   Slots.TryGetValue(surface, out surfaceSlots) &&
                   ReferenceEquals(GetSlot(surfaceSlots, placement).ActiveTip, tip);
        }

        public static void Cancel(
            InteractiveSurfaceScript surface,
            HintPlacement placement,
            FtueTip tip)
        {
            SurfaceSlots surfaceSlots;
            if (surface == null || tip == null || !Slots.TryGetValue(surface, out surfaceSlots))
                return;

            var slot = GetSlot(surfaceSlots, placement);
            RemovePending(slot, tip);
            if (ReferenceEquals(slot.ActiveTip, tip))
            {
                slot.ActiveTip = null;
                ActivateNext(surface, placement, surfaceSlots, slot);
            }

            CleanupSurfaceIfEmpty(surface, surfaceSlots);
        }

        public static void Release(
            InteractiveSurfaceScript surface,
            HintPlacement placement,
            FtueTip tip)
        {
            SurfaceSlots surfaceSlots;
            if (surface == null || tip == null || !Slots.TryGetValue(surface, out surfaceSlots))
                return;

            var slot = GetSlot(surfaceSlots, placement);
            if (!ReferenceEquals(slot.ActiveTip, tip))
            {
                RemovePending(slot, tip);
                CleanupSurfaceIfEmpty(surface, surfaceSlots);
                return;
            }

            slot.ActiveTip = null;
            ActivateNext(surface, placement, surfaceSlots, slot);
            CleanupSurfaceIfEmpty(surface, surfaceSlots);
        }

        public static void Clear()
        {
            Slots.Clear();
        }

        static void QueueOrReplace(SlotState slot, FtueTip tip, Func<bool> activate)
        {
            for (int i = 0; i < slot.Pending.Count; i++)
            {
                var pending = slot.Pending[i];
                if (!ReferenceEquals(pending.Tip, tip))
                    continue;

                pending.Activate = activate;
                return;
            }

            slot.Pending.Add(new PendingActivation
            {
                Tip = tip,
                Activate = activate
            });
        }

        static void ActivateNext(
            InteractiveSurfaceScript surface,
            HintPlacement placement,
            SurfaceSlots surfaceSlots,
            SlotState slot)
        {
            while (slot.Pending.Count > 0)
            {
                var pending = slot.Pending[0];
                slot.Pending.RemoveAt(0);

                if (pending.Tip == null || pending.Activate == null || pending.Tip.IsCompleted)
                    continue;

                if (TryActivate(surface, slot, pending.Tip, pending.Activate))
                    return;

                if (slot.ActiveTip != null)
                    return;
            }

            CleanupSurfaceIfEmpty(surface, surfaceSlots);
        }

        static void RemovePending(SlotState slot, FtueTip tip)
        {
            for (int i = slot.Pending.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(slot.Pending[i].Tip, tip))
                    slot.Pending.RemoveAt(i);
            }
        }

        static bool TryActivate(
            InteractiveSurfaceScript surface,
            SlotState slot,
            FtueTip tip,
            Func<bool> activate)
        {
            slot.ActiveTip = tip;
            try
            {
                if (activate())
                    return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, surface);
            }

            if (ReferenceEquals(slot.ActiveTip, tip))
                slot.ActiveTip = null;
            return false;
        }

        static SurfaceSlots GetOrCreateSurfaceSlots(InteractiveSurfaceScript surface)
        {
            SurfaceSlots slots;
            if (Slots.TryGetValue(surface, out slots))
                return slots;

            slots = new SurfaceSlots();
            Slots[surface] = slots;
            return slots;
        }

        static SlotState GetSlot(SurfaceSlots slots, HintPlacement placement)
        {
            return placement == HintPlacement.Top
                ? slots.Top
                : slots.Bottom;
        }

        static void CleanupSurfaceIfEmpty(InteractiveSurfaceScript surface, SurfaceSlots slots)
        {
            if (surface == null || slots == null)
                return;

            if (IsEmpty(slots.Top) && IsEmpty(slots.Bottom))
                Slots.Remove(surface);
        }

        static bool IsEmpty(SlotState slot)
        {
            return slot.ActiveTip == null && slot.Pending.Count == 0;
        }
    }
}
