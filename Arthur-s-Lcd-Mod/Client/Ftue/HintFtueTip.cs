using System;
using System.Collections.Generic;
using Adk.Gui.Animation;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.UserControls;
using LcdMod.Client.Markdown;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;

namespace LcdMod.Client.Ftue
{
    internal abstract class HintFtueTip : FtueTip
    {
        sealed class SurfaceState
        {
            public readonly InteractiveSurfaceScript Surface;
            public readonly IApp App;
            public readonly FtueHintWindow Window;
            public readonly MarkdownParser MarkdownParser = new MarkdownParser();

            public Action UnbindCompletion;
            public MarkdownDocument Document;
            public string ParsedMarkdown;
            public bool Triggered;
            public bool Eligible;
            public bool CompletionAnimationActive;

            public SurfaceState(InteractiveSurfaceScript surface, IApp app, HintPlacement placement)
            {
                Surface = surface;
                App = app;
                Window = new FtueHintWindow(placement);
            }

            public void Invalidate()
            {
                Surface.RenderSprites();
            }
        }

        const int SUCCESS_TRANSITION_FRAMES = 8;
        const int SUCCESS_HOLD_FRAMES = 30;

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
            IApp app);

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

        protected void Show(InteractiveSurfaceScript surface, IApp app)
        {
            if (_completionStarted || surface == null || app == null)
                return;

            SurfaceState state;
            if (!_states.TryGetValue(surface, out state) || !ReferenceEquals(state.App, app))
                return;

            bool changed = !state.Triggered;
            state.Triggered = true;
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
            state.Window.SetSuccess(0f);
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

            var state = new SurfaceState(surface, app, Placement);
            state.Window.Dismissed = () => CompleteFrom(state);
            state.Eligible = !_completionStarted && IsEligible(surface);
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
            state.Window.Hide();
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

        internal override void OnRender(InteractiveSurfaceScript surface, IApp app, List<MySprite> frame)
        {
            SurfaceState state;
            if (!_states.TryGetValue(surface, out state))
                return;

            bool visible = state.CompletionAnimationActive ||
                           state.Triggered && state.Eligible && FtueTipSlotScheduler.IsActive(surface, Placement, this);
            if (visible)
            {
                EnsureMarkdown(state);
                visible = state.Document != null;
            }

            var owner = surface.RetainedAppGuiHost == null ? null : surface.RetainedAppGuiHost.Root as Adk.Gui.Controls.Window;
            if (!visible || owner == null || !owner.IsOpen)
            {
                state.Window.Hide();
                return;
            }

            state.Window.Document = state.Document;
            state.Window.ShowOver(owner);
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
            state.Window.SetSuccess(0f);

            _activeCompletionAnimations++;
            try
            {
                state.Surface.Animations.Run(
                    state,
                    state.Invalidate,
                    "ftue-hint-complete:" + Id,
                    new Keyframe(
                        value => state.Window.SetSuccess(value),
                        0f,
                        1f,
                        SUCCESS_TRANSITION_FRAMES,
                        EasingMode.EaseOutCubic),
                    new DelayKeyframe(SUCCESS_HOLD_FRAMES),
                    new ActionKeyframe(() => FinishSuccessAnimation(state), false));
            }
            catch (Exception e)
            {
                state.CompletionAnimationActive = false;
                state.Window.Hide();
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
            state.Window.Hide();
            _activeCompletionAnimations = Math.Max(0, _activeCompletionAnimations - 1);
            FtueTipSlotScheduler.Release(state.Surface, Placement, this);
            QueueCompletionFinalizationIfReady();
        }

        void HideState(SurfaceState state)
        {
            if (state == null)
                return;

            state.Eligible = false;
            state.Window.Hide();
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

            state.Window.Hide();
            _states.Remove(state.Surface);
        }

        void EnsureMarkdown(SurfaceState state)
        {
            string markdown = BuildMarkdown(state.Surface, state.App);
            if (string.Equals(state.ParsedMarkdown, markdown, StringComparison.Ordinal))
                return;

            state.ParsedMarkdown = markdown;
            state.Document = string.IsNullOrWhiteSpace(markdown)
                ? null
                : state.MarkdownParser.Parse(markdown);
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
    }
}
