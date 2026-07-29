using System;
using System.Collections.Generic;
using System.Globalization;
using LcdMod.Client.Animation;
using LcdMod.Client.Gui.ControlsTemplates;

namespace LcdMod.Client.Gui.Styling
{
    internal abstract class StyleAnimationBase
    {
        protected StyleAnimationBase(StylePropertyBase property)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            Property = property;
            Channel = GetChannel(property.Id);
        }

        public StylePropertyBase Property { get; private set; }

        protected string Channel { get; private set; }

        internal static string GetChannel(int propertyId)
        {
            return "StyleAnimation:" + propertyId.ToString(CultureInfo.InvariantCulture);
        }

        public abstract void Start(
            ControlTemplate target,
            StyleState previousState,
            StyleState currentState);
    }

    internal sealed class StyleAnimation<TValue> : StyleAnimationBase
    {
        readonly StyleProperty<TValue> _property;
        readonly int _durationFrames;
        readonly EasingMode _easingMode;
        readonly AnimationInterpolator<TValue> _interpolator;

        public StyleAnimation(
            StyleProperty<TValue> property,
            int durationFrames,
            EasingMode easingMode,
            AnimationInterpolator<TValue> interpolator)
            : base(property)
        {
            if (durationFrames < 0)
                throw new Exception(nameof(durationFrames));
            if (interpolator == null)
                throw new ArgumentNullException(nameof(interpolator));

            _property = property;
            _durationFrames = durationFrames;
            _easingMode = easingMode;
            _interpolator = interpolator;
        }

        public override void Start(
            ControlTemplate target,
            StyleState previousState,
            StyleState currentState)
        {
            if (target == null)
                return;

            TValue from = target.GetStyleAnimationStartValue(_property, previousState);
            TValue to = target.ResolveStyleValueForState(_property, currentState);

            target.CancelAnimation(Channel, false);

            if (_durationFrames <= 0 ||
                EqualityComparer<TValue>.Default.Equals(from, to) ||
                target.AnimationController == null ||
                target.AnimationController.IsDisposed)
            {
                target.ClearAnimatedStyleValue(_property);
                return;
            }

            // Install the old/current value immediately so the render which
            // observed the state change does not snap to the destination.
            target.SetAnimatedStyleValue(_property, from);

            target.RunAnimation(
                Channel,
                AnimationConflict.Replace,
                new Keyframe<TValue>(
                    () => target.GetStyleAnimationCurrentValue(_property, previousState),
                    value => target.SetAnimatedStyleValue(_property, value),
                    to,
                    _durationFrames,
                    _easingMode,
                    _interpolator),
                new ActionKeyframe(
                    () => target.ClearAnimatedStyleValue(_property),
                    false));
        }
    }

    internal sealed class StyleAnimationSet
    {
        readonly Dictionary<int, StyleAnimationBase> _animations =
            new Dictionary<int, StyleAnimationBase>();

        public void Set<TValue>(
            StyleProperty<TValue> property,
            int durationFrames,
            EasingMode easingMode,
            AnimationInterpolator<TValue> interpolator)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            _animations[property.Id] = new StyleAnimation<TValue>(
                property,
                durationFrames,
                easingMode,
                interpolator);
        }

        public void CopyTo(Dictionary<int, StyleAnimationBase> destination)
        {
            if (destination == null)
                return;

            foreach (KeyValuePair<int, StyleAnimationBase> pair in _animations)
                destination[pair.Key] = pair.Value;
        }
    }
}
