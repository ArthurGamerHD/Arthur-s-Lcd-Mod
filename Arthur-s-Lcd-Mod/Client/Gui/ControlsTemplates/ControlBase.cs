using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using Sandbox.Game.Entities;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public delegate bool ControlScrollHandler(object dataContext, object sender, int delta);
    public delegate bool ControlHoverHandler(object dataContext, object sender);
    public delegate bool ControlDragHandler(object dataContext, object sender, Vector2 delta);
    delegate bool ControlHitFilter(ControlBase control);

    public abstract class ControlBase
    {
        bool _isDirty;
        bool _isLayoutDirty = true;

        public bool Visible { get; private set; } = true;
        public bool Enabled { get; private set; } = true;

        public bool IsPointerOver { get; private set; }

        internal void SetPointerOver(bool value)
        {
            IsPointerOver = value;
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
        }

        public ControlBase SetEnabled(bool enabled)
        {
            if (Enabled == enabled)
                return this;

            Enabled = enabled;
            OnEnabledChanged();
            MarkDirty();
            return this;
        }

        protected virtual void OnEnabledChanged()
        {
        }

        readonly List<ControlBase> _children = new List<ControlBase>();
        public IReadOnlyList<ControlBase> Children => _children;

        public bool HasChildren => _children.Count > 0;
        public ControlBase Parent { get; private set; }

        public bool IsLayoutDirty
        {
            get { return _isLayoutDirty; }
        }

        public bool IsDirty
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

        public void MarkDirty()
        {
            _isDirty = true;
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

        protected virtual void OnChildLayoutInvalidated(ControlBase child)
        {
            InvalidateLayout();
        }

        public virtual void ClearChildren()
        {
            for (int i = 0; i < _children.Count; i++)
            {
                var child = _children[i];
                if (child != null && ReferenceEquals(child.Parent, this))
                    child.Parent = null;
            }

            _children.Clear();
            OnChildrenChanged();
        }

        public virtual void AddChild(ControlBase child)
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

        public virtual void AddChildren(IEnumerable<ControlBase> children)
        {
            if (children == null)
                return;

            foreach (var child in children)
                AddChild(child);
        }

        public virtual void AddOverlayEntries(List<ControlBase> entries)
        {
            if (!Visible || entries == null)
                return;

            for (int i = 0; i < _children.Count; i++)
            {
                var child = _children[i];
                if (child != null)
                    child.AddOverlayEntries(entries);
            }
        }

        public virtual bool RemoveChild(ControlBase child)
        {
            if (child == null || !_children.Remove(child))
                return false;

            if (ReferenceEquals(child.Parent, this))
                child.Parent = null;

            OnChildrenChanged();
            return true;
        }

        public virtual bool MoveChild(ControlBase child, int index)
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

        protected void AttachTo(ControlBase parent)
        {
            // Derived constructors call this after local initialization so parent invalidation observes a ready child.
            if (parent != null)
                parent.AddChild(this);
        }

        protected virtual void OnChildrenChanged()
        {
            InvalidateLayout();
        }

        bool WouldCreateCycle(ControlBase child)
        {
            for (var parent = this; parent != null; parent = parent.Parent)
            {
                if (ReferenceEquals(parent, child))
                    return true;
            }

            return false;
        }

        public virtual bool CanClick
        {
            get { return CanPrimaryClick || CanSecondaryClick; }
        }

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

        public virtual bool CanDrag
        {
            get { return Visible && Enabled && Draggable && OnDrag != null; }
        }

        protected ControlBase(CursorType? cursor = null, object dataContext = null, Action<object, object> onClick = null,
            InteractiveTooltip tooltip = null)
        {
            DataContext = dataContext;
            OnClick = onClick;
            Tooltip = tooltip ?? Model?.Tooltip;
            Cursor = cursor ?? GetDefaultCursor(onClick, Model);
        }

        public CursorType Cursor { get; private set; }

        public ControlBase SetCursor(CursorType cursor)
        {
            Cursor = cursor;
            return this;
        }

        public object DataContext { get; private set; }
        public ControlModelBase Model => DataContext as ControlModelBase;

        public ControlBase SetDataContext(object dataContext)
        {
            DataContext = dataContext;
            ApplyModelDefaults();
            return this;
        }

        public Action<object, object> OnClick { get; private set; }
        public Action<object, object> OnSecondaryClick { get; set; }
        public ControlScrollHandler OnScroll { get; set; }
        public ControlHoverHandler OnHover { get; set; }
        public ControlDragHandler OnDrag { get; set; }
        public Action<object, object> OnBeginDrag { get; set; }
        public Action<object, object> OnEndDrag { get; set; }
        public bool Draggable { get; set; }

        public bool ClickOnPress { get; set; }

        protected virtual bool ClipContent
        {
            get { return false; }
        }

        protected virtual RectangleF ClipContentBounds
        {
            get { return Bounds; }
        }

        public ControlBase SetOnClick(Action<object, object> onClick)
        {
            OnClick = onClick;
            return this;
        }

        public ControlBase SetOnScroll(ControlScrollHandler onScroll)
        {
            OnScroll = onScroll;
            return this;
        }

        public ControlBase SetOnHover(ControlHoverHandler onHover)
        {
            OnHover = onHover;
            return this;
        }

        public ControlBase SetOnDrag(ControlDragHandler onDrag)
        {
            OnDrag = onDrag;
            return this;
        }

        public ControlBase SetOnBeginDrag(Action<object, object> onBeginDrag)
        {
            OnBeginDrag = onBeginDrag;
            return this;
        }

        public ControlBase SetOnEndDrag(Action<object, object> onEndDrag)
        {
            OnEndDrag = onEndDrag;
            return this;
        }

        public ControlBase SetDraggable(bool draggable = true)
        {
            Draggable = draggable;
            return this;
        }

        public ControlBase SetClickOnPress(bool clickOnPress = true)
        {
            ClickOnPress = clickOnPress;
            return this;
        }

        public InteractiveTooltip Tooltip { get; private set; }

        public ControlStyle Style { get; private set; }
        public ControlStyleOverride StyleOverride { get; private set; }

        public ControlBase SetTooltip(InteractiveTooltip tooltip)
        {
            Tooltip = tooltip;
            return this;
        }

        public ControlBase SetStyle(ControlStyle style)
        {
            Style = style;
            StyleOverride = null;
            MarkDirty();
            return this;
        }

        public ControlBase SetStyleOverride(ControlStyleOverride style)
        {
            StyleOverride = style;
            Style = null;
            MarkDirty();
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

        public void Render(ControlRenderContext context, List<MySprite> sprites)
        {
            if (!Visible || context == null || sprites == null)
                return;

            try
            {
                var renderContext = ResolveRenderContext(context);
                var customRender = CustomRender ?? Model?.CustomRender;

                if (customRender != null)
                {
                    customRender(this, renderContext, sprites);
                    return;
                }

                RenderDefault(renderContext, sprites);
            }
            finally
            {
                _isDirty = IsDirtyAfterRender();
                IsPointerOver = false;
            }
        }

        protected virtual bool IsDirtyAfterRender()
        {
            return false;
        }

        protected virtual void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = GetViewBox();
            var hovered = IsPointerOver;
            var fillColor = context.Style.GetPanelColor(hovered);

            Border.CreateSpritesFromRect(rect, sprites, fillColor,
                radiusScale: context.Scale);
            RenderDefaultText(rect, context, sprites);
        }

        public RectangleF GetViewBox()
        {
            return ApplyPadding(Bounds, GetLocalPadding());
        }

        protected Vector4 GetLocalPadding()
        {
            var style = GetLocalStyle();
            return style == null ? Vector4.Zero : style.Padding;
        }

        ControlStyle GetLocalStyle()
        {
            if (Style != null)
                return Style;

            if (StyleOverride != null && StyleOverride.Padding.HasValue)
                return StyleOverride.ResolveAgainst(null, null);

            return null;
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

        protected void RenderDefaultText(RectangleF rect, ControlRenderContext context, List<MySprite> sprites)
        {
            string text = DataContext != null ? DataContext.ToString() : string.Empty;

            if (string.IsNullOrEmpty(text))
                return;

            float textScale = 0.58f * context.Scale * context.FontScale;
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, context.Surface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textSize.Y * 0.5f),
                Color = context.Style.GetTextColor(IsPointerOver),
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }

        public bool Hit(Vector2 point)
        {
            return Visible && Enabled && HitCore(point);
        }

        protected abstract bool HitCore(Vector2 point);

        public bool TryResolveHit(Vector2 point, out ControlBase hit)
        {
            hit = ResolveHit(point, AcceptAnyHit);
            return hit != null;
        }

        public bool TryResolveClickable(Vector2 point, out ControlBase clickable)
        {
            clickable = ResolveHit(point, AcceptClickableHit);
            return clickable != null;
        }

        public bool TryResolvePrimaryClickable(Vector2 point, out ControlBase clickable)
        {
            clickable = ResolveHit(point, AcceptPrimaryClickableHit);
            return clickable != null;
        }

        public bool TryResolveSecondaryClickable(Vector2 point, out ControlBase clickable)
        {
            clickable = ResolveHit(point, AcceptSecondaryClickableHit);
            return clickable != null;
        }

        public bool TryResolveScrollable(Vector2 point, out ControlBase scrollable)
        {
            scrollable = ResolveHit(point, AcceptScrollableHit);
            return scrollable != null;
        }

        public bool TryResolveHoverable(Vector2 point, out ControlBase hoverable)
        {
            hoverable = ResolveHit(point, AcceptHoverableHit);
            return hoverable != null;
        }

        public bool TryResolveDraggable(Vector2 point, out ControlBase draggable)
        {
            draggable = ResolveHit(point, AcceptDraggableHit);
            return draggable != null;
        }

        public bool TryResolveTooltipTarget(Vector2 point, out ControlBase tooltipTarget)
        {
            tooltipTarget = ResolveHit(point, AcceptTooltipHit);
            return tooltipTarget != null;
        }

        public CursorType GetCursor(Vector2 point)
        {
            ControlBase hit;
            return TryResolveHit(point, out hit) ? hit.Cursor : CursorType.Default;
        }

        public bool Click(Vector2 point, object sender)
        {
            ControlBase clickable;
            return TryResolvePrimaryClickable(point, out clickable) && clickable.ClickAt(point, sender);
        }

        public bool SecondaryClick(Vector2 point, object sender)
        {
            ControlBase clickable;
            return TryResolveSecondaryClickable(point, out clickable) && clickable.SecondaryClickAt(point, sender);
        }

        public bool Scroll(Vector2 point, object sender, int delta)
        {
            ControlBase scrollable;
            return TryResolveScrollable(point, out scrollable) && scrollable.Scroll(sender, delta);
        }

        public bool Hover(Vector2 point, object sender)
        {
            ControlBase hoverable;
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

        public virtual bool Click(object sender) => HandleClick(sender, OnClick, false);

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

        public virtual bool Drag(object sender, Vector2 delta)
        {
            if (!CanDrag || !IsValidDelta(delta))
                return false;

            return OnDrag != null && OnDrag(DataContext ?? this, sender, delta);
        }

        public virtual void EndDrag(object sender)
        {
            if (OnEndDrag != null)
                OnEndDrag(DataContext ?? this, sender);
        }

        ControlBase ResolveHit(Vector2 point, ControlHitFilter accept)
        {
            if (!Visible || !Enabled)
                return null;

            bool selfHit = HitCore(point);

            if (CanResolveChildren(point, selfHit) && _children.Count > 0)
            {
                for (int i = _children.Count - 1; i >= 0; i--)
                {
                    var childHit = _children[i].ResolveHit(point, accept);

                    if (childHit != null)
                        return childHit;
                }
            }

            return selfHit && accept(this) ? this : null;
        }

        ControlBase FindClipContentParent()
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

        static bool AcceptAnyHit(ControlBase control)
        {
            return true;
        }

        static bool AcceptClickableHit(ControlBase control)
        {
            return control.CanClick;
        }

        static bool AcceptPrimaryClickableHit(ControlBase control)
        {
            return control.CanPrimaryClick;
        }

        static bool AcceptSecondaryClickableHit(ControlBase control)
        {
            return control.CanSecondaryClick;
        }

        static bool AcceptScrollableHit(ControlBase control)
        {
            return control.CanScroll;
        }

        static bool AcceptHoverableHit(ControlBase control)
        {
            return control.CanHover;
        }

        static bool AcceptDraggableHit(ControlBase control)
        {
            return control.CanDrag;
        }

        static bool AcceptTooltipHit(ControlBase control)
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

        ControlRenderContext ResolveRenderContext(ControlRenderContext context)
        {
            ControlStyle style;

            if (StyleOverride != null)
            {
                style = StyleOverride.ResolveAgainst(context.Style, context.Theme);
            }
            else
            {
                style = Style;

                if (style == null || ReferenceEquals(style, context.Style))
                    return context;

                style = style.ResolveAgainst(context.Style, context.Theme);
            }

            return context.WithStyle(style);
        }

        static bool IsValidDelta(Vector2 delta)
        {
            return !float.IsNaN(delta.X) && !float.IsNaN(delta.Y);
        }
    }
}
