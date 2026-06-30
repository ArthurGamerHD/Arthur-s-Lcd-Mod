using System;
using System.Collections.Generic;

namespace LcdMod.Client.Animation
{
    /// <summary>
    /// Advances sequential animations from one shared frame callback and
    /// coalesces all visual changes into one redraw request per tick.
    /// </summary>
    public sealed class AnimationController : IDisposable
    {
        sealed class RunningAnimation
        {
            public object Owner;
            public Action Invalidate;
            public string Channel;
            public IAnimationStep[] Steps;
            public AnimationHandle Handle;
            public int StepIndex;
            public bool StepStarted;
            public long StepStartFrame;
        }

        readonly Func<long> _getFrame;
        readonly Action<Action> _scheduleNextFrame;
        readonly Action _requestRedraw;
        readonly List<RunningAnimation> _animations = new List<RunningAnimation>();
        readonly Action _tickAction;

        bool _tickQueued;
        bool _disposed;

        public AnimationController(
            Func<long> getFrame,
            Action<Action> scheduleNextFrame,
            Action requestRedraw)
        {
            if (getFrame == null)
                throw new ArgumentNullException(nameof(getFrame));
            if (scheduleNextFrame == null)
                throw new ArgumentNullException(nameof(scheduleNextFrame));
            if (requestRedraw == null)
                throw new ArgumentNullException(nameof(requestRedraw));

            _getFrame = getFrame;
            _scheduleNextFrame = scheduleNextFrame;
            _requestRedraw = requestRedraw;
            _tickAction = Tick;
        }

        public int ActiveCount => _animations.Count;

        public bool IsDisposed => _disposed;

        /// <summary>
        /// Runs all steps in sequence. Unnamed animations are allowed to run in
        /// parallel with other animations owned by the same object.
        /// </summary>
        public AnimationHandle Run(
            object owner,
            Action invalidate,
            params IAnimationStep[] steps)
        {
            return Run(owner, invalidate, null, AnimationConflict.Allow, steps);
        }

        /// <summary>
        /// Runs a named sequence, replacing another sequence on the same owner
        /// and channel by default.
        /// </summary>
        public AnimationHandle Run(
            object owner,
            Action invalidate,
            string channel,
            params IAnimationStep[] steps)
        {
            return Run(owner, invalidate, channel, AnimationConflict.Replace, steps);
        }

        public AnimationHandle Run(
            object owner,
            Action invalidate,
            string channel,
            AnimationConflict conflict,
            params IAnimationStep[] steps)
        {
            ThrowIfDisposed();

            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (invalidate == null)
                throw new ArgumentNullException(nameof(invalidate));
            if (steps == null)
                throw new ArgumentNullException(nameof(steps));

            IAnimationStep[] sequence = CopyAndValidateSteps(steps);

            if (!string.IsNullOrEmpty(channel) && conflict != AnimationConflict.Allow)
            {
                RunningAnimation existing = Find(owner, channel);
                if (existing != null)
                {
                    if (conflict == AnimationConflict.Ignore)
                        return existing.Handle;

                    Cancel(existing, false);
                }
            }

            var handle = new AnimationHandle(this);
            if (sequence.Length == 0)
            {
                handle.Detach(AnimationState.Completed);
                return handle;
            }

            _animations.Add(new RunningAnimation
            {
                Owner = owner,
                Invalidate = invalidate,
                Channel = channel,
                Steps = sequence,
                Handle = handle,
                StepIndex = 0
            });

            QueueTick();
            return handle;
        }

        public bool HasAnimations(object owner)
        {
            if (owner == null)
                return false;

            for (int i = 0; i < _animations.Count; i++)
            {
                if (ReferenceEquals(_animations[i].Owner, owner))
                    return true;
            }

            return false;
        }

        public bool HasAnimation(object owner, string channel)
        {
            return owner != null && Find(owner, channel) != null;
        }

        public void Cancel(AnimationHandle handle)
        {
            if (handle == null || !handle.IsRunning)
                return;

            RunningAnimation animation = Find(handle);
            if (animation == null)
                return;

            Cancel(animation, true);
        }

        public void Complete(AnimationHandle handle)
        {
            if (handle == null || !handle.IsRunning)
                return;

            RunningAnimation animation = Find(handle);
            if (animation == null)
                return;

            bool changed = CompleteRemainingSteps(animation);
            _animations.Remove(animation);
            animation.Handle.Detach(AnimationState.Completed);

            if (changed)
            {
                animation.Invalidate();
                _requestRedraw();
            }
        }

        public void CancelOwner(object owner)
        {
            CancelOwner(owner, true);
        }

        public void CancelOwner(object owner, bool requestRedraw)
        {
            if (owner == null)
                return;

            bool changed = false;
            for (int i = _animations.Count - 1; i >= 0; i--)
            {
                RunningAnimation animation = _animations[i];
                if (!ReferenceEquals(animation.Owner, owner))
                    continue;

                Cancel(animation, false);
                changed = true;
            }

            if (changed && requestRedraw)
                _requestRedraw();
        }

