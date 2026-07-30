using System;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
using VRageMath;

namespace LcdMod.Client.Ftue
{
    internal sealed class AppInputHintFtueTip<TApp> : HintFtueTip
        where TApp : class, IApp
    {
        readonly Func<InteractiveSurfaceScript, TApp, string> _markdownFactory;
        readonly Func<InteractiveSurfaceScript, TApp, ControlTemplate, bool> _completionCondition;

        public AppInputHintFtueTip(
            string id,
            Func<InteractiveSurfaceScript, TApp, string> markdownFactory,
            Func<InteractiveSurfaceScript, TApp, ControlTemplate, bool> completionCondition = null)
            : base(id, typeof(TApp))
        {
            if (markdownFactory == null)
                throw new ArgumentNullException(nameof(markdownFactory));
            _markdownFactory = markdownFactory;
            _completionCondition = completionCondition;
        }

        public Func<InteractiveSurfaceScript, TApp, Action, Action> CompletionBinder { get; set; }

        public Func<InteractiveSurfaceScript, TApp, bool> ActivationCondition { get; set; }

        internal override void OnVisualContact(
            InteractiveSurfaceScript surface,
            IApp app,
            Vector2 coordinates)
        {
            var typedApp = app as TApp;
            if (typedApp != null && IsActiveFor(surface, typedApp))
                Show(surface, typedApp, null);
        }

        internal override void OnControlClick(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate control,
            bool secondary)
        {
            if (secondary || !IsTriggered(surface))
                return;

            var typedApp = app as TApp;
            if (_completionCondition != null && typedApp != null &&
                IsActiveFor(surface, typedApp) && control != null &&
                _completionCondition(surface, typedApp, control))
            {
                Complete(surface);
            }
        }

        protected override Action BindExternalCompletion(
            InteractiveSurfaceScript surface,
            IApp app,
            Action complete)
        {
            var typedApp = app as TApp;
            if (typedApp == null || CompletionBinder == null)
                return null;

            return CompletionBinder(surface, typedApp, () =>
            {
                if (IsTriggered(surface))
                    complete();
            });
        }

        bool IsActiveFor(InteractiveSurfaceScript surface, TApp app)
        {
            return ActivationCondition == null || ActivationCondition(surface, app);
        }

        protected override string BuildMarkdown(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate targetControl)
        {
            var typedApp = app as TApp;
            return typedApp == null
                ? string.Empty
                : _markdownFactory(surface, typedApp);
        }
    }
}
