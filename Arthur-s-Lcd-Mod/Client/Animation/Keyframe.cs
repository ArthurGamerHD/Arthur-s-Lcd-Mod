using System;
using ArgumentOutOfRangeException = LcdMod.Common.ArgumentOutOfRangeException;

namespace LcdMod.Client.Animation
{
    /// <summary>
    /// Float keyframe convenience type.
    /// </summary>
    public sealed class Keyframe : Keyframe<float>
    {
        /// <summary>
        /// Creates a keyframe with an explicit starting value.
        /// </summary>
        public Keyframe(
            Action<float> setter,
            float from,
            float to,
            int durationFrames,
            EasingMode easingMode = EasingMode.Linear)
            : base(setter, from, to, durationFrames, easingMode, AnimationInterpolators.Float)
        {
        }

        /// <summary>
        /// Creates a keyframe whose starting value is captured from getter when
        /// this keyframe starts playing. This is useful for later steps in a
        /// sequence and for replacing an animation without a visual jump.
        /// </summary>
        public Keyframe(
            Func<float> getter,
            Action<float> setter,
            float to,
            int durationFrames,
            EasingMode easingMode = EasingMode.Linear)
            : base(getter, setter, to, durationFrames, easingMode, AnimationInterpolators.Float)
        {
        }
    }

    /// <summary>
    /// Interpolates one value during a sequential animation step.
    /// </summary>
    public class Keyframe<T> : IAnimationStep
    {
        readonly Func<T> _startValueProvider;
        readonly Action<T> _setter;
        readonly T _explicitFrom;
        readonly T _to;
        readonly EasingMode _easingMode;
        readonly AnimationInterpolator<T> _interpolator;
        readonly bool _captureStartValue;

        T _from;

        /// <summary>
        /// Creates a keyframe with an explicit starting value.
        /// </summary>
        public Keyframe(
            Action<T> setter,
            T from,
            T to,
            int durationFrames,
            EasingMode easingMode,
            AnimationInterpolator<T> interpolator)
        {
            if (setter == null)
                throw new ArgumentNullException(nameof(setter));
            if (interpolator == null)
                throw new ArgumentNullException(nameof(interpolator));
            if (durationFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(durationFrames));

            _setter = setter;
            _explicitFrom = from;
            _to = to;
            _easingMode = easingMode;
            _interpolator = interpolator;
            _captureStartValue = false;
            DurationFrames = durationFrames;
        }

        /// <summary>
        /// Creates a keyframe whose starting value is captured when this step
        /// starts playing, rather than when the sequence is constructed.
        /// </summary>
        public Keyframe(
            Func<T> getter,
            Action<T> setter,
            T to,
            int durationFrames,
            EasingMode easingMode,
            AnimationInterpolator<T> interpolator)
        {
            if (getter == null)
                throw new ArgumentNullException(nameof(getter));
            if (setter == null)
                throw new ArgumentNullException(nameof(setter));
            if (interpolator == null)
                throw new ArgumentNullException(nameof(interpolator));
            if (durationFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(durationFrames));

            _startValueProvider = getter;
            _setter = setter;
            _to = to;
            _easingMode = easingMode;
            _interpolator = interpolator;
            _captureStartValue = true;
            DurationFrames = durationFrames;
        }

        public int DurationFrames { get; private set; }

        public bool RequiresRedraw => true;

        public void Begin()
        {
            _from = _captureStartValue ? _startValueProvider() : _explicitFrom;
        }

        public void Apply(float progress)
        {
            float easedProgress = Easings.Apply(_easingMode, progress);
            _setter(_interpolator(_from, _to, easedProgress));
        }
    }
}
