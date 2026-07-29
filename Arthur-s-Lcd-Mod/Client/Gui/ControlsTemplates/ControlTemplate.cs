using System;
using System.Collections.Generic;
using LcdMod.Client.Animation;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using Sandbox.Game.Entities;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public delegate bool ControlScrollHandler(object dataContext, object sender, int delta);
    public delegate bool ControlHoverHandler(object dataContext, object sender);
    public delegate bool ControlDragHandler(object dataContext, object sender, Vector2 delta);
    delegate bool ControlHitFilter(ControlTemplate control);

    public delegate void InteractiveRenderHandler(ControlTemplate entry, List<MySprite> sprites);

    public interface ITextSurfaceProvider
    {
        IMyTextSurface TextSurface { get; }
    }

    public abstract partial class ControlTemplate : Control, ITextStyleProvider
    {
        public static readonly StyleProperty<Color> TextColorProperty =
            StyleProperty.Register<ControlTemplate, Color>("TextColor", null);

        public static readonly StyleProperty<string> TextFontProperty =
            StyleProperty.Register<ControlTemplate, string>("TextFont", "White");

        public static readonly StyleProperty<float> LayoutScaleProperty =
            StyleProperty.Register<ControlTemplate, float>("LayoutScale", 1f);

        public static readonly StyleProperty<float> FontScaleProperty =
            StyleProperty.Register<ControlTemplate, float>("FontScale", 1f);

        public static readonly StyleProperty<float> OpacityProperty =
            StyleProperty.Register<ControlTemplate, float>("Opacity", 1f);

        public static readonly StyleProperty<RenderTransform> RenderTransformProperty =
            StyleProperty.Register<ControlTemplate, RenderTransform>(
                "RenderTransform",
                LcdMod.Client.Animation.RenderTransform.Identity,
                false);

        public static readonly StyleProperty<Color> BackgroundColorProperty =
            StyleProperty.Register<ControlTemplate, Color>("BackgroundColor", (Color?)Color.Gray);

        public static readonly StyleProperty<Color> BorderColorProperty =
            StyleProperty.Register<ControlTemplate, Color>("BorderColor", (Color?)Color.Transparent);

        public static readonly StyleProperty<float> BorderRadiusPixelsProperty =
            StyleProperty.Register<ControlTemplate, float>("BorderRadiusPixels", (float?)BorderRenderer.DEFAULT_RADIUS_PIXELS);

        public static readonly StyleProperty<float> BorderThicknessPixelsProperty =
            StyleProperty.Register<ControlTemplate, float>("BorderThicknessPixels", (float?)0f);

        public static readonly StyleProperty<Vector4> PaddingProperty =
            StyleProperty.Register<ControlTemplate, Vector4>("Padding", (Vector4?)Vector4.Zero);
        
        internal ControlAnimationState AnimationState;

        RectangleF? _renderBoundsOverride;
        float _renderBorderRadiusInsetPixels;
        bool _isLayoutDirty = true;

        public bool WasMouseOver { get; private set; }
        public bool IsMouseOver { get; private set; }
        public bool IsPressed { get; private set; }

        public bool IsPointerOver => IsMouseOver;

        internal void SetMouseOver(bool value)
        {
            if (IsMouseOver == value)
                return;

            IsMouseOver = value;
            MarkDirty();
        }

        internal void SetPointerOver(bool value)
        {
            SetMouseOver(value);
        }

        internal void RestorePointerOverForRender()
        {
            IsMouseOver = true;
        }

        internal void SetPressed(bool value)
        {
            if (IsPressed == value)
                return;

            IsPressed = value;
            MarkDirty();
        }
        
        public override Control SetEnabled(bool enabled)
        {
            base.SetEnabled(enabled);
            OnEnabledChanged();
            return this;
        }

        protected virtual void OnEnabledChanged()
        {
        }

        readonly List<Control> _children = new List<Control>();
        public override IReadOnlyList<Control> LogicalChildren => _children;
        public override IReadOnlyList<Control> VisualChildren => _children;

        public bool HasChildren => _children.Count > 0;
        public ControlTemplate Parent { get; private set; }
        public string StyleId { get; private set; }

        public override IVisualStyleScope StyleParent => Parent ?? base.StyleParent;

        public bool IsLayoutDirty => _isLayoutDirty;

        public override bool IsDirty
        {
            get
            {
                if (_isDirty)
                    return true;

                for (int i = 0; i < _children.Count; i++)
                {
                    if (_children[i] != null && _children[i].IsDirty)
                        return true;
                }

                return false;
            }
        }
        
        public ControlTemplate SetStyleId(string styleId)
        {
            if (StyleId == styleId)
                return this;

            StyleId = styleId;
            MarkDirty();
            return this;
        }

        public ControlTemplate SetStyles(StyleTree styles)
        {
            if (ReferenceEquals(Styles, styles))
                return this;

            Styles = styles;
            MarkDirty();
            return this;
        }

        public ControlTemplate SetResources(ResourceTree resources)
        {
            if (ReferenceEquals(Resources, resources))
                return this;

            Resources = resources;
            MarkDirty();
            return this;
        }

        public void InvalidateLayout()
        {
            _isLayoutDirty = true;
            MarkDirty();

            if (Parent != null)
                Parent.OnChildLayoutInvalidated(this);
        }

        protected void ValidateLayout()
        {
            _isLayoutDirty = false;
        }

        protected virtual void OnChildLayoutInvalidated(ControlTemplate child)
        {
            InvalidateLayout();
        }

        public virtual void ClearChildren()
        {
            for (int i = 0; i < _children.Count; i++)
            {
                var child = _children[i] as ControlTemplate;
                if (child != null && ReferenceEquals(child.Parent, this))
                {
                    child.CancelAnimationTree(AnimationController);
                    child.Parent = null;
                    child.SetStyleParent(null);
                }
            }

            _children.Clear();
            OnChildrenChanged();
        }

        public virtual void AddChild(ControlTemplate child)
        {
            if (child == null)
                return;

            if (ReferenceEquals(child, this))
                throw new InvalidOperationException("A control cannot contain itself.");

            if (WouldCreateCycle(child))
                throw new InvalidOperationException("Adding the child would create a cycle.");

            if (ReferenceEquals(child.Parent, this))
            {
                if (!_children.Contains(child))
                {
                    _children.Add(child);
                    OnChildrenChanged();
                }

                return;
            }

            if (child.Parent != null)
                child.Parent.RemoveChild(child);

            if (!_children.Contains(child))
                _children.Add(child);

            child.Parent = this;
            OnChildrenChanged();
        }

        public virtual void AddChildren(IEnumerable<ControlTemplate> children)
        {
            if (children == null)
                return;

            foreach (var child in children)
                AddChild(child);
        }

        public virtual void AddOverlayEntries(List<Control> entries)
        {
            if (!Visible || entries == null)
                return;

            for (int i = 0; i < _children.Count; i++)
            {
                var child = _children[i] as ControlTemplate;
                if (child != null)
                    child.AddOverlayEntries(entries);
            }
        }

        public virtual bool RemoveChild(Control child)
        {
            
            var childControl = child as ControlTemplate;
            
            if (childControl == null || !_children.Remove(child))
                return false;

            if (ReferenceEquals(childControl.Parent, this))
                childControl.Parent = null;

            childControl.CancelAnimationTree(AnimationController);
            childControl.SetStyleParent(null);

            OnChildrenChanged();
            return true;
        }

        public virtual bool MoveChild(ControlTemplate child, int index)
        {
            if (child == null || !ReferenceEquals(child.Parent, this))
                return false;

            int currentIndex = _children.IndexOf(child);
            if (currentIndex < 0)
                return false;

            int targetIndex = Math.Max(0, Math.Min(index, _children.Count - 1));
            if (currentIndex == targetIndex)
                return false;

            _children.RemoveAt(currentIndex);
            _children.Insert(targetIndex, child);
            OnChildrenChanged();
            return true;
        }

        protected void AttachTo(ControlTemplate parent)
        {
            // Derived constructors call this after local initialization so parent invalidation observes a ready child.
            if (parent != null)
                parent.AddChild(this);
        }

        protected virtual void OnChildrenChanged()
        {
            InvalidateLayout();
        }

        bool WouldCreateCycle(ControlTemplate child)
        {
            for (var parent = this; parent != null; parent = parent.Parent)
            {
                if (ReferenceEquals(parent, child))
                    return true;
            }

            return false;
        }

        public virtual bool CanClick => CanPrimaryClick || CanSecondaryClick;

        public virtual bool CanPrimaryClick
        {
            get
            {
                var model = Model;
                return Visible && Enabled && (OnClick != null || model != null && model.CanClick);
            }
        }

        public virtual bool CanSecondaryClick
        {
            get
            {
                var model = Model;
                return Visible && Enabled && (OnSecondaryClick != null || model != null && model.CanSecondaryClick);
            }
        }

        public virtual bool CanScroll
        {
            get
            {
                var model = Model;
                return Visible && Enabled && (OnScroll != null || model != null && model.CanScroll);
            }
        }

        public virtual bool CanHover
        {
            get
            {
                var model = Model;
                return Visible && Enabled && (OnHover != null || model != null && model.CanHover);
            }
        }

        public virtual bool CanDrag => Visible && Enabled && Draggable && OnDrag != null;

        public virtual bool CanSecondaryDrag => Visible && Enabled && SecondaryDraggable && OnDrag != null;

        protected ControlTemplate(CursorType? cursor = null, object dataContext = null, Action<object, object> onClick = null,
            InteractiveTooltip tooltip = null)
        {
            DataContext = dataContext;
            OnClick = onClick;
            Tooltip = tooltip ?? Model?.Tooltip;
            Cursor = cursor ?? GetDefaultCursor(onClick, Model);
        }

        public CursorType Cursor { get; private set; }

        public ControlTemplate SetCursor(CursorType cursor)
        {
            Cursor = cursor;
            return this;
        }

        public ControlModelBase Model => DataContext as ControlModelBase;

        public ControlTemplate SetDataContext(object dataContext)
        {
            DataContext = dataContext;
            ApplyModelDefaults();
            return this;
        }

        public Action<object, object> OnClick { get; private set; }
        public Action<object, object> OnDoubleClick { get; private set; }
        public Action<object, object> OnSecondaryClick { get; set; }
        public ControlScrollHandler OnScroll { get; set; }
        public ControlHoverHandler OnHover { get; set; }
        public ControlDragHandler OnDrag { get; set; }
        public Action<object, object> OnBeginDrag { get; set; }
        public Action<object, object> OnEndDrag { get; set; }
        public bool Draggable { get; set; }
        public bool SecondaryDraggable { get; set; }
        public bool PreservePrimaryClickUntilDragged { get; set; }

        public bool ClickOnPress { get; set; }

        const long DOUBLE_CLICK_MAX_TICKS = TimeSpan.TicksPerMillisecond * 500L;
        long _lastPrimaryClickTicks = long.MinValue;

        protected virtual bool ClipContent => false;

        protected virtual RectangleF ClipContentBounds => Bounds;

        public ControlTemplate SetOnClick(Action<object, object> onClick)
        {
            OnClick = onClick;
            return this;
        }

        public ControlTemplate SetOnDoubleClick(Action<object, object> onDoubleClick)
        {
            OnDoubleClick = onDoubleClick;
            _lastPrimaryClickTicks = long.MinValue;
            return this;
        }

        public ControlTemplate SetOnScroll(ControlScrollHandler onScroll)
        {
            OnScroll = onScroll;
            return this;
        }

        public ControlTemplate SetOnHover(ControlHoverHandler onHover)
        {
            OnHover = onHover;
            return this;
        }

        public ControlTemplate SetOnDrag(ControlDragHandler onDrag)
        {
            OnDrag = onDrag;
            return this;
        }

        public ControlTemplate SetOnBeginDrag(Action<object, object> onBeginDrag)
        {
            OnBeginDrag = onBeginDrag;
            return this;
        }

        public ControlTemplate SetOnEndDrag(Action<object, object> onEndDrag)
        {
            OnEndDrag = onEndDrag;
            return this;
        }

        public ControlTemplate SetDraggable(bool draggable = true)
        {
            Draggable = draggable;
            return this;
        }

        public ControlTemplate SetSecondaryDraggable(bool draggable = true)
        {
            SecondaryDraggable = draggable;
            return this;
        }

        public ControlTemplate SetClickOnPress(bool clickOnPress = true)
        {
            ClickOnPress = clickOnPress;
            return this;
        }

        public InteractiveTooltip Tooltip { get; private set; }

        public ControlTemplate SetTooltip(InteractiveTooltip tooltip)
        {
            Tooltip = tooltip;
            return this;
        }

        public InteractiveRenderHandler CustomRender { get; set; }

        public abstract RectangleF Bounds { get; }

        public virtual Vector2 Measure(Vector2 availableSize)
        {
            return Bounds.Size;
        }

        public virtual void Arrange(RectangleF bounds)
        {
            ValidateLayout();
        }

        public MySoundPair ClickSound { get; set; } = AudioHelper.HudClick;
        public MySoundPair ClickFailSound { get; set; } = AudioHelper.HudUnable;

        public void Render(List<MySprite> sprites)
        {
            if (!Visible || sprites == null)
                return;

            this.UpdateStyleStateAnimations();

            int spriteStart = sprites.Count;
            RectangleF renderBounds = Bounds;
            RenderTransform renderTransform = RenderTransform ?? LcdMod.Client.Animation.RenderTransform.Identity;
            RectangleF? inheritedClip = GetInheritedClipBounds();
            bool rendered = false;

            try
            {
                var customRender = CustomRender ?? Model?.CustomRender;

                // Draw the interaction border at the control's normal bounds,
                // then render the button itself against an inset bounds. This
                // preserves the existing two-layer rounded rendering while
                // keeping the border inside the layout/hit-test rectangle.
                float borderInsetPixels = ShouldRenderStyleBorder()
                    ? RenderStyleBorder(sprites)
                    : 0f;

                if (borderInsetPixels > 0f)
                    BeginStyleBorderBackgroundInset(renderBounds, borderInsetPixels);

                if (customRender != null)
                    customRender(this, sprites);
                else
                    RenderDefault(sprites);

                rendered = true;
            }
            finally
            {
                EndStyleBorderBackgroundInset();

                if (rendered)
                {
                    this.ApplyRenderTransform(
                        sprites,
                        spriteStart,
                        renderBounds,
                        inheritedClip,
                        renderTransform);
                }

                CompleteRenderState();
            }
        }

        RectangleF? GetInheritedClipBounds()
        {
            ControlTemplate clipParent = FindClipContentParent();
            return clipParent != null
                ? (RectangleF?)clipParent.ClipContentBounds
                : null;
        }

        void CompleteRenderState()
        {
            bool wasMouseOver = IsMouseOver;
            bool changed = WasMouseOver != wasMouseOver;

            WasMouseOver = wasMouseOver;
            IsMouseOver = false;
            _isDirty = IsDirtyAfterRender() || changed;
        }

        protected virtual bool IsDirtyAfterRender()
        {
            return false;
        }

        protected virtual StyleState GetStyleState()
        {
            StyleState state = StyleState.None;

            if (IsMouseOver)
                state |= StyleState.Hover;

            if (IsPressed)
                state |= StyleState.Pressed;

            if (!Enabled)
                state |= StyleState.Disabled;

            return state;
        }

        internal StyleState GetStyleStateForResolver()
        {
            return GetStyleState();
        }

        protected TValue GetStyleValue<TValue>(
            StyleProperty<TValue> property,
            PropertyValue<TValue> value)
        {
            if (value.LocalOverride)
                return value.Local;

            TValue animatedValue;
            if (this.TryGetAnimatedStyleValue(property, out animatedValue))
                return animatedValue;

            if (IsDirty || HasDirtyStyleAncestor() || !value.HasCache)
            {
                value.Cache = ResolveStyleValue(property);
                value.HasCache = true;
            }

            return value.Cache;
        }


        bool HasDirtyStyleAncestor()
        {
            int guard = 0;
            for (IVisualStyleScope scope = StyleParent; scope != null && guard++ < 128;)
            {
                if (scope.IsDirty)
                    return true;

                IVisualStyleScope next = scope.StyleParent;
                if (ReferenceEquals(next, scope))
                    break;

                scope = next;
            }

            return false;
        }

        protected bool TryResolveStyleValue<TValue>(
            StyleProperty<TValue> property,
            out TValue value)
        {
            return TryResolveStyleValueForState(
                property,
                GetStyleStateForResolver(),
                out value);
        }

        internal bool TryResolveStyleValueForState<TValue>(
            StyleProperty<TValue> property,
            StyleState state,
            out TValue value)
        {
            int guard = 0;
            for (IVisualStyleScope scope = this; scope != null && guard++ < 128;)
            {
                StyleTree styles = scope.Styles;
                if (styles != null && styles.TryResolve(this, StyleId, state, property, out value))
                    return true;

                IVisualStyleScope next = scope.StyleParent;
                if (ReferenceEquals(next, scope))
                    break;

                scope = next;
            }

            value = default(TValue);
            return false;
        }

        protected TValue ResolveStyleValue<TValue>(StyleProperty<TValue> property)
        {
            return ResolveStyleValueForState(property, GetStyleStateForResolver());
        }

        internal TValue ResolveStyleValueForState<TValue>(
            StyleProperty<TValue> property,
            StyleState state)
        {
            TValue value;

            if (TryResolveStyleValueForState(property, state, out value))
                return value;

            if (property.Inherits && Parent != null)
                return Parent.ResolveStyleValue(property);

            if (property.HasDefaultValue)
                return property.DefaultValue;

            throw new ResourceKeyNotFoundException(
                property.OwnerType.Name + "." + property.Name,
                typeof(TValue).Name);
        }

        public Color GetResourceColor(ResourceKey<Color> key, Color fallback)
        {
            Color value;
            return ScopedResourceResolver.TryResolve(this, key, out value) ? value : fallback;
        }

        public Color ResolveColor(ResourceKey<Color> key)
        {
            Color value;
            if (ScopedResourceResolver.TryResolve(this, key, out value))
                return value;

            throw new ResourceKeyNotFoundException(key.Name, "ResourceTree");
        }

        protected virtual void RenderDefault(List<MySprite> sprites)
        {
            var rect = GetViewBox();
            var fillColor = GetRenderBackgroundColor();

            BorderRenderer.CreateSpritesFromRect(rect, sprites, fillColor,
                radiusPixels: GetRenderBorderRadiusPixels(),
                radiusScale: LayoutScale);
            RenderDefaultText(rect, sprites);
        }

        protected virtual Color GetRenderBackgroundColor()
        {
            return BackgroundColor;
        }

        protected virtual bool ShouldRenderStyleBorder()
        {
            return false;
        }

        float RenderStyleBorder(List<MySprite> sprites)
        {
            RectangleF rect = GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return 0f;

            float thicknessPixels = Math.Max(0f, BorderThicknessPixels);
            Color borderColor = ApplyOpacity(BorderColor);
            if (thicknessPixels <= 0f || borderColor.A == 0)
                return 0f;

            BorderRenderer.CreateBorderSpritesFromRect(
                rect,
                sprites,
                GetRenderBackgroundColor(),
                borderColor,
                GetRenderBorderRadiusPixels(),
                LayoutScale,
                thicknessPixels);

            return thicknessPixels;
        }

        void BeginStyleBorderBackgroundInset(RectangleF bounds, float thicknessPixels)
        {
            float scaledThickness = Math.Max(0f, thicknessPixels * Math.Max(0f, LayoutScale));
            if (scaledThickness <= 0f)
                return;

            _renderBoundsOverride = new RectangleF(
                bounds.X + scaledThickness,
                bounds.Y + scaledThickness,
                Math.Max(0f, bounds.Width - scaledThickness * 2f),
                Math.Max(0f, bounds.Height - scaledThickness * 2f));
            _renderBorderRadiusInsetPixels = thicknessPixels;
        }

        void EndStyleBorderBackgroundInset()
        {
            _renderBoundsOverride = null;
            _renderBorderRadiusInsetPixels = 0f;
        }

        internal RectangleF GetRenderBounds(RectangleF bounds)
        {
            return _renderBoundsOverride ?? bounds;
        }

        internal float GetEffectiveRenderBorderRadiusPixels()
        {
            return Math.Max(0f, BorderRadiusPixels - _renderBorderRadiusInsetPixels);
        }

        protected Color ApplyOpacity(Color color)
        {
            float opacity = MathHelper.Clamp(Opacity, 0f, 1f);
            byte alpha = (byte)Math.Round(color.A * opacity);
            return new Color(color.R, color.G, color.B, alpha);
        }

        protected virtual Color GetRenderTextColor()
        {
            return TextColor;
        }

        protected virtual string GetRenderTextFont()
        {
            return TextFont;
        }

        string ITextStyleProvider.ResolvedTextFont => GetRenderTextFont();

        protected virtual float GetRenderBorderRadiusPixels()
        {
            return GetEffectiveRenderBorderRadiusPixels();
        }

        public RectangleF GetViewBox()
        {
            return ApplyPadding(Bounds, GetLocalPadding());
        }

        protected Vector4 GetLocalPadding()
        {
            return Padding;
        }

        static RectangleF ApplyPadding(RectangleF bounds, Vector4 padding)
        {
            if (bounds.Width <= 0f || bounds.Height <= 0f ||
                padding.X == 0f && padding.Y == 0f && padding.Z == 0f && padding.W == 0f)
                return bounds;

            float left = ClampPadding(padding.X);
            float top = ClampPadding(padding.Y);
            float right = ClampPadding(padding.Z);
            float bottom = ClampPadding(padding.W);

            var x = bounds.X + bounds.Width * left;
            var y = bounds.Y + bounds.Height * top;
            var width = Math.Max(0f, bounds.Width * (1f - left - right));
            var height = Math.Max(0f, bounds.Height * (1f - top - bottom));

            return new RectangleF(x, y, width, height);
        }

        static float ClampPadding(float value)
        {
            if (value < 0f)
                return 0f;

            if (value > 1f)
                return 1f;

            return value;
        }

        protected void RenderDefaultText(RectangleF rect, List<MySprite> sprites)
        {
            RenderDefaultText(rect, sprites, GetRenderTextColor());
        }

        protected void RenderDefaultText(RectangleF rect, List<MySprite> sprites, Color color)
        {
            string text = DataContext != null ? DataContext.ToString() : string.Empty;

            if (string.IsNullOrEmpty(text))
                return;

            string fontId = GetRenderTextFont();
            float textScale = 0.58f * LayoutScale * FontScale;
            var textSize = MeasureText(text, textScale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textSize.Y * 0.5f),
                Color = color,
                FontId = fontId,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }

        public IMyTextSurface TextSurface => ResolveTextSurface();

        public Vector2 MeasureText(string text, float scale)
        {
            var surface = TextSurface;
            return surface != null ? FormatingHelper.GetSizeInPixel(text, this, scale, surface) : Vector2.Zero;
        }

        public Vector2 MeasureText(string text, string fontId, float scale)
        {
            var surface = TextSurface;
            if (surface == null || string.IsNullOrEmpty(text))
                return Vector2.Zero;

            return FormatingHelper.GetSizeInPixel(text, fontId, scale, surface);
        }

        public float GetLineHeight(float scale)
        {
            var surface = TextSurface;
            return surface != null ? FormatingHelper.LineHeight(scale, this, surface) : 0f;
        }

        public float GetLineHeight(float scale, string fontId)
        {
            var surface = TextSurface;
            return surface != null ? FormatingHelper.LineHeight(scale, surface, fontId) : 0f;
        }

        IMyTextSurface ResolveTextSurface()
        {
            int guard = 0;
            for (IVisualStyleScope scope = this; scope != null && guard++ < 128;)
            {
                var provider = scope as ITextSurfaceProvider;
                if (provider != null && provider.TextSurface != null)
                    return provider.TextSurface;

                IVisualStyleScope next = scope.StyleParent;
                if (ReferenceEquals(next, scope))
                    break;

                scope = next;
            }

            return null;
        }

        public bool Hit(Vector2 point)
        {
            return Visible && Enabled && HitCore(point);
        }

        protected abstract bool HitCore(Vector2 point);

        public bool TryResolveHit(Vector2 point, out ControlTemplate hit)
        {
            hit = ResolveHit(point, AcceptAnyHit);
            return hit != null;
        }

        public bool TryResolveClickable(Vector2 point, out ControlTemplate clickable)
        {
            clickable = ResolveHit(point, AcceptClickableHit);
            return clickable != null;
        }

        public bool TryResolvePrimaryClickable(Vector2 point, out ControlTemplate clickable)
        {
            clickable = ResolveHit(point, AcceptPrimaryClickableHit);
            return clickable != null;
        }

        public bool TryResolveSecondaryClickable(Vector2 point, out ControlTemplate clickable)
        {
            clickable = ResolveHit(point, AcceptSecondaryClickableHit);
            return clickable != null;
        }

        public bool TryResolveScrollable(Vector2 point, out ControlTemplate scrollable)
        {
            scrollable = ResolveHit(point, AcceptScrollableHit);
            return scrollable != null;
        }

        public bool TryResolveHoverable(Vector2 point, out ControlTemplate hoverable)
        {
            hoverable = ResolveHit(point, AcceptHoverableHit);
            return hoverable != null;
        }

        public bool TryResolveDraggable(Vector2 point, out ControlTemplate draggable)
        {
            draggable = ResolveHit(point, AcceptDraggableHit);
            return draggable != null;
        }

        public bool TryResolveDraggable(Vector2 point, bool secondary, out ControlTemplate draggable)
        {
            ControlHitFilter accept = secondary
                ? (ControlHitFilter)AcceptSecondaryDraggableHit
                : AcceptDraggableHit;
            draggable = ResolveHit(point, accept);
            return draggable != null;
        }

        public bool TryResolveTooltipTarget(Vector2 point, out ControlTemplate tooltipTarget)
        {
            tooltipTarget = ResolveHit(point, AcceptTooltipHit);
            return tooltipTarget != null;
        }

        public CursorType GetCursor(Vector2 point)
        {
            ControlTemplate hit;
            return TryResolveHit(point, out hit) ? hit.Cursor : CursorType.Default;
        }

        public bool Click(Vector2 point, object sender)
        {
            ControlTemplate clickable;
            return TryResolvePrimaryClickable(point, out clickable) && clickable.ClickAt(point, sender);
        }

        public bool SecondaryClick(Vector2 point, object sender)
        {
            ControlTemplate clickable;
            return TryResolveSecondaryClickable(point, out clickable) && clickable.SecondaryClickAt(point, sender);
        }

        public bool Scroll(Vector2 point, object sender, int delta)
        {
            ControlTemplate scrollable;
            return TryResolveScrollable(point, out scrollable) && scrollable.Scroll(sender, delta);
        }

        public bool Hover(Vector2 point, object sender)
        {
            ControlTemplate hoverable;
            return TryResolveHoverable(point, out hoverable) && hoverable.Hover(sender);
        }

        public virtual bool ClickAt(Vector2 point, object sender)
        {
            return Click(sender);
        }

        public virtual bool SecondaryClickAt(Vector2 point, object sender)
        {
            return SecondaryClick(sender);
        }

        public virtual bool Click(object sender)
        {
            if (OnDoubleClick != null)
            {
                long now = DateTime.UtcNow.Ticks;
                long elapsed = now - _lastPrimaryClickTicks;
                _lastPrimaryClickTicks = now;
                if (elapsed >= 0L && elapsed <= DOUBLE_CLICK_MAX_TICKS)
                {
                    _lastPrimaryClickTicks = long.MinValue;
                    OnDoubleClick(DataContext ?? this, sender);
                    return true;
                }
            }

            return HandleClick(sender, OnClick, false);
        }

        public virtual bool SecondaryClick(object sender) => HandleClick(sender, OnSecondaryClick, true);

        internal bool HandleClick(object sender, Action<object, object> handler, bool secondary)
        {
            if (!Visible || !Enabled)
                return false;

            if (handler != null)
            {
                handler(DataContext ?? this, sender);
                return true;
            }

            var model = Model;

            if (model == null)
                return false;

            return secondary ? model.SecondaryClick(sender) : model.Click(sender);
        }

        public virtual bool Scroll(object sender, int delta)
        {
            if (!Visible || !Enabled)
                return false;

            if (OnScroll != null)
                return OnScroll(DataContext ?? this, sender, delta);

            var model = Model;
            return model != null && model.Scroll(sender, delta);
        }

        public virtual bool Hover(object sender)
        {
            if (!Visible || !Enabled)
                return false;

            if (OnHover != null)
                return OnHover(DataContext ?? this, sender);

            var model = Model;
            return model != null && model.Hover(sender);
        }

        public virtual bool BeginDrag(object sender)
        {
            if (!CanDrag)
                return false;

            if (OnBeginDrag != null)
                OnBeginDrag(DataContext ?? this, sender);

            return true;
        }

        public virtual bool BeginDrag(object sender, bool secondary)
        {
            if (secondary)
            {
                if (!CanSecondaryDrag)
                    return false;

                if (OnBeginDrag != null)
                    OnBeginDrag(DataContext ?? this, sender);

                return true;
            }

            return BeginDrag(sender);
        }

        public virtual bool Drag(object sender, Vector2 delta)
        {
            if (!CanDrag || !IsValidDelta(delta))
                return false;

            return OnDrag != null && OnDrag(DataContext ?? this, sender, delta);
        }

        public virtual bool Drag(object sender, Vector2 delta, bool secondary)
        {
            if (secondary)
            {
                if (!CanSecondaryDrag || !IsValidDelta(delta))
                    return false;

                return OnDrag != null && OnDrag(DataContext ?? this, sender, delta);
            }

            return Drag(sender, delta);
        }

        public virtual void EndDrag(object sender)
        {
            if (OnEndDrag != null)
                OnEndDrag(DataContext ?? this, sender);
        }

        ControlTemplate ResolveHit(Vector2 point, ControlHitFilter accept)
        {
            if (!Visible || !Enabled)
                return null;

            bool selfHit = HitCore(point);

            if (CanResolveChildren(point, selfHit) && _children.Count > 0)
            {
                for (int i = _children.Count - 1; i >= 0; i--)
                {
                    var childHit = (_children[i] as ControlTemplate)?.ResolveHit(point, accept);

                    if (childHit != null)
                        return childHit;
                }
            }

            return selfHit && accept(this) ? this : null;
        }

        ControlTemplate FindClipContentParent()
        {
            for (var parent = Parent; parent != null; parent = parent.Parent)
            {
                if (parent.ClipContent)
                    return parent;
            }

            return null;
        }

        protected bool BeginContentClip(List<MySprite> sprites, RectangleF bounds)
        {
            RectangleF clip;
            if (!TryResolveClip(bounds, out clip))
                return false;

            AddClip(sprites, clip);
            return true;
        }

        protected void EndContentClip(List<MySprite> sprites)
        {
            if (sprites == null)
                return;

            sprites.Add(MySprite.CreateClearClipRect());

            var clipParent = FindClipContentParent();
            if (clipParent != null)
                AddClip(sprites, clipParent.ClipContentBounds);
        }

        bool TryResolveClip(RectangleF bounds, out RectangleF clip)
        {
            var clipParent = FindClipContentParent();
            clip = clipParent != null ? Intersect(bounds, clipParent.ClipContentBounds) : bounds;
            return clip.Width > 0f && clip.Height > 0f;
        }

        static RectangleF Intersect(RectangleF a, RectangleF b)
        {
            float x = Math.Max(a.X, b.X);
            float y = Math.Max(a.Y, b.Y);
            float right = Math.Min(a.Right, b.Right);
            float bottom = Math.Min(a.Bottom, b.Bottom);
            return new RectangleF(x, y, Math.Max(0f, right - x), Math.Max(0f, bottom - y));
        }

        static void AddClip(List<MySprite> sprites, RectangleF bounds)
        {
            if (sprites == null)
                return;

            int x = (int)Math.Floor(bounds.X);
            int y = (int)Math.Floor(bounds.Y);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);
            sprites.Add(MySprite.CreateClipRect(new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y))));
        }

        protected virtual bool CanResolveChildren(Vector2 point, bool selfHit)
        {
            return selfHit;
        }

        static bool AcceptAnyHit(ControlTemplate control)
        {
            return true;
        }

        static bool AcceptClickableHit(ControlTemplate control)
        {
            return control.CanClick;
        }

        static bool AcceptPrimaryClickableHit(ControlTemplate control)
        {
            return control.CanPrimaryClick;
        }

        static bool AcceptSecondaryClickableHit(ControlTemplate control)
        {
            return control.CanSecondaryClick;
        }

        static bool AcceptScrollableHit(ControlTemplate control)
        {
            return control.CanScroll;
        }

        static bool AcceptHoverableHit(ControlTemplate control)
        {
            return control.CanHover;
        }

        static bool AcceptDraggableHit(ControlTemplate control)
        {
            return control.CanDrag;
        }

        static bool AcceptSecondaryDraggableHit(ControlTemplate control)
        {
            return control.CanSecondaryDrag;
        }

        static bool AcceptTooltipHit(ControlTemplate control)
        {
            return control.Tooltip != null;
        }

        static CursorType GetDefaultCursor(Action<object, object> onClick, ControlModelBase model)
        {
            if (onClick != null)
                return CursorType.Hand;

            if (model != null)
            {
                if (model.Cursor != CursorType.Default)
                    return model.Cursor;

                if (model.CanClick || model.CanSecondaryClick)
                    return CursorType.Hand;
            }

            return CursorType.Default;
        }

        void ApplyModelDefaults()
        {
            var model = Model;

            if (model == null)
                return;

            if (Tooltip == null)
                Tooltip = model.Tooltip;

            if (Cursor == CursorType.Default)
                Cursor = GetDefaultCursor(OnClick, model);
        }

        static bool IsValidDelta(Vector2 delta)
        {
            return !float.IsNaN(delta.X) && !float.IsNaN(delta.Y);
        }
    }
}
