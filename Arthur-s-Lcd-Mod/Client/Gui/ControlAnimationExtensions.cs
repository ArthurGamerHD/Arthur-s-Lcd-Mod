using System;
using System.Collections.Generic;
using LcdMod.Client.Animation;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui
{
    internal sealed class ControlAnimationState
    {
        public readonly Dictionary<int, object> AnimatedStyleValues =
            new Dictionary<int, object>();

        public readonly Dictionary<int, StyleAnimationBase> ResolvedStyleAnimations =
            new Dictionary<int, StyleAnimationBase>();

        public readonly Dictionary<int, StyleAnimationBase> ScopeStyleAnimations =
            new Dictionary<int, StyleAnimationBase>();

        public readonly List<int> StyleAnimationPropertyIds = new List<int>();

        public bool HasStyleStateSnapshot;
        public StyleState LastStyleState;
    }

    internal static class ControlAnimationExtensions
    {
        public static AnimationHandle RunAnimation(this Control control, params IAnimationStep[] keyframes)
        {
            return control.RunAnimation(null, AnimationConflict.Allow, keyframes);
        }

        public static AnimationHandle RunAnimation(this Control control, string channel, params IAnimationStep[] keyframes)
        {
            return control.RunAnimation(channel, AnimationConflict.Replace, keyframes);
        }

        public static AnimationHandle RunAnimation(
            this Control control,
            string channel,
            AnimationConflict conflict,
            params IAnimationStep[] keyframes)
        {
            return control.RunAnimation(control.MarkDirty, channel, conflict, keyframes);
        }

        public static AnimationHandle RunAnimation(
            this Control control,
            Action invalidate,
            string channel,
            AnimationConflict conflict,
            params IAnimationStep[] keyframes)
        {
            if (control == null)
                throw new ArgumentNullException(nameof(control));

            AnimationController animationController = control.AnimationController;
            if (animationController == null)
                throw new InvalidOperationException(
                    "The control must be attached to a visual tree before starting animations.");

            return animationController.Run(
                control,
                invalidate ?? control.MarkDirty,
                channel,
                conflict,
                keyframes);
        }

        public static int ResolveAnimationFrames(this Control control, string resourceName, int fallback)
        {
            if (control == null)
                return Math.Max(0, fallback);

            ResourceKey<int> key;
            int value;
            if (!string.IsNullOrEmpty(resourceName) &&
                ResourceKey.TryGet(resourceName, out key) &&
                ScopedResourceResolver.TryResolve(control, key, out value))
            {
                return Math.Max(0, value);
            }

            return Math.Max(0, fallback);
        }

        public static void CancelAnimations(this Control control)
        {
            if (control == null)
                return;

            AnimationController animationController = control.AnimationController;
            if (animationController != null)
                animationController.CancelOwner(control);

            var controlTemplate = control as ControlTemplate;
            if (controlTemplate != null)
                controlTemplate.ResetStyleAnimations();
        }

        public static void CancelAnimation(this Control control, string channel)
        {
            control.CancelAnimation(channel, true);
        }

        public static void CancelAnimation(this Control control, string channel, bool requestRedraw)
        {
            if (control == null)
                return;

            AnimationController animationController = control.AnimationController;
            if (animationController != null)
                animationController.Cancel(control, channel, requestRedraw);
        }

        public static void CancelAnimationTree(this Control control)
        {
            if (control == null)
                return;

            control.CancelAnimationTree(control.AnimationController);
        }

        internal static void CancelAnimationTree(
            this Control control,
            AnimationController animationController)
        {
            if (control == null)
                return;

            if (animationController != null)
                animationController.CancelOwner(control, false);

            var controlTemplate = control as ControlTemplate;
            if (controlTemplate != null)
                controlTemplate.ResetStyleAnimations();

            IReadOnlyList<Control> children = control.LogicalChildren;

            if (children == null)
                return;

            for (int i = 0; i < children.Count; i++)
            {
                Control child = children[i];
                if (child != null)
                    child.CancelAnimationTree(animationController);
            }
        }

        internal static void UpdateStyleStateAnimations(this ControlTemplate control)
        {
            if (control == null)
                return;

            ControlAnimationState state = GetAnimationState(control, true);
            StyleState currentState = control.GetStyleStateForResolver();

            if (!state.HasStyleStateSnapshot)
            {
                state.HasStyleStateSnapshot = true;
                state.LastStyleState = currentState;
                return;
            }

            if (state.LastStyleState == currentState)
                return;

            StyleState previousState = state.LastStyleState;
            state.LastStyleState = currentState;

            ResolveStyleAnimations(control, state, currentState);
            CancelMissingStyleAnimations(control, state);

            foreach (KeyValuePair<int, StyleAnimationBase> pair in state.ResolvedStyleAnimations)
                pair.Value.Start(control, previousState, currentState);
        }

        internal static void ResetStyleAnimations(this ControlTemplate control)
        {
            if (control == null)
                return;

            ControlAnimationState state = GetAnimationState(control, false);
            if (state == null)
                return;

            state.AnimatedStyleValues.Clear();
            state.ResolvedStyleAnimations.Clear();
            state.ScopeStyleAnimations.Clear();
            state.StyleAnimationPropertyIds.Clear();
            state.HasStyleStateSnapshot = false;
            control.AnimationState = null;
            control.MarkDirty();
        }

        internal static TValue GetStyleAnimationStartValue<TValue>(
            this ControlTemplate control,
            StyleProperty<TValue> property,
            StyleState previousState)
        {
            TValue value;
            return control.TryGetAnimatedStyleValue(property, out value)
                ? value
                : control.ResolveStyleValueForState(property, previousState);
        }

        internal static TValue GetStyleAnimationCurrentValue<TValue>(
            this ControlTemplate control,
            StyleProperty<TValue> property,
            StyleState fallbackState)
        {
            TValue value;
            return control.TryGetAnimatedStyleValue(property, out value)
                ? value
                : control.ResolveStyleValueForState(property, fallbackState);
        }

        internal static void SetAnimatedStyleValue<TValue>(
            this ControlTemplate control,
            StyleProperty<TValue> property,
            TValue value)
        {
            if (control == null || property == null)
                return;

            GetAnimationState(control, true).AnimatedStyleValues[property.Id] = value;
            control.MarkDirty();
        }

        internal static void ClearAnimatedStyleValue<TValue>(
            this ControlTemplate control,
            StyleProperty<TValue> property)
        {
            if (control == null || property == null)
                return;

            ControlAnimationState state = GetAnimationState(control, false);
            if (state != null && state.AnimatedStyleValues.Remove(property.Id))
                control.MarkDirty();
        }

        internal static bool TryGetAnimatedStyleValue<TValue>(
            this ControlTemplate control,
            StyleProperty<TValue> property,
            out TValue value)
        {
            ControlAnimationState state = GetAnimationState(control, false);
            object raw;
            if (state != null &&
                property != null &&
                state.AnimatedStyleValues.TryGetValue(property.Id, out raw))
            {
                value = (TValue)raw;
                return true;
            }

            value = default(TValue);
            return false;
        }

        internal static void ApplyRenderTransform(
            this ControlTemplate control,
            List<MySprite> sprites,
            int startIndex,
            RectangleF bounds,
            RectangleF? inheritedClip,
            RenderTransform transform)
        {
            if (control == null || sprites == null)
                return;

            bool applyTransform = transform != null && !transform.IsIdentity;
            bool emittedClip = false;
            int first = Math.Max(0, startIndex);
            for (int i = first; i < sprites.Count; i++)
            {
                MySprite sprite = sprites[i];

                if (sprite.Type == SpriteType.CLIP_RECT)
                {
                    emittedClip = true;
                    if (!applyTransform ||
                        inheritedClip.HasValue && IsClipSprite(sprite, inheritedClip.Value))
                    {
                        continue;
                    }
                }

                if (!applyTransform)
                    continue;

                if (sprite.Position.HasValue)
                    sprite.Position = transform.TransformPosition(sprite.Position.Value, bounds);

                if (sprite.Type == SpriteType.TEXT)
                {
                    sprite.RotationOrScale = transform.TransformTextScale(sprite.RotationOrScale);
                }
                else if (sprite.Size.HasValue)
                {
                    sprite.Size = transform.TransformSize(sprite.Size.Value);
                }

                sprites[i] = sprite;
            }

            // Clip commands are stateful on the LCD sprite surface. A control
            // must not leak its private rounded-rectangle/content clip into the
            // next sibling. Restore the clip context that was active on entry.
            if (emittedClip)
                RestoreInheritedClip(sprites, inheritedClip);
        }

        static void RestoreInheritedClip(List<MySprite> sprites, RectangleF? inheritedClip)
        {
            sprites.Add(MySprite.CreateClearClipRect());
            if (!inheritedClip.HasValue)
                return;

            RectangleF bounds = inheritedClip.Value;
            int x = (int)Math.Floor(bounds.X);
            int y = (int)Math.Floor(bounds.Y);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);
            sprites.Add(MySprite.CreateClipRect(new Rectangle(
                x,
                y,
                Math.Max(0, right - x),
                Math.Max(0, bottom - y))));
        }

        static ControlAnimationState GetAnimationState(ControlTemplate control, bool create)
        {
            if (control == null)
                return null;

            ControlAnimationState state = control.AnimationState;
            if (state == null && create)
            {
                state = new ControlAnimationState();
                control.AnimationState = state;
            }

            return state;
        }

        static void ResolveStyleAnimations(
            ControlTemplate control,
            ControlAnimationState state,
            StyleState styleState)
        {
            state.ResolvedStyleAnimations.Clear();

            int guard = 0;
            for (IVisualStyleScope scope = control; scope != null && guard++ < 128;)
            {
                StyleTree styles = scope.Styles;
                if (styles != null)
                {
                    state.ScopeStyleAnimations.Clear();
                    styles.ResolveAnimations(
                        control,
                        control.StyleId,
                        styleState,
                        state.ScopeStyleAnimations);

                    foreach (KeyValuePair<int, StyleAnimationBase> pair in state.ScopeStyleAnimations)
                    {
                        if (!state.ResolvedStyleAnimations.ContainsKey(pair.Key))
                            state.ResolvedStyleAnimations.Add(pair.Key, pair.Value);
                    }
                }

                IVisualStyleScope next = scope.StyleParent;
                if (ReferenceEquals(next, scope))
                    break;

                scope = next;
            }
        }

        static void CancelMissingStyleAnimations(
            ControlTemplate control,
            ControlAnimationState state)
        {
            if (state.AnimatedStyleValues.Count == 0)
                return;

            state.StyleAnimationPropertyIds.Clear();
            foreach (KeyValuePair<int, object> pair in state.AnimatedStyleValues)
            {
                if (!state.ResolvedStyleAnimations.ContainsKey(pair.Key))
                    state.StyleAnimationPropertyIds.Add(pair.Key);
            }

            for (int i = 0; i < state.StyleAnimationPropertyIds.Count; i++)
            {
                int propertyId = state.StyleAnimationPropertyIds[i];
                control.CancelAnimation(StyleAnimationBase.GetChannel(propertyId), false);
                state.AnimatedStyleValues.Remove(propertyId);
            }

            state.StyleAnimationPropertyIds.Clear();
        }

        static bool IsClipSprite(MySprite sprite, RectangleF bounds)
        {
            if (sprite.Type != SpriteType.CLIP_RECT ||
                !sprite.Position.HasValue ||
                !sprite.Size.HasValue)
            {
                return false;
            }

            int x = (int)Math.Floor(bounds.X);
            int y = (int)Math.Floor(bounds.Y);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);
            Vector2 position = sprite.Position.Value;
            Vector2 size = sprite.Size.Value;

            return Math.Abs(position.X - x) <= 0.01f &&
                   Math.Abs(position.Y - y) <= 0.01f &&
                   Math.Abs(size.X - Math.Max(0, right - x)) <= 0.01f &&
                   Math.Abs(size.Y - Math.Max(0, bottom - y)) <= 0.01f;
        }
    }
}
