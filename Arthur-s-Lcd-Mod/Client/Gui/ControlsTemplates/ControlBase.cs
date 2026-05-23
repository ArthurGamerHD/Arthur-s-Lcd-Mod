using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using Sandbox.Game.Entities;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public abstract class ControlBase
    {
        public bool Visible { get; private set; } = true;

        public void SetVisible(bool visible)
        {
            Visible = visible;
        }

        readonly List<ControlBase> _children = new List<ControlBase>();

        public IList<ControlBase> Children => _children;

        public bool HasChildren => _children.Count > 0;

        public void ClearChildren()
        {
            _children.Clear();
        }

        public void AddChild(ControlBase child)
        {
            if (child != null && !_children.Contains(child))
                _children.Add(child);
        }

        public void AddChildren(IEnumerable<ControlBase> children)
        {
            if (children == null)
                return;

            foreach (var child in children)
                AddChild(child);
        }

        public virtual bool CanClick
        {
            get
            {
                var model = Model;
                return Visible && (OnClick != null || OnSecondaryClick != null ||
                                   model != null && (model.CanClick || model.CanSecondaryClick));
            }
        }

        protected ControlBase(CursorType? cursor = null, object dataContext = null, Action<object, object> onClick = null,
            InteractiveTooltip tooltip = null)
        {
            DataContext = dataContext;
            OnClick = onClick;
            Tooltip = tooltip ?? Model?.Tooltip;
            Style = Model?.Style;
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

        public ControlBase SetOnClick(Action<object, object> onClick)
        {
            OnClick = onClick;
            return this;
        }

        public InteractiveTooltip Tooltip { get; private set; }
        public ControlStyle Style { get; private set; }
        bool _styleExplicitlySet;

        public ControlBase SetTooltip(InteractiveTooltip tooltip)
        {
            Tooltip = tooltip;
            return this;
        }

        public ControlBase SetStyle(ControlStyle style)
        {
            Style = style;
            _styleExplicitlySet = true;
            return this;
        }

        public InteractiveRenderHandler CustomRender { get; set; }

        public abstract RectangleF Bounds { get; }
        public MySoundPair ClickSound { get; set; } = AudioHelper.HudClick;
        public MySoundPair ClickFailSound { get; set; } = AudioHelper.HudUnable;

        public void Render(ControlRenderContext context, List<MySprite> sprites)
        {
            if (context == null || sprites == null)
                return;

            var renderContext = ResolveRenderContext(context);
            var customRender = CustomRender ?? Model?.CustomRender;
            if (customRender != null)
            {
                customRender(this, renderContext, sprites);
                return;
            }

            RenderDefault(renderContext, sprites);
        }

        protected virtual void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = Bounds;
            var hovered = rect.Contains(context.CursorPosition);
            var fillColor = context.Style.GetPanelColor(hovered);

            Border.CreateSpritesFromRect(rect, sprites, fillColor, context.Style.BorderPercentage);
            RenderDefaultText(rect, context, sprites);
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
                Color = context.Style.GetTextColor(rect.Contains(context.CursorPosition)),
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }

        public bool Hit(Vector2 point)
        {
            return Visible && HitCore(point);
        }

        protected abstract bool HitCore(Vector2 point);

        public virtual bool Click(object sender)=> HandleClick(sender, OnClick, false);
        
        public virtual bool SecondaryClick(object sender) => HandleClick(sender, OnSecondaryClick, true);

        internal bool HandleClick(object sender, Action<object, object> handler, bool secondary)
        {
            if (!Visible)
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

            if (!_styleExplicitlySet)
                Style = model.Style;

            if (Cursor == CursorType.Default)
                Cursor = GetDefaultCursor(OnClick, model);
        }

        ControlRenderContext ResolveRenderContext(ControlRenderContext context)
        {
            var style = Style ?? Model?.Style;
            if (style == null || ReferenceEquals(style, context.Style))
                return context;

            return new ControlRenderContext(
                context.Surface,
                context.Scale,
                context.FontScale,
                style,
                context.CursorPosition);
        }
    }
}
