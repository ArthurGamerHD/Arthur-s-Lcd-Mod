using System;
using System.Collections.Generic;
using Graph.Helpers;
using Sandbox.Game.Entities;
using VRageMath;

namespace Graph.Apps.Utility
{
    public interface ITooltipLine
    {
        string GetText();
        object GetDataContext();
        Action<object, object> GetOnClick();
        MySoundPair GetClickSound();
        CursorType? GetCursor();
        bool IsClickable { get; }
    }

    public sealed class DynamicTooltipLine : ITooltipLine
    {
        readonly Func<string> _getText;
        readonly Func<bool> _isClickable;
        readonly Func<object> _getDataContext;
        readonly Func<Action<object, object>> _getOnClick;
        readonly Func<CursorType?> _getCursor;
        readonly Func<MySoundPair> _getClickSound;

        public DynamicTooltipLine(
            Func<string> getText,
            Func<bool> isClickable = null,
            Func<object> getDataContext = null,
            Func<Action<object, object>> getOnClick = null,
            Func<CursorType?> getCursor = null,
            Func<MySoundPair> getClickSound = null)
        {
            _getText = getText;
            _isClickable = isClickable;
            _getDataContext = getDataContext;
            _getOnClick = getOnClick;
            _getCursor = getCursor;
            _getClickSound = getClickSound;
        }

        public string GetText()
        {
            return _getText != null ? _getText() : string.Empty;
        }

        public bool IsClickable => _isClickable != null && _isClickable();

        public object GetDataContext() => _getDataContext != null ? _getDataContext() : this;

        public Action<object, object> GetOnClick() => _getOnClick?.Invoke();

        public CursorType? GetCursor() => _getCursor?.Invoke();

        public MySoundPair GetClickSound() => _getClickSound?.Invoke();

        public override string ToString() => GetText();
    }

    public sealed class StaticTooltipLine : ITooltipLine
    {
        readonly string _text;

        public StaticTooltipLine(string text)
        {
            _text = text ?? string.Empty;
        }

        public string GetText()
        {
            return _text;
        }

        public object GetDataContext()
        {
            return null;
        }

        public Action<object, object> GetOnClick()
        {
            return null;
        }

        public MySoundPair GetClickSound()
        {
            return AudioHelper.HudClick;
        }

        public CursorType? GetCursor()
        {
            return null;
        }

        public bool IsClickable => false;

        public override string ToString()
        {
            return GetText();
        }
    }

    public sealed class ClickableTooltipLine : ITooltipLine
    {
        readonly string _text;
        readonly object _dataContext;
        readonly Action<object, object> _onClick;

        public ClickableTooltipLine(string text, object dataContext, Action<object, object> onClick)
        {
            _text = text ?? string.Empty;
            _dataContext = dataContext;
            _onClick = onClick;
        }

        public MySoundPair ClickSound { get; set; } = AudioHelper.HudClick;

        public string GetText()
        {
            return _text;
        }

        public object GetDataContext()
        {
            return _dataContext ?? this;
        }

        public Action<object, object> GetOnClick()
        {
            return _onClick;
        }

        public MySoundPair GetClickSound()
        {
            return ClickSound;
        }

        public CursorType? GetCursor()
        {
            return IsClickable ? (CursorType?)CursorType.Hand : null;
        }

        public bool IsClickable => _onClick != null;

        public override string ToString()
        {
            return GetText();
        }
    }

    public enum TooltipActivationMode
    {
        Auto,
        Click,
        RightClick
    }

    public sealed class InteractiveTooltip
    {
        readonly Func<string> _titleGetter;
        readonly Func<string> _footerGetter;
        readonly Func<CursorType?> _getCursor;

        public InteractiveTooltip(
            Func<string> titleGetter,
            IList<ITooltipLine> lines,
            Func<string> footerGetter = null,
            Func<CursorType?> getCursor = null,
            TooltipActivationMode openMode = TooltipActivationMode.Auto,
            TooltipActivationMode closeMode = TooltipActivationMode.Auto)
        {
            _titleGetter = titleGetter;
            _footerGetter = footerGetter;
            _getCursor = getCursor;
            OpenMode = openMode;
            CloseMode = closeMode;
            Lines = lines != null ? new List<ITooltipLine>(lines) : new List<ITooltipLine>();
        }

        public InteractiveTooltip(
            string title,
            IList<ITooltipLine> lines,
            string footer = null,
            TooltipActivationMode openMode = TooltipActivationMode.Auto,
            TooltipActivationMode closeMode = TooltipActivationMode.Auto)
            : this(
                () => title ?? string.Empty,
                lines,
                footer != null ? (Func<string>)(() => footer) : null,
                null,
                openMode,
                closeMode)
        {
        }

        public List<ITooltipLine> Lines { get; private set; }

        public TooltipActivationMode OpenMode { get; private set; }

        public TooltipActivationMode CloseMode { get; private set; }

