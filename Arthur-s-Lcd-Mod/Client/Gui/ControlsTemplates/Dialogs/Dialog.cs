using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Common.Helpers;
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
        Button _closeButton;
        ControlStyle _closeButtonStyle;
        RectangleF _dialogCardRect;
        bool _dialogCardRegistered;

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

        protected Action OnClose { get; set; }

        protected virtual bool ShowCloseButton
        {
            get { return true; }
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
            _dialogCardRegistered = false;

            if (Dismissed)
                return;

            RenderCore(owner, viewBox, scale, fontScale, surface, textColor, backgroundColor, panelColor, cursorPosition);
            RenderCloseButton(surface, scale, fontScale, textColor, panelColor, cursorPosition);

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

        protected void RegisterDialogCard(RectangleF cardRect)
        {
            _dialogCardRect = cardRect;
            _dialogCardRegistered = true;
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

        protected Vector2 GetDialogCloseButtonSize(float scale)
        {
            var size = Math.Max(20f * scale, 26f * scale);
            return new Vector2(size, size);
        }

        protected RectangleF GetDialogCloseButtonRect(RectangleF cardRect, float scale)
        {
            var size = GetDialogCloseButtonSize(scale);
            var inset = 8f * scale;

            return new RectangleF(
                cardRect.X + inset,
                cardRect.Y + inset,
                size.X,
                size.Y);
        }

        void RenderCloseButton(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            Color textColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            if (!ShowCloseButton || !_dialogCardRegistered || Dismissed || _containerControl == null)
                return;

            var rect = GetDialogCloseButtonRect(_dialogCardRect, scale);
            EnsureCloseButton(rect);

            _containerControl.AddChild(_closeButton);

            var context = CreateRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);
            _closeButton.Render(context, _sprites);
        }

        void EnsureCloseButton(RectangleF rect)
        {
            if (_closeButton == null)
            {
                _closeButton = new Button(
                    rect,
                    new ButtonModel
                    {
                        Text = string.Empty,
                        Clicked = OnDialogCloseButtonClicked
                    });
            }
            else
            {
                _closeButton.SetRect(rect);
            }

            var model = _closeButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = string.Empty;
                model.Enabled = true;
                model.Clicked = OnDialogCloseButtonClicked;
            }

            _closeButton.SetStyle(GetCloseButtonStyle());
            _closeButton.CustomRender = RenderDialogCloseButton;
            _closeButton.SetCursor(CursorType.Hand);
            _closeButton.SetVisible(true);
        }

        ControlStyle GetCloseButtonStyle()
        {
            if (_closeButtonStyle == null)
            {
                _closeButtonStyle = ControlStyle.FromThemeRoles(
                    Constants.ON_SURFACE,
                    Constants.SURFACE_CONTAINER,
                    Constants.SURFACE_CONTAINER_LOW,
                    Constants.ON_SURFACE,
                    ParentTheme);
                _closeButtonStyle.BorderRadiusPixels = Border.DEFAULT_RADIUS_PIXELS;
            }
            else
            {
                _closeButtonStyle.ThemeColors = ParentTheme;
            }

            return _closeButtonStyle;
        }

        void RenderDialogCloseButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var hovered = rect.Contains(context.CursorPosition);
            var radiusPixels = Math.Min(rect.Width, rect.Height) * 0.5f / Math.Max(0.001f, context.Scale);

            Border.CreateSpritesFromRect(
                rect,
                sprites,
                context.Style.GetPanelColor(hovered),
                radiusPixels: radiusPixels,
                radiusScale: context.Scale);

            var iconSize = Math.Max(1f, Math.Min(rect.Width, rect.Height) - 10f * context.Scale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Cross",
                Position = rect.Center,
                Size = new Vector2(iconSize, iconSize),
                Color = Constants.ColorCorrection,
                Alignment = TextAlignment.CENTER
            });
        }

        void OnDialogCloseButtonClicked(ButtonModel model, object sender)
        {
            Dismiss();

            if (OnClose != null)
                OnClose();
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

        protected override bool CanResolveChildren(Vector2 point, bool selfHit)
        {
            return true;
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            var children = Children;
            if (children == null || children.Count == 0)
                return;

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null)
                    child.Render(context, sprites);
            }
        }
    }
}
