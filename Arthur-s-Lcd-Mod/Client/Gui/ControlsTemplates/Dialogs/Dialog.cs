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
        InteractiveSurfaceScript _owner;
        Button _closeButton;
        RectangleF _dialogCardRect;
        RectangleF _dialogViewBox;
        bool _dialogCardRegistered;
        bool _dialogViewBoxRegistered;

        protected Dialog(IApp parentApp)
        {
            if (parentApp == null)
                throw new ArgumentNullException("parentApp");

            _parentApp = parentApp;
            _styleParent = parentApp;
        }

        public bool Dismissed { get; private set; }

        /// <summary>The dialog immediately below this one in the modal stack.</summary>
        public Dialog Parent { get; private set; }

        /// <summary>
        /// Allows input to reach <see cref="Parent"/> while this dialog is top-most.
        /// Passthrough is recursive: every dialog between the top and a lower dialog must opt in.
        /// </summary>
        public bool Passthrough { get; set; }

        public IVisualStyleScope StyleParent => _styleParent;

        public StyleTree Styles => _styles;

        public ResourceTree Resources => _resources;

        public bool IsDirty => _isDirty;

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

        internal void SetParent(Dialog parent)
        {
            if (ReferenceEquals(parent, this))
                throw new InvalidOperationException("A dialog cannot be its own parent.");

            for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, this))
                    throw new InvalidOperationException("A dialog parent cycle is not allowed.");
            }

            Parent = parent;
            SetStyleParent(parent ?? (IVisualStyleScope)_parentApp);
        }

        protected List<MySprite> Sprites => _sprites;

        protected IApp ParentApp => _parentApp;

        protected DialogContainerControl ContainerControl => _containerControl;

        protected Action OnClose { get; set; }

        protected virtual bool ShowCloseButton => true;

        protected bool CurrentDialogIsTiny
        {
            get { return _dialogViewBoxRegistered && IsTinyDialogAspectRatio(_dialogViewBox); }
        }

        protected float DialogCardRadiusPixels
        {
            get { return CurrentDialogIsTiny ? 0f : BorderRenderer.DEFAULT_RADIUS_PIXELS; }
        }

        protected bool IsTinyDialogAspectRatio(RectangleF viewBox)
        {
            return !SurfaceAspectRatioHelper.MeetsMinimumHeightToWidthRatio(
                viewBox.Width,
                viewBox.Height,
                Constants.MIN_SCREEN_HEIGHT_TO_WIDTH_RATIO);
        }

        protected float GetDialogOuterPadding(
            RectangleF viewBox,
            float scale,
            float regularPixels = 18f,
            float tinyPixels = 2f)
        {
            return IsTinyDialogAspectRatio(viewBox) ? 0f : regularPixels * scale;
        }

        protected Vector2 GetDialogPadding(
            RectangleF viewBox,
            float scale,
            float regularX = 18f,
            float regularY = 14f,
            float tinyX = 4f,
            float tinyY = 2f)
        {
            return IsTinyDialogAspectRatio(viewBox)
                ? new Vector2(tinyX, tinyY) * scale
                : new Vector2(regularX, regularY) * scale;
        }

        protected float GetDialogSpacing(
            RectangleF viewBox,
            float scale,
            float regularPixels = 10f,
            float tinyPixels = 4f)
        {
            return (IsTinyDialogAspectRatio(viewBox) ? tinyPixels : regularPixels) * scale;
        }

        protected RectangleF GetDialogCardRect(
            RectangleF viewBox,
            float scale,
            float widthFraction,
            float heightFraction,
            float minWidthPixels,
            float minHeightPixels,
            float outerPaddingPixels = 18f,
            float tinyOuterPaddingPixels = 2f)
        {
            var outerPadding = GetDialogOuterPadding(viewBox, scale, outerPaddingPixels, tinyOuterPaddingPixels);
            var maxCardWidth = Math.Max(1f, viewBox.Width - outerPadding * 2f);
            var maxCardHeight = Math.Max(1f, viewBox.Height - outerPadding * 2f);
            var compact = IsTinyDialogAspectRatio(viewBox);
            var cardWidth = compact
                ? maxCardWidth
                : Math.Min(Math.Max(minWidthPixels * scale, viewBox.Width * widthFraction), maxCardWidth);
            var cardHeight = compact
                ? maxCardHeight
                : Math.Min(Math.Max(minHeightPixels * scale, viewBox.Height * heightFraction), maxCardHeight);

            return CenterDialogCard(viewBox, cardWidth, cardHeight);
        }

        protected RectangleF CenterDialogCard(RectangleF viewBox, float width, float height)
        {
            return new RectangleF(
                viewBox.Center.X - width * 0.5f,
                viewBox.Center.Y - height * 0.5f,
                Math.Max(1f, width),
                Math.Max(1f, height));
        }

        protected RectangleF GetDialogContentRect(RectangleF cardRect, RectangleF viewBox, float scale, Vector2 padding)
        {
            var left = cardRect.X + padding.X;
            var top = cardRect.Y + padding.Y;
            var right = cardRect.Right - padding.X;
            var bottom = cardRect.Bottom - padding.Y;

            if (IsTinyDialogAspectRatio(viewBox) && ShowCloseButton)
            {
                left = Math.Max(left, GetDialogCloseButtonRect(cardRect, scale).Right + GetDialogSpacing(viewBox, scale, 10f, 4f));
            }

            return new RectangleF(
                left,
                top,
                Math.Max(1f, right - left),
                Math.Max(1f, bottom - top));
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

        string ITextStyleProvider.ResolvedTextFont => TextFont;

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
            _owner = owner;
            _sprites.Clear();
            _dialogCardRegistered = false;
            _dialogViewBox = viewBox;
            _dialogViewBoxRegistered = true;

            if (Dismissed)
                return;

            BuildDialogControls(owner, viewBox, scale, fontScale, surface, textColor, backgroundColor, panelColor, cursorPosition);
            RenderCloseButton(scale);

            if (targetSprites != null)
                targetSprites.AddRange(_sprites);
        }

        protected void RequestRender()
        {
            MarkDirty();
            if (_owner != null)
                _owner.RenderSprites();
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
            var aspectRect = _dialogViewBoxRegistered ? _dialogViewBox : cardRect;
            if (IsTinyDialogAspectRatio(aspectRect))
            {
                var compactInset = 2f * scale;
                var width = Math.Max(24f * scale, Math.Min(Math.Max(1f, cardRect.Height - compactInset * 2f), 38f * scale));
                return new RectangleF(
                    cardRect.X + compactInset,
                    cardRect.Y + compactInset,
                    width,
                    Math.Max(1f, cardRect.Height - compactInset * 2f));
            }

            var size = GetDialogCloseButtonSize(scale);
            var inset = 8f * scale;
            return new RectangleF(
                cardRect.X + inset,
                cardRect.Y + inset,
                size.X,
                size.Y);
        }

        void RenderCloseButton(float scale)
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
            _closeButton.BorderRadiusPixels = BorderRenderer.DEFAULT_RADIUS_PIXELS;
            _closeButton.BorderThicknessPixels = 0f;
            _closeButton.SetStyleParent(this);
            _closeButton.CustomRender = RenderDialogCloseButton;
            _closeButton.SetCursor(CursorType.Hand);
            _closeButton.SetVisible(true);
        }

        void RenderDialogCloseButton(ControlTemplate control, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var hovered = control.IsPointerOver;
            var radiusPixels = CurrentDialogIsTiny
                ? BorderRenderer.DEFAULT_RADIUS_PIXELS
                : Math.Min(rect.Width, rect.Height) * 0.5f / Math.Max(0.001f, control.LayoutScale);
            var fillColor = hovered
                ? ResolveColor(ThemeResources.SurfaceContainerLowColor)
                : ResolveColor(ThemeResources.SurfaceContainerColor);

            BorderRenderer.CreateSpritesFromRect(
                rect,
                sprites,
                fillColor,
                radiusPixels: radiusPixels,
                radiusScale: control.LayoutScale);

            var iconSize = Math.Max(1f, Math.Min(rect.Width, rect.Height) - 10f * control.LayoutScale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = CurrentDialogIsTiny ? "LeftArrow" : "Cross",
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

        public override bool CanHover => Visible;

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
            var children = VisualChildren;
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
