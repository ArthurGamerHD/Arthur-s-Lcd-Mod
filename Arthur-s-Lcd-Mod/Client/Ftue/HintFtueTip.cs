using System;
using System.Collections.Generic;
using LcdMod.Client.Animation;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.UserControls;
using LcdMod.Client.Markdown;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Ftue
{
    internal enum HintPlacement
    {
        Top,
        Bottom
    }

    internal abstract class HintFtueTip : FtueTip
    {
        sealed class BorderlessButton : Button
        {
            public BorderlessButton(RectangleF bounds, ButtonModel model)
                : base(bounds, model)
            {
            }

            protected override bool ShouldRenderStyleBorder()
            {
                return false;
            }
        }

        sealed class SurfaceState
        {
            public readonly InteractiveSurfaceScript Surface;
            public readonly IApp App;
            public readonly Button CloseButton;
            public readonly MarkdownParser MarkdownParser = new MarkdownParser();
            public readonly List<MySprite> RenderBuffer = new List<MySprite>();

            public Action UnbindCompletion;
            public ControlTemplate TargetControl;
            public MarkdownDocument Document;
            public string ParsedMarkdown;
            public bool Triggered;
            public bool Eligible;
            public bool LayoutAvailable;
            public bool CompletionAnimationActive;
            public float SuccessProgress;
            public float Opacity = 1f;

            public SurfaceState(InteractiveSurfaceScript surface, IApp app)
            {
                Surface = surface;
                App = app;
                CloseButton = new BorderlessButton(default(RectangleF), new ButtonModel
                {
                    Text = string.Empty
                });
                CloseButton.SetStyleParent(app);
                CloseButton.SetCursor(CursorType.Hand);
            }

            public void SetCloseClicked(Action<ButtonModel, object> clicked)
            {
                var model = CloseButton.DataContext as ButtonModel;
                if (model != null)
                    model.Clicked = clicked;
            }

            public void Invalidate()
            {
                CloseButton.MarkDirty();
            }
        }

        const int SUCCESS_TRANSITION_FRAMES = 8;
        const int SUCCESS_HOLD_FRAMES = 8;
        const int FADE_OUT_FRAMES = 30;

        readonly Dictionary<InteractiveSurfaceScript, SurfaceState> _states =
            new Dictionary<InteractiveSurfaceScript, SurfaceState>();

        bool _completionStarted;
        bool _completionFinalizeQueued;
        int _activeCompletionAnimations;

        protected HintFtueTip(string id, params Type[] appTypeWhitelist)
            : base(id, appTypeWhitelist)
        {
            SetCompleted(LocalConfigManager.IsFtueTipCompleted(Id));
        }

        public HintPlacement Placement { get; set; } = HintPlacement.Bottom;

        protected abstract string BuildMarkdown(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate targetControl);

        protected virtual Action BindExternalCompletion(
            InteractiveSurfaceScript surface,
            IApp app,
            Action complete)
        {
            return null;
        }

        protected virtual bool IsEligible(InteractiveSurfaceScript surface)
        {
            return FtueSurfaceEligibility.IsSurfaceEligible(surface);
        }

        protected void Show(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate targetControl)
        {
            if (_completionStarted || surface == null || app == null)
                return;

            SurfaceState state;
            if (!_states.TryGetValue(surface, out state) || !ReferenceEquals(state.App, app))
                return;

            bool changed = !state.Triggered || !ReferenceEquals(state.TargetControl, targetControl);
            state.Triggered = true;
            state.TargetControl = targetControl;
            if (changed)
            {
                state.ParsedMarkdown = null;
                state.Document = null;
            }

            if (!IsEligible(surface))
                return;

            FtueTipSlotScheduler.ShowOrSchedule(
                surface,
                Placement,
                this,
                () => Activate(state));
        }

        bool Activate(SurfaceState state)
        {
            SurfaceState current;
            if (_completionStarted ||
                state == null ||
                !_states.TryGetValue(state.Surface, out current) ||
                !ReferenceEquals(current, state) ||
                !state.Triggered ||
                !IsEligible(state.Surface))
            {
                return false;
            }

            state.Eligible = true;
            state.Opacity = 1f;
            state.SuccessProgress = 0f;
            state.Surface.RenderSprites();
            return true;
        }

        protected void Complete(InteractiveSurfaceScript surface)
        {
            SurfaceState state;
            if (surface != null && _states.TryGetValue(surface, out state))
                CompleteFrom(state);
        }

        protected bool IsTriggered(InteractiveSurfaceScript surface)
        {
            SurfaceState state;
            return surface != null &&
                   _states.TryGetValue(surface, out state) &&
                   state.Triggered &&
                   FtueTipSlotScheduler.IsActive(surface, Placement, this);
        }

        internal override void OnHooked(InteractiveSurfaceScript surface, IApp app)
        {
            if (surface == null || app == null || _states.ContainsKey(surface))
                return;

            var state = new SurfaceState(surface, app);
            state.SetCloseClicked((model, sender) => CompleteFrom(state));
            state.CloseButton.CustomRender = (control, sprites) => RenderCloseButton(state, control, sprites);
            state.Eligible = !_completionStarted && IsEligible(surface);
            state.CloseButton.SetVisible(false);
            _states[surface] = state;

            try
            {
                var unbind = BindExternalCompletion(surface, app, () =>
                {
                    if (CanCompleteFrom(state))
                        CompleteFrom(state);
                });
                state.UnbindCompletion = unbind;
                if (_completionStarted)
                    UnbindComplete(state);
            }
            catch
            {
                _states.Remove(surface);
                throw;
            }
        }

        internal override void OnUnhooked(InteractiveSurfaceScript surface, IApp app)
        {
            SurfaceState state;
            if (surface == null || !_states.TryGetValue(surface, out state))
                return;

            RemoveState(state);
            QueueCompletionFinalizationIfReady();
        }

        internal override void OnUpdateEnd(InteractiveSurfaceScript surface, IApp app)
        {
            SurfaceState state;
            if (!_states.TryGetValue(surface, out state))
                return;

            bool eligible = !_completionStarted && IsEligible(surface);
            if (state.Eligible == eligible)
                return;

            state.Eligible = eligible;
            state.LayoutAvailable = false;
            state.CloseButton.SetVisible(false);
            if (!eligible)
                FtueTipSlotScheduler.Release(surface, Placement, this);
            else if (state.Triggered)
                FtueTipSlotScheduler.ShowOrSchedule(
                    surface,
                    Placement,
                    this,
                    () => Activate(state));
            surface.RenderSprites();
        }

        internal override void OnCollectInteractiveEntries(
            InteractiveSurfaceScript surface,
            IApp app,
            List<Control> entries)
        {
            SurfaceState state;
            if (entries == null || !_states.TryGetValue(surface, out state))
                return;

            bool visible = state.Triggered &&
                           state.LayoutAvailable &&
                           state.Eligible &&
                           !_completionStarted &&
                           FtueTipSlotScheduler.IsActive(surface, Placement, this);
            var model = state.CloseButton.DataContext as ButtonModel;
            if (model != null)
                model.Enabled = visible;

            state.CloseButton.SetVisible(visible && state.Opacity > 0.01f);
            if (state.CloseButton.Visible)
                entries.Add(state.CloseButton);
        }

        internal override void OnRender(InteractiveSurfaceScript surface, IApp app, List<MySprite> frame)
        {
            SurfaceState state;
            if (frame == null || !_states.TryGetValue(surface, out state))
                return;

            if ((!state.Triggered || !state.Eligible) && !state.CompletionAnimationActive)
            {
                state.LayoutAvailable = false;
                return;
            }

            if (!state.CompletionAnimationActive &&
                !FtueTipSlotScheduler.IsActive(surface, Placement, this))
            {
                state.LayoutAvailable = false;
                state.CloseButton.SetVisible(false);
                return;
            }

            EnsureMarkdown(state);
            if (state.Document == null)
            {
                state.LayoutAvailable = false;
                return;
            }

            RectangleF cardRect;
            RectangleF textRect;
            RectangleF closeRect;
            if (!TryLayout(state, out cardRect, out textRect, out closeRect))
            {
                state.LayoutAvailable = false;
                state.CloseButton.SetVisible(false);
                return;
            }

            state.LayoutAvailable = true;
            state.CloseButton.SetRect(closeRect);

            var buffer = state.RenderBuffer;
            buffer.Clear();

            var normalBackground = ResolveColor(app, ThemeResources.SurfaceContainerHighestColor, surface.BackgroundColor);
            var normalBorder = ResolveColor(app, ThemeResources.AccentColor, surface.ForegroundColor);
            var success = ResolveColor(app, ThemeResources.SuccessColor, new Color(41, 171, 79));
            var normalText = ResolveColor(app, ThemeResources.OnSurfaceColor, surface.ForegroundColor);
            var successText = GetContrastingColor(success);

            var background = ApplyOpacity(
                Lerp(normalBackground, success, state.SuccessProgress),
                state.Opacity);
            var text = ApplyOpacity(
                Lerp(normalText, successText, state.SuccessProgress),
                state.Opacity);
            normalBackground = ApplyOpacity(normalBackground, state.Opacity);
            normalBorder = ApplyOpacity(normalBorder, state.Opacity);
            float layoutScale = Math.Max(0.1f, surface.ConfiguredScale * surface.Surface.FontSize);

            if (state.CompletionAnimationActive)
            {
                BorderRenderer.CreateSpritesFromRect(
                    cardRect,
                    buffer,
                    background,
                    radiusPixels: 8f,
                    radiusScale: layoutScale);
            }
            else
            {
                float borderThickness = Math.Max(1f, 2f * layoutScale);
                BorderRenderer.CreateSpritesFromRect(
                    cardRect,
                    buffer,
                    normalBorder,
                    radiusPixels: 8f,
                    radiusScale: layoutScale);

                BorderRenderer.CreateSpritesFromRect(
                    Inset(cardRect, borderThickness),
                    buffer,
                    normalBackground,
                    radiusPixels: 8f,
                    radiusScale: layoutScale);
            }

            MarkdownPanel.CreateSprites(
                state.Document,
                textRect,
                text,
                text,
                buffer,
                surface);

            if (!state.CompletionAnimationActive)
                state.CloseButton.Render(buffer);

            frame.AddRange(buffer);
        }

        void CompleteFrom(SurfaceState source)
        {
            if (_completionStarted || !CanCompleteFrom(source))
            {
                return;
            }

            _completionStarted = true;
            try
            {
                LocalConfigManager.SetFtueTipCompleted(Id, true);
            }
            catch
            {
                _completionStarted = false;
                throw;
            }

            try
            {
                var states = new List<SurfaceState>(_states.Values);
                for (int i = 0; i < states.Count; i++)
                {
                    var state = states[i];
                    UnbindComplete(state);

                    if (state.Triggered &&
                        IsEligible(state.Surface) &&
                        FtueTipSlotScheduler.IsActive(state.Surface, Placement, this))
                    {
                        StartSuccessAnimation(state);
                    }
                    else
                    {
                        HideState(state);
                    }
                }
            }
            finally
            {
                QueueCompletionFinalizationIfReady();
            }
        }

        bool CanCompleteFrom(SurfaceState state)
        {
            return state != null &&
                   state.Triggered &&
                   IsEligible(state.Surface) &&
                   FtueTipSlotScheduler.IsActive(state.Surface, Placement, this);
        }

        void StartSuccessAnimation(SurfaceState state)
        {
            if (state == null || state.CompletionAnimationActive)
                return;

            state.Eligible = false;
            state.CompletionAnimationActive = true;
            state.SuccessProgress = 0f;
            state.Opacity = 1f;
            state.CloseButton.SetVisible(false);
            var model = state.CloseButton.DataContext as ButtonModel;
            if (model != null)
                model.Enabled = false;

            _activeCompletionAnimations++;
            try
            {
                state.Surface.Animations.Run(
                    state,
                    state.Invalidate,
                    "ftue-hint-complete:" + Id,
                    new Keyframe(
                        value => state.SuccessProgress = value,
                        0f,
                        1f,
                        SUCCESS_TRANSITION_FRAMES,
                        EasingMode.EaseOutCubic),
                    new DelayKeyframe(SUCCESS_HOLD_FRAMES),
                    new Keyframe(
                        value => state.Opacity = value,
                        1f,
                        0f,
                        FADE_OUT_FRAMES,
                        EasingMode.EaseOutCubic),
                    new ActionKeyframe(() => FinishSuccessAnimation(state), false));
            }
            catch (Exception e)
            {
                state.CompletionAnimationActive = false;
                state.Opacity = 0f;
                state.CloseButton.SetVisible(false);
                _activeCompletionAnimations = Math.Max(0, _activeCompletionAnimations - 1);
                FtueTipSlotScheduler.Release(state.Surface, Placement, this);
                ErrorHandlerHelper.LogError(e, state.Surface);
            }

            state.Surface.RenderSprites();
        }

        void FinishSuccessAnimation(SurfaceState state)
        {
            if (state == null || !state.CompletionAnimationActive)
                return;

            state.CompletionAnimationActive = false;
            state.Opacity = 0f;
            state.CloseButton.SetVisible(false);
            _activeCompletionAnimations = Math.Max(0, _activeCompletionAnimations - 1);
            FtueTipSlotScheduler.Release(state.Surface, Placement, this);
            QueueCompletionFinalizationIfReady();
        }

        void HideState(SurfaceState state)
        {
            if (state == null)
                return;

            state.Eligible = false;
            state.LayoutAvailable = false;
            state.Opacity = 0f;
            state.CloseButton.SetVisible(false);
            FtueTipSlotScheduler.Release(state.Surface, Placement, this);
            state.Surface.RenderSprites();
        }

        void QueueCompletionFinalizationIfReady()
        {
            if (!_completionStarted || _activeCompletionAnimations != 0 || _completionFinalizeQueued)
                return;

            _completionFinalizeQueued = true;
            LcdModClientComponent.RunNextFrame.Add(FinalizeCompletion);
        }

        void FinalizeCompletion()
        {
            _completionFinalizeQueued = false;
            if (!_completionStarted || _activeCompletionAnimations != 0)
                return;

            SetCompleted(true);
            CloseAllStates();
        }

        void CloseAllStates()
        {
            var states = new List<SurfaceState>(_states.Values);
            for (int i = 0; i < states.Count; i++)
                RemoveState(states[i]);
        }

        void RemoveState(SurfaceState state)
        {
            if (state == null)
                return;

            FtueTipSlotScheduler.Cancel(state.Surface, Placement, this);
            UnbindComplete(state);

            if (state.CompletionAnimationActive)
            {
                state.Surface.Animations.CancelOwner(state, false);
                state.CompletionAnimationActive = false;
                _activeCompletionAnimations = Math.Max(0, _activeCompletionAnimations - 1);
            }

            state.CloseButton.SetVisible(false);
            _states.Remove(state.Surface);
        }

        void EnsureMarkdown(SurfaceState state)
        {
            string markdown = BuildMarkdown(state.Surface, state.App, state.TargetControl);
            if (string.Equals(state.ParsedMarkdown, markdown, StringComparison.Ordinal))
                return;

            state.ParsedMarkdown = markdown;
            state.Document = string.IsNullOrWhiteSpace(markdown)
                ? null
                : state.MarkdownParser.Parse(markdown);
        }

        bool TryLayout(
            SurfaceState state,
            out RectangleF cardRect,
            out RectangleF textRect,
            out RectangleF closeRect)
        {
            cardRect = default(RectangleF);
            textRect = default(RectangleF);
            closeRect = default(RectangleF);

            var surface = state.Surface;
            var viewBox = surface.ViewBox;
            float scale = Math.Max(0.1f, surface.ConfiguredScale * surface.Surface.FontSize);
            float margin = Math.Max(8f, 14f * scale);
            float padding = Math.Max(8f, 12f * scale);
            float closeSize = Math.Max(24f, 28f * scale);
            float availableWidth = viewBox.Width - margin * 2f;
            if (availableWidth <= closeSize + padding * 3f)
                return false;

            float cardWidth = Math.Min(availableWidth, Math.Max(260f * scale, viewBox.Width * 0.82f));
            float textWidth = cardWidth - padding * 3f - closeSize;
            if (textWidth <= 1f)
                return false;

            Vector2 measured = state.Document == null
                ? Vector2.Zero
                : MarkdownPanel.MeasureContent(state.Document, textWidth, surface);
            float contentHeight = Math.Max(closeSize, measured.Y);
            float cardHeight = contentHeight + padding * 2f;
            if (cardHeight > viewBox.Height - margin * 2f)
                return false;

            float cardY = Placement == HintPlacement.Bottom
                ? viewBox.Bottom - margin - cardHeight
                : viewBox.Y + margin;

            cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                cardY,
                cardWidth,
                cardHeight);

            closeRect = new RectangleF(
                cardRect.X + padding,
                cardRect.Y + padding,
                closeSize,
                closeSize);

            textRect = new RectangleF(
                closeRect.Right + padding,
                cardRect.Y + padding,
                textWidth,
                contentHeight);
            return true;
        }

        static RectangleF Inset(RectangleF rect, float amount)
        {
            return new RectangleF(
                rect.X + amount,
                rect.Y + amount,
                Math.Max(0f, rect.Width - amount * 2f),
                Math.Max(0f, rect.Height - amount * 2f));
        }

        static Color ResolveColor(IApp app, ResourceKey<Color> key, Color fallback)
        {
            Color color;
            return ScopedResourceResolver.TryResolve(app, key, out color)
                ? color
                : fallback;
        }

        static Color Lerp(Color from, Color to, float amount)
        {
            amount = MathHelper.Clamp(amount, 0f, 1f);
            return new Color(
                (byte)Math.Round(MathHelper.Lerp(from.R, to.R, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.G, to.G, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.B, to.B, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.A, to.A, amount)));
        }

        static Color GetContrastingColor(Color color)
        {
            float luminance = color.R * 0.299f + color.G * 0.587f + color.B * 0.114f;
            return luminance >= 150f ? Color.Black : Color.White;
        }

        static Color ApplyOpacity(Color color, float opacity)
        {
            opacity = MathHelper.Clamp(opacity, 0f, 1f);
            return new Color(
                color.R,
                color.G,
                color.B,
                (byte)Math.Round(color.A * opacity));
        }

        static void UnbindComplete(SurfaceState state)
        {
            if (state?.UnbindCompletion == null)
                return;

            var unbind = state.UnbindCompletion;
            state.UnbindCompletion = null;
            try
            {
                unbind();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, state.Surface);
            }
        }

        static void RenderCloseButton(SurfaceState state, ControlTemplate control, List<MySprite> sprites)
        {
            if (state == null || control == null || sprites == null)
                return;

            var rect = control.Bounds;
            float scale = Math.Max(0.1f, state.Surface.ConfiguredScale * state.Surface.Surface.FontSize);
            var app = state.App;
            var normalFill = ResolveColor(app, ThemeResources.SurfaceContainerColor, state.Surface.BackgroundColor);
            var hoverFill = ResolveColor(app, ThemeResources.SurfaceContainerLowColor, normalFill);
            var success = ResolveColor(app, ThemeResources.SuccessColor, new Color(41, 171, 79));
            var fill = Lerp(control.IsPointerOver ? hoverFill : normalFill, success, state.SuccessProgress);
            var iconColor = Lerp(
                ResolveColor(app, ThemeResources.OnSurfaceColor, state.Surface.ForegroundColor),
                GetContrastingColor(success),
                state.SuccessProgress);
            float radiusPixels = Math.Min(rect.Width, rect.Height) * 0.5f / scale;

            BorderRenderer.CreateSpritesFromRect(
                rect,
                sprites,
                fill,
                radiusPixels: radiusPixels,
                radiusScale: scale);

            float iconSize = Math.Max(1f, Math.Min(rect.Width, rect.Height) - 10f * scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Cross",
                Position = rect.Center,
                Size = new Vector2(iconSize, iconSize),
                Color = iconColor,
                Alignment = TextAlignment.CENTER
            });
        }
    }
}
