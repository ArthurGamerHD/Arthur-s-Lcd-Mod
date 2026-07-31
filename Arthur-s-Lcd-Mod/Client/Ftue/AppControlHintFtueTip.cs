using System;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;

namespace LcdMod.Client.Ftue
{
    internal enum ControlHintTrigger
    {
        PointerEnter,
        PrimaryClick
    }

    internal sealed class AppControlHintFtueTip<TApp> : HintFtueTip
        where TApp : class, IApp
    {
        readonly Func<InteractiveSurfaceScript, TApp, ControlTemplate, bool> _condition;
        readonly Func<InteractiveSurfaceScript, TApp, ControlTemplate, string> _markdownFactory;

        public AppControlHintFtueTip(
            string id,
            Func<InteractiveSurfaceScript, TApp, ControlTemplate, bool> condition,
            Func<InteractiveSurfaceScript, TApp, ControlTemplate, string> markdownFactory)
            : base(id, typeof(TApp))
        {
            if (condition == null)
                throw new ArgumentNullException(nameof(condition));
            if (markdownFactory == null)
                throw new ArgumentNullException(nameof(markdownFactory));

            _condition = condition;
            _markdownFactory = markdownFactory;
        }

        public ControlHintTrigger Trigger { get; set; } = ControlHintTrigger.PointerEnter;

        public bool CompleteOnPrimaryClick { get; set; } = true;

        public Func<InteractiveSurfaceScript, TApp, Action, Action> CompletionBinder { get; set; }

        internal override void OnControlPointerEnter(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate control)
        {
            if (Trigger != ControlHintTrigger.PointerEnter)
                return;

            var typedApp = app as TApp;
            if (!Matches(surface, typedApp, control))
                return;

            Show(surface, typedApp, control);
        }

        internal override void OnControlClick(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate control,
            ControlClickButton button)
        {
            if (button != ControlClickButton.Primary)
                return;

            var typedApp = app as TApp;
            if (!Matches(surface, typedApp, control))
                return;

            if (Trigger == ControlHintTrigger.PrimaryClick)
                Show(surface, typedApp, control);

            if (CompleteOnPrimaryClick && IsTriggered(surface))
                Complete(surface);
        }

        protected override string BuildMarkdown(
            InteractiveSurfaceScript surface,
            IApp app,
            ControlTemplate targetControl)
        {
            var typedApp = app as TApp;
            return typedApp == null
                ? string.Empty
                : _markdownFactory(surface, typedApp, targetControl);
        }

        protected override Action BindExternalCompletion(
            InteractiveSurfaceScript surface,
            IApp app,
            Action complete)
        {
            var typedApp = app as TApp;
            return typedApp == null || CompletionBinder == null
                ? null
                : CompletionBinder(surface, typedApp, complete);
        }

        bool Matches(
            InteractiveSurfaceScript surface,
            TApp app,
            ControlTemplate control)
        {
            return app != null && control != null && _condition(surface, app, control);
        }
    }
}
