using System;

namespace LcdMod.Client.Animation
{
    /// <summary>
    /// A zero-duration action that runs at its position in the sequence.
    /// </summary>
    public sealed class ActionKeyframe : IAnimationStep
    {
        readonly Action _action;
        readonly bool _requiresRedraw;

        public ActionKeyframe(Action action, bool requiresRedraw = true)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            _action = action;
            _requiresRedraw = requiresRedraw;
        }

        public int DurationFrames => 0;

        public bool RequiresRedraw => _requiresRedraw;

        public void Begin()
        {
        }

        public void Apply(float progress)
        {
            if (progress >= 1f)
                _action();
        }
    }
}