        public void Cancel(object owner, string channel)
        {
            Cancel(owner, channel, true);
        }

        public void Cancel(object owner, string channel, bool requestRedraw)
        {
            if (owner == null)
                return;

            bool changed = false;
            for (int i = _animations.Count - 1; i >= 0; i--)
            {
                RunningAnimation animation = _animations[i];
                if (!ReferenceEquals(animation.Owner, owner) ||
                    !string.Equals(animation.Channel, channel, StringComparison.Ordinal))
                    continue;

                Cancel(animation, false);
                changed = true;
            }

            if (changed && requestRedraw)
                _requestRedraw();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _tickQueued = false;

            for (int i = 0; i < _animations.Count; i++)
                _animations[i].Handle.Detach(AnimationState.Cancelled);

            _animations.Clear();
        }

        void Tick()
        {
            _tickQueued = false;
            if (_disposed || _animations.Count == 0)
                return;

            long currentFrame = _getFrame();
            bool redraw = false;

            for (int i = _animations.Count - 1; i >= 0; i--)
            {
                RunningAnimation animation = _animations[i];
                bool animationChanged;
                bool completed = Advance(animation, currentFrame, out animationChanged);

                if (animationChanged)
                {
                    animation.Invalidate();
                    redraw = true;
                }

                if (!completed)
                    continue;

                _animations.RemoveAt(i);
                animation.Handle.Detach(AnimationState.Completed);
            }

            if (redraw)
                _requestRedraw();

            if (_animations.Count > 0)
                QueueTick();
        }

        bool Advance(RunningAnimation animation, long currentFrame, out bool changed)
        {
            changed = false;
            long carryFrames = 0L;

            while (animation.StepIndex < animation.Steps.Length)
            {
                IAnimationStep step = animation.Steps[animation.StepIndex];

                if (!animation.StepStarted)
                {
                    step.Begin();
                    animation.StepStarted = true;
                    animation.StepStartFrame = currentFrame - carryFrames;
                }

                long elapsedFrames = currentFrame - animation.StepStartFrame;
                if (elapsedFrames < 0L)
                    elapsedFrames = 0L;

                int durationFrames = step.DurationFrames;
                if (durationFrames <= 0)
                {
                    step.Apply(1f);
                    changed |= step.RequiresRedraw;
                    animation.StepIndex++;
                    animation.StepStarted = false;
                    continue;
                }

                if (elapsedFrames < durationFrames)
                {
                    step.Apply(elapsedFrames / (float)durationFrames);
                    changed |= step.RequiresRedraw;
                    return false;
                }

                step.Apply(1f);
                changed |= step.RequiresRedraw;
                carryFrames = elapsedFrames - durationFrames;
                animation.StepIndex++;
                animation.StepStarted = false;
            }

            return true;
        }

        bool CompleteRemainingSteps(RunningAnimation animation)
        {
            bool changed = false;

            while (animation.StepIndex < animation.Steps.Length)
            {
                IAnimationStep step = animation.Steps[animation.StepIndex];
                if (!animation.StepStarted)
                    step.Begin();

                step.Apply(1f);
                changed |= step.RequiresRedraw;
                animation.StepIndex++;
                animation.StepStarted = false;
            }

            return changed;
        }

        void QueueTick()
        {
            if (_disposed || _tickQueued || _animations.Count == 0)
                return;

            _tickQueued = true;
            _scheduleNextFrame(_tickAction);
        }

        void Cancel(RunningAnimation animation, bool requestRedraw)
        {
            _animations.Remove(animation);
            animation.Handle.Detach(AnimationState.Cancelled);
            animation.Invalidate();

            if (requestRedraw)
                _requestRedraw();
        }

        RunningAnimation Find(AnimationHandle handle)
        {
            for (int i = 0; i < _animations.Count; i++)
            {
                if (ReferenceEquals(_animations[i].Handle, handle))
                    return _animations[i];
            }

            return null;
        }

        RunningAnimation Find(object owner, string channel)
        {
            for (int i = 0; i < _animations.Count; i++)
            {
                RunningAnimation animation = _animations[i];
                if (ReferenceEquals(animation.Owner, owner) &&
                    string.Equals(animation.Channel, channel, StringComparison.Ordinal))
                    return animation;
            }

            return null;
        }

        static IAnimationStep[] CopyAndValidateSteps(IAnimationStep[] steps)
        {
            var copy = new IAnimationStep[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                IAnimationStep step = steps[i];
                if (step == null)
                    throw new ArgumentException("Animation steps cannot contain null values.", nameof(steps));

                copy[i] = step;
            }

            return copy;
        }

        void ThrowIfDisposed()
        {
            if (_disposed)
                throw new InvalidOperationException("AnimationController is disposed.");
        }
    }
}
