using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    abstract class Dialog
    {
        readonly IApp _parentApp;
        readonly IThemedApp _themedParentApp;
        readonly List<MySprite> _sprites = new List<MySprite>();
        DialogContainerControl _containerControl;

        protected Dialog(IApp parentApp)
        {
            if (parentApp == null)
                throw new ArgumentNullException("parentApp");

            _parentApp = parentApp;
            _themedParentApp = parentApp as IThemedApp;
        }

        public bool Dismissed { get; private set; }

        protected List<MySprite> Sprites
        {
            get { return _sprites; }
        }

        protected IApp ParentApp
        {
            get { return _parentApp; }
        }

        protected IThemedApp ThemedParentApp
        {
            get { return _themedParentApp; }
        }

        protected IReadOnlyDictionary<string, Color> ParentTheme
        {
            get { return _themedParentApp?.Theme; }
        }

        protected DialogContainerControl ContainerControl
        {
            get { return _containerControl; }
        }

        protected ControlRenderContext CreateRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            Color textColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            if (_themedParentApp != null)
                return _themedParentApp.CreateControlRenderContext(surface, scale, fontScale, cursorPosition);

            return new ControlRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);
        }

        protected Color GetThemeColor(string role)
        {
            if (_themedParentApp == null)
                throw new ResourceKeyNotFoundException(role, "ParentTheme");

            return _themedParentApp.GetThemeColor(role);
        }

        public virtual void AddInteractiveEntries(List<ControlBase> entries)
        {
            if (Dismissed || entries == null)
                return;

            if (_containerControl != null && _containerControl.Visible)
                entries.Add(_containerControl);
        }

        public void Render(
            InteractiveSurfaceScript owner,
            List<MySprite> targetSprites,
            RectangleF viewBox,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            Color textColor,
            Color backgroundColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            _sprites.Clear();

            if (Dismissed)
                return;

            RenderCore(owner, viewBox, scale, fontScale, surface, textColor, backgroundColor, panelColor, cursorPosition);

            if (targetSprites != null)
                targetSprites.AddRange(_sprites);
        }

        protected DialogContainerControl EnsureContainer(RectangleF bounds)
        {
            if (_containerControl == null)
                _containerControl = new DialogContainerControl(bounds, _parentApp);
            else
                _containerControl.SetRect(bounds);

            _containerControl.SetDataContext(_parentApp);
            _containerControl.SetVisible(true);
            return _containerControl;
        }

        public void Dismiss()
        {
            Dismissed = true;
            OnDismiss();
            _sprites.Clear();

            if (_containerControl != null)
            {
                _containerControl.ClearChildren();
                _containerControl.SetVisible(false);
            }
        }

        protected virtual void OnDismiss()
        {
        }

        protected abstract void RenderCore(
            InteractiveSurfaceScript owner,
            RectangleF viewBox,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            Color textColor,
            Color backgroundColor,
            Color panelColor,
            Vector2 cursorPosition);
    }

    class DialogContainerControl : RectangleControl
    {
        public DialogContainerControl(RectangleF rect, IApp parentApp)
            : base(rect, CursorType.Default, parentApp)
        {
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
        }
    }
}