        public string GetTitle()
        {
            return _titleGetter != null ? (_titleGetter() ?? string.Empty) : string.Empty;
        }

        public CursorType GetCursor()
        {
            return _getCursor != null ? (_getCursor() ?? CursorType.Default) : CursorType.Default;
        }

        public string GetFooter()
        {
            return _footerGetter != null ? (_footerGetter() ?? string.Empty) : string.Empty;
        }
    }

    public abstract class InteractiveEntry
    {
        public bool Visible { get; private set; } = true;

        public void SetVisible(bool visible)
        {
            Visible = visible;
        }

        readonly List<InteractiveEntry> _children = new List<InteractiveEntry>();

        public IList<InteractiveEntry> Children => _children;

        public bool HasChildren => _children.Count > 0;

        public void ClearChildren()
        {
            _children.Clear();
        }

        public void AddChild(InteractiveEntry child)
        {
            if (child != null && !_children.Contains(child))
                _children.Add(child);
        }

        public void AddChildren(IEnumerable<InteractiveEntry> children)
        {
            if (children == null)
                return;

            foreach (var child in children)
                AddChild(child);
        }

        public virtual bool CanClick => Visible && OnClick != null;

        protected InteractiveEntry(CursorType? cursor = null, object dataContext = null, Action<object, object> onClick = null,
            InteractiveTooltip tooltip = null)
        {
            DataContext = dataContext;
            OnClick = onClick;
            Tooltip = tooltip;
            Cursor = cursor ?? (onClick != null ? CursorType.Hand : CursorType.Default);
        }

        public CursorType Cursor { get; private set; }

        public InteractiveEntry SetCursor(CursorType cursor)
        {
            Cursor = cursor;
            return this;
        }

        public object DataContext { get; private set; }

        public Action<object, object> OnClick { get; private set; }
        public Action<object, object> OnSecondaryClick { get; set; }

        public InteractiveTooltip Tooltip { get; private set; }

        public InteractiveEntry SetTooltip(InteractiveTooltip tooltip)
        {
            Tooltip = tooltip;
            return this;
        }

        public abstract RectangleF Bounds { get; }
        public MySoundPair ClickSound { get; set; } = AudioHelper.HudClick;
        public MySoundPair ClickFailSound { get; set; } = AudioHelper.HudUnable;

        public bool Hit(Vector2 point)
        {
            return Visible && HitCore(point);
        }

        protected abstract bool HitCore(Vector2 point);

        public virtual bool Click(object sender)=> HandleClick(sender, OnClick);
        
        public virtual bool SecondaryClick(object sender) => HandleClick(sender, OnSecondaryClick);

        internal bool HandleClick(object sender, Action<object, object> handler)
        {
            if (!Visible || handler == null)
                return false;

            handler(DataContext ?? this, sender);
            return true;
        }
    }

    public sealed class InteractiveCircleEntry : InteractiveEntry
    {
        public InteractiveCircleEntry(Vector2 center, float radius, CursorType? cursor = null, object dataContext = null,
            Action<object, object> onClick = null, InteractiveTooltip tooltip = null)
            : base(cursor, dataContext, onClick, tooltip)
        {
            Center = center;
            Radius = radius;
        }

        public Vector2 Center { get; private set; }
        public float Radius { get; private set; }

        public override RectangleF Bounds
        {
            get
            {
                var size = Radius * 2f;
                return new RectangleF(Center.X - Radius, Center.Y - Radius, size, size);
            }
        }

        protected override bool HitCore(Vector2 point)
        {
            if (Radius <= 0f)
                return false;

            return Vector2.DistanceSquared(point, Center) <= Radius * Radius;
        }
    }


    sealed class TooltipLineInteractiveEntry : InteractiveRectangleEntry
    {
        readonly ITooltipLine _line;

        public TooltipLineInteractiveEntry(RectangleF rect, ITooltipLine line, CursorType cursor)
            : base(rect, cursor, line)
        {
            _line = line;
        }

        public override bool CanClick => Visible && _line != null && _line.GetOnClick() != null;

        public override bool Click(object sender)
        {
            if (!Visible || _line == null)
                return false;

            var onClick = _line.GetOnClick();
            if (onClick == null)
                return false;

            onClick(_line.GetDataContext(), sender);
            return true;
        }
    }

    public class InteractiveRectangleEntry : InteractiveEntry
    {
        public InteractiveRectangleEntry(RectangleF bounds, CursorType? cursor = null, object dataContext = null,
            Action<object, object> onClick = null, InteractiveTooltip tooltip = null)
            : base(cursor, dataContext, onClick, tooltip)
        {
            Rect = bounds;
        }

        public RectangleF Rect { get; private set; }

        public void SetRect(RectangleF bounds)
        {
            Rect = bounds;
        }

        public override RectangleF Bounds => Rect;
        public object RightClick { get; set; }

        protected override bool HitCore(Vector2 point)
        {
            return Rect.Contains(point);
        }
    }

}
