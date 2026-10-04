using System;

namespace LcdMod.Client.Modules.EyeTracking
{
    /// <summary>
    /// Tracks the logical input owner, independently of the physical panel used to
    /// reach it. A held button may cross a panel seam without cancelling capture.
    /// </summary>
    internal sealed class PointerInputCapture<T> where T : class
    {
        readonly Action<T> _reset;

        public PointerInputCapture(Action<T> reset)
        {
            _reset = reset;
        }

        public T Current { get; private set; }

        public void Select(T owner, bool preserveThroughGap)
        {
            if (owner == null && preserveThroughGap)
                return;
            if (ReferenceEquals(Current, owner))
                return;
            if (Current != null)
                _reset(Current);
            Current = owner;
        }
    }
}
