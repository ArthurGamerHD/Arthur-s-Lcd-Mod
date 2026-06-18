using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
{
    abstract class Dialog : IVisualStyleScope, ITextStyleProvider
    {
        readonly IApp _parentApp;
        readonly List<MySprite> _sprites = new List<MySprite>();
        IVisualStyleScope _styleParent;
        StyleTree _styles;
        ResourceTree _resources;
        bool _isDirty = true;
        DialogContainerControl _containerControl;
        Button _closeButton;
        RectangleF _dialogCardRect;
        bool _dialogCardRegistered;

        protected Dialog(IApp parentApp)
        {
            if (parentApp == null)
                throw new ArgumentNullException("parentApp");

            _parentApp = parentApp;
            _styleParent = parentApp as IVisualStyleScope;
        }

        public bool Dismissed { get; private set; }

        public IVisualStyleScope StyleParent
        {
            get { return _styleParent; }
        }

        public StyleTree Styles
        {
            get { return _styles; }
        }

        public ResourceTree Resources
        {
            get { return _resources; }
        }

        public bool IsDirty
        {
            get { return _isDirty; }
        }

        public void MarkDirty()
        {
            _isDirty = true;
        }

        public Dialog SetStyles(StyleTree styles)
        {
            if (ReferenceEquals(_styles, styles))
                return this;

            _styles = styles;
            MarkDirty();
            return this;
        }

        public Dialog SetResources(ResourceTree resources)
        {
            if (ReferenceEquals(_resources, resources))
                return this;

            _resources = resources;
            MarkDirty();
            return this;
        }

        internal void SetStyleParent(IVisualStyleScope styleParent)
        {
            if (ReferenceEquals(_styleParent, styleParent))
                return;

            _styleParent = styleParent;
            MarkDirty();

            if (_containerControl != null)
                _containerControl.SetStyleParent(this);
        }

        protected List<MySprite> Sprites
        {
            get { return _sprites; }
        }

        protected IApp ParentApp
        {
            get { return _parentApp; }
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

        protected Color ResolveColor(ResourceKey<Color> key)
        {
            Color value;
            if (ScopedResourceResolver.TryResolve(this, key, out value))
                return value;

            throw new ResourceKeyNotFoundException(key.Name, "ResourceTree");
        }

        protected string TextFont
        {
            get
            {
                if (_containerControl != null)
                    return _containerControl.TextFont;

                string value;
                if (ScopedResourceResolver.TryResolve(this, ThemeResources.TextFont, out value) &&
                    !string.IsNullOrEmpty(value))
                    return value;

                return "White";
            }
        }

        string ITextStyleProvider.ResolvedTextFont
        {
            get { return TextFont; }
        }

        protected Vector2 MeasureText(string text, float scale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            return FormatingHelper.GetSizeInPixel(text, this, scale, surface);
        }

        protected float MeasureLineHeight(float scale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface, string probe = "Ag")
        {
            return FormatingHelper.LineHeight(scale, this, surface, probe);
        }

        public virtual void AddInteractiveEntries(List<Control> entries)
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

            BuildDialogControls(owner, viewBox, scale, fontScale, surface, textColor, backgroundColor, panelColor, cursorPosition);
            RenderCloseButton(surface, scale, fontScale, textColor, panelColor, cursorPosition);

            if (targetSprites != null)
                targetSprites.AddRange(_sprites);
        }

        protected DialogContainerControl EnsureContainer(RectangleF bounds)
        {
            if (_containerControl == null)
            {
                _containerControl = new DialogContainerControl(bounds, _parentApp);
                _containerControl.SetStyleParent(this);
            }
            else
            {
                _containerControl.SetRect(bounds);
                _containerControl.SetStyleParent(this);
            }

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
            _closeButton.Render(_sprites);
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

            _closeButton.TextColor = ResolveColor(ThemeResources.OnSurfaceColor);
            _closeButton.BackgroundColor = ResolveColor(ThemeResources.SurfaceContainerColor);
            _closeButton.BorderRadiusPixels = Border.DEFAULT_RADIUS_PIXELS;
            _closeButton.SetStyleParent(this);
            _closeButton.CustomRender = RenderDialogCloseButton;
            _closeButton.SetCursor(CursorType.Hand);
            _closeButton.SetVisible(true);
        }

        void RenderDialogCloseButton(ControlTemplate control, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var hovered = control.IsPointerOver;
            var radiusPixels = Math.Min(rect.Width, rect.Height) * 0.5f / Math.Max(0.001f, control.LayoutScale);
            var fillColor = hovered
                ? ResolveColor(ThemeResources.SurfaceContainerLowColor)
                : ResolveColor(ThemeResources.SurfaceContainerColor);

            Border.CreateSpritesFromRect(
                rect,
                sprites,
                fillColor,
                radiusPixels: radiusPixels,
                radiusScale: control.LayoutScale);

            var iconSize = Math.Max(1f, Math.Min(rect.Width, rect.Height) - 10f * control.LayoutScale);

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

        /// <summary>
        /// Configures the dialog visual tree for the current frame.
        ///
        /// New dialogs should add/update controls under <see cref="ContainerControl"/> and let those
        /// controls render themselves. Existing migrated dialogs may still use this hook while their
        /// internal rows/buttons are being converted to real controls.
        /// </summary>
        protected abstract void BuildDialogControls(
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

        public override bool CanHover
        {
            get { return Visible; }
        }

        public override bool Hover(object sender)
        {
            return Visible;
        }

        protected override bool CanResolveChildren(Vector2 point, bool selfHit)
        {
            return true;
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var children = Children;
            if (children == null || children.Count == 0)
                return;

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i] as ControlTemplate;
                if (child != null)
                    child.Render(sprites);
            }
        }
    }
}
