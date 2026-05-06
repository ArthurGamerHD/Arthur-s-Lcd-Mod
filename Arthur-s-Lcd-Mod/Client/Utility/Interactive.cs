using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.Controls;
using LcdMod.Client.Helpers;
using Sandbox.Game.Entities;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Utility
{
    public sealed class InteractiveRenderContext
    {
        public InteractiveRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            Color textColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            Surface = surface;
            Scale = scale;
            FontScale = fontScale;
            TextColor = textColor;
            PanelColor = panelColor;
            CursorPosition = cursorPosition;
        }

        public Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; private set; }
        public float Scale { get; private set; }
        public float FontScale { get; private set; }
        public Color TextColor { get; private set; }
        public Color PanelColor { get; private set; }
        public Vector2 CursorPosition { get; private set; }
    }

    public delegate void InteractiveRenderHandler(
        InteractiveEntry entry,
        InteractiveRenderContext context,
        List<MySprite> sprites);

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
        readonly Func<IList<ITooltipLine>> _linesGetter;
        readonly List<ITooltipLine> _staticLines;
        readonly Func<string> _footerGetter;
        readonly Func<CursorType?> _getCursor;
        readonly Func<string> _iconTextureGetter;
        readonly List<InteractiveEntry> _interactiveEntries = new List<InteractiveEntry>();
        readonly Dictionary<ITooltipLine, TooltipLineInteractiveEntry> _lineEntryByLine =
            new Dictionary<ITooltipLine, TooltipLineInteractiveEntry>();
        readonly HashSet<ITooltipLine> _linesUsedThisFrame = new HashSet<ITooltipLine>();

        InteractiveRectangleEntry _cardEntry;

        public InteractiveTooltip(
            Func<string> titleGetter,
            IList<ITooltipLine> lines,
            Func<string> footerGetter = null,
            Func<CursorType?> getCursor = null,
            TooltipActivationMode openMode = TooltipActivationMode.Auto,
            TooltipActivationMode closeMode = TooltipActivationMode.Auto,
            Func<string> iconGetter = null)
            : this(
                titleGetter,
                lines != null ? (Func<IList<ITooltipLine>>)(() => lines) : null,
                footerGetter,
                getCursor,
                openMode,
                closeMode,
                iconGetter)
        {
        }

        public InteractiveTooltip(
            Func<string> titleGetter,
            Func<IList<ITooltipLine>> linesGetter,
            Func<string> footerGetter = null,
            Func<CursorType?> getCursor = null,
            TooltipActivationMode openMode = TooltipActivationMode.Auto,
            TooltipActivationMode closeMode = TooltipActivationMode.Auto,
            Func<string> iconGetter = null)
        {
            _titleGetter = titleGetter;
            _linesGetter = linesGetter;
            _staticLines = null;
            _footerGetter = footerGetter;
            _getCursor = getCursor;
            _iconTextureGetter = iconGetter;
            OpenMode = openMode;
            CloseMode = closeMode;
        }

        public InteractiveTooltip(
            string title,
            IList<ITooltipLine> lines,
            string footer = null,
            TooltipActivationMode openMode = TooltipActivationMode.Auto,
            TooltipActivationMode closeMode = TooltipActivationMode.Auto,
            string iconTexture = null)
        {
            _titleGetter = () => title ?? string.Empty;
            _staticLines = lines != null ? new List<ITooltipLine>(lines) : new List<ITooltipLine>();
            _linesGetter = null;
            _footerGetter = footer != null ? (Func<string>)(() => footer) : null;
            _getCursor = null;
            _iconTextureGetter = iconTexture != null ? (Func<string>)(() => iconTexture) : null;
            OpenMode = openMode;
            CloseMode = closeMode;
        }

        public List<ITooltipLine> Lines
        {
            get
            {
                if (_linesGetter == null)
                    return _staticLines != null ? new List<ITooltipLine>(_staticLines) : new List<ITooltipLine>();

                var lines = _linesGetter();
                return lines != null ? new List<ITooltipLine>(lines) : new List<ITooltipLine>();
            }
        }

        public TooltipActivationMode OpenMode { get; private set; }

        public TooltipActivationMode CloseMode { get; private set; }

        public RectangleF Bounds { get; private set; }

        public RectangleF KeepOpenBounds { get; private set; }

        public bool HasBounds { get; private set; }

        public IList<InteractiveEntry> InteractiveEntries => _interactiveEntries;

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

        public string GetIconTexture()
        {
            return _iconTextureGetter != null ? (_iconTextureGetter() ?? string.Empty) : string.Empty;
        }

        public void Hide()
        {
            if (_cardEntry != null)
                _cardEntry.SetVisible(false);

            foreach (var kv in _lineEntryByLine)
            {
                if (kv.Value != null)
                    kv.Value.SetVisible(false);
            }

            HasBounds = false;
            Bounds = default(RectangleF);
            KeepOpenBounds = default(RectangleF);
            _interactiveEntries.Clear();
            _linesUsedThisFrame.Clear();

            // Entries remain attached to their parent. Visibility gating keeps hidden
            // tooltip entries out of hit testing and cursor resolution.
        }

        public List<MySprite> Render(
            InteractiveEntry parentEntry,
            RectangleF viewBox,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            Color textColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            var sprites = new List<MySprite>();
            _interactiveEntries.Clear();
            _linesUsedThisFrame.Clear();

            var tooltipLines = Lines;
            var title = GetTitle();
            var footer = GetFooter();
            var iconTexture = GetIconTexture();
            var cursor = GetCursor();

            int lineCount = tooltipLines.Count;
            float lineScale = 0.52f * scale * fontScale;

            var lineTexts = new string[lineCount];
            var clickables = new bool[lineCount];
            var lineCursors = new CursorType?[lineCount];
            var lineSizes = new Vector2[lineCount];

            float maxLineWidth = 0f;

            for (int i = 0; i < lineCount; i++)
            {
                var line = tooltipLines[i];

                lineTexts[i] = line != null ? line.GetText() : string.Empty;
                clickables[i] = line != null && line.IsClickable;
                lineCursors[i] = line?.GetCursor();
                lineSizes[i] = FormatingHelper.GetSizeInPixel(lineTexts[i], "White", lineScale, surface);

                if (lineSizes[i].X > maxLineWidth)
                    maxLineWidth = lineSizes[i].X;
            }

            const float spacing = 6f;
            Vector2 padding = new Vector2(8f, 4f) * scale;
            float offset = 16f * scale;

            float titleScale = 0.72f * scale * fontScale;
            float footerScale = 0.62f * scale * fontScale;

            var titleSize = FormatingHelper.GetSizeInPixel(title, "White", titleScale, surface);
            var footerSize = string.IsNullOrEmpty(footer)
                ? Vector2.Zero
                : FormatingHelper.GetSizeInPixel(footer, "White", footerScale, surface);

            float lineStep = FormatingHelper.GetSizeInPixel("Ag", "White", lineScale, surface).Y + 2f;

            bool hasIcon = !string.IsNullOrEmpty(iconTexture);

            float linesHeight = tooltipLines.Count * lineStep;
            float titleFooterWidth = Math.Max(titleSize.X, footerSize.X);

            float iconSize = hasIcon
                ? Math.Max(24f * scale, Math.Min(52f * scale, Math.Max(linesHeight, 24f * scale)))
                : 0f;

            float iconGap = hasIcon ? 8f * scale : 0f;

            // Body is only icon + lines. Title/footer are centered over the whole card.
            float bodyWidth = maxLineWidth + iconSize + iconGap;
            float contentWidth = Math.Max(titleFooterWidth, bodyWidth);

            float contentHeight = titleSize.Y + spacing + Math.Max(linesHeight, iconSize);
            if (!string.IsNullOrEmpty(footer))
                contentHeight += spacing + footerSize.Y;

            float cardWidth = Math.Max(20f * scale, contentWidth + 2f * padding.X);
            float cardHeight = Math.Max(20f * scale, contentHeight + 2f * padding.Y);

            var parentBounds = parentEntry.Bounds;

            bool placeOnRight = parentBounds.Center.X <= viewBox.Center.X;
            float anchorX = placeOnRight
                ? parentBounds.Right + offset
                : parentBounds.X - offset - cardWidth;

            float startX = MathHelper.Clamp(
                anchorX,
                viewBox.X + padding.X,
                viewBox.Right - cardWidth - padding.X);

            float startY = MathHelper.Clamp(
                parentBounds.Center.Y - cardHeight * 0.5f,
                viewBox.Y + padding.Y,
                viewBox.Bottom - cardHeight - padding.Y);

            var cardRect = new RectangleF(startX, startY, cardWidth, cardHeight);
            var shadowRect = new RectangleF(cardRect.Position + 2f, cardRect.Size);
            var shadowColor = panelColor.MulValue(0.2f);

            Bounds = cardRect;
            KeepOpenBounds = new RectangleF(
                Math.Min(parentBounds.X, cardRect.X),
                parentBounds.Y,
                Math.Max(parentBounds.Right, cardRect.Right) - Math.Min(parentBounds.X, cardRect.X),
                parentBounds.Height);
            HasBounds = true;

            RectanglePanel.CreateSpritesFromRect(shadowRect, sprites, shadowColor, 0.2f);
            RectanglePanel.CreateSpritesFromRect(cardRect, sprites, panelColor, 0.2f);

            if (_cardEntry == null)
            {
                _cardEntry = new InteractiveRectangleEntry(
                    cardRect,
                    cursor,
                    parentEntry.DataContext);
            }
            else if (!Equals(_cardEntry.DataContext, parentEntry.DataContext))
            {
                _cardEntry.SetVisible(false);
                _cardEntry = new InteractiveRectangleEntry(
                    cardRect,
                    cursor,
                    parentEntry.DataContext);
            }
            else
            {
                _cardEntry.SetRect(cardRect);
                _cardEntry.SetCursor(cursor);
            }

            _cardEntry.SetVisible(true);
            _interactiveEntries.Add(_cardEntry);

            float currentY = cardRect.Y + padding.Y;

            float contentLeftX = cardRect.X + padding.X;
            float contentCenterX = contentLeftX + contentWidth * 0.5f;

            float bodyLeftX = contentLeftX + Math.Max(0f, (contentWidth - bodyWidth) * 0.5f);
            float iconLeftX = bodyLeftX;
            float leftX = bodyLeftX + iconSize + iconGap;

            var titleSprite = new MySprite
            {
                Type = SpriteType.TEXT,
                Data = title,
                Position = new Vector2(contentCenterX, currentY),
                Color = textColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = titleScale
            };

            sprites.Add(titleSprite.Shadow(2 * titleScale, shadowColor));
            sprites.Add(titleSprite);

            currentY += titleSize.Y + spacing;

            float bodyTopY = currentY;
            float bodyHeight = Math.Max(linesHeight, iconSize);

            if (hasIcon)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = iconTexture,
                    Position = new Vector2(
                        iconLeftX + iconSize * 0.5f,
                        bodyTopY + bodyHeight * 0.5f),
                    Size = new Vector2(iconSize),
                    Color = textColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            // Vertically center the lines against the icon/body area.
            currentY = bodyTopY + Math.Max(0f, (bodyHeight - linesHeight) * 0.5f);

            for (int i = 0; i < tooltipLines.Count; i++)
            {
                var line = tooltipLines[i];

                var textPosition = new Vector2(
                    leftX,
                    currentY - lineSizes[i].Y * 0.25f * lineScale);

                var lineBounds = new RectangleF(
                    leftX,
                    textPosition.Y,
                    Math.Max(lineSizes[i].X, 1f),
                    Math.Max(lineSizes[i].Y, lineStep));

                bool hasLineCursor = lineCursors[i].HasValue;
                bool hasLineEntry = line != null && (clickables[i] || hasLineCursor);

                bool lineHovered = hasLineEntry && lineBounds.Contains(cursorPosition);
                var lineColor = lineHovered
                    ? panelColor.DeriveTextAccentColor()
                    : textColor;

                if (hasLineEntry)
                {
                    TooltipLineInteractiveEntry lineEntry;
                    var resolvedCursor = lineCursors[i] ?? (clickables[i] ? CursorType.Hand : CursorType.Default);

                    if (!_lineEntryByLine.TryGetValue(line, out lineEntry) || lineEntry == null)
                    {
                        lineEntry = new TooltipLineInteractiveEntry(lineBounds, line, resolvedCursor);
                        _lineEntryByLine[line] = lineEntry;
                    }
                    else
                    {
                        lineEntry.SetRect(lineBounds);
                        lineEntry.SetCursor(resolvedCursor);
                    }

                    lineEntry.SetVisible(true);
                    lineEntry.ClickSound = line.GetClickSound();

                    _linesUsedThisFrame.Add(line);
                    _interactiveEntries.Add(lineEntry);
                }

                var position = new Vector2(leftX, currentY - lineSizes[i].Y * 0.25f * lineScale);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = lineTexts[i],
                    Position = position,
                    Color = lineColor,
                    FontId = "White",
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = lineScale
                });

                if (clickables[i])
                {
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(position.X, position.Y + lineSizes[i].Y),
                        Size = new Vector2(Math.Max(1f, lineSizes[i].X), Math.Max(1f, scale)),
                        Color = new Color(lineColor, .3f),
                        Alignment = TextAlignment.LEFT
                    });
                }

                currentY += lineStep;
            }

            currentY = bodyTopY + bodyHeight;

            if (!string.IsNullOrEmpty(footer))
            {
                currentY += spacing;

                var footerSprite = new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = footer,
                    Position = new Vector2(contentCenterX, currentY),
                    Color = textColor,
                    FontId = "White",
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = footerScale
                };

                sprites.Add(footerSprite.Shadow(2 * footerScale, shadowColor));
                sprites.Add(footerSprite);
            }

            PruneUnusedLineEntries();
            return sprites;
        }

        void PruneUnusedLineEntries()
        {
            if (_lineEntryByLine.Count == 0)
                return;

            var remove = new List<ITooltipLine>();

            foreach (var kv in _lineEntryByLine)
            {
                if (!_linesUsedThisFrame.Contains(kv.Key))
                {
                    if (kv.Value != null)
                        kv.Value.SetVisible(false);

                    remove.Add(kv.Key);
                }
            }

            for (int i = 0; i < remove.Count; i++)
                _lineEntryByLine.Remove(remove[i]);
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

        public InteractiveEntry SetDataContext(object dataContext)
        {
            DataContext = dataContext;
            return this;
        }

        public Action<object, object> OnClick { get; private set; }
        public Action<object, object> OnSecondaryClick { get; set; }

        public InteractiveEntry SetOnClick(Action<object, object> onClick)
        {
            OnClick = onClick;
            return this;
        }

        public InteractiveTooltip Tooltip { get; private set; }

        public InteractiveEntry SetTooltip(InteractiveTooltip tooltip)
        {
            Tooltip = tooltip;
            return this;
        }

        public InteractiveRenderHandler CustomRender { get; set; }

        public abstract RectangleF Bounds { get; }
        public MySoundPair ClickSound { get; set; } = AudioHelper.HudClick;
        public MySoundPair ClickFailSound { get; set; } = AudioHelper.HudUnable;

        public void Render(InteractiveRenderContext context, List<MySprite> sprites)
        {
            if (context == null || sprites == null)
                return;

            if (CustomRender != null)
            {
                CustomRender(this, context, sprites);
                return;
            }

            RenderDefault(context, sprites);
        }

        protected virtual void RenderDefault(InteractiveRenderContext context, List<MySprite> sprites)
        {
            var rect = Bounds;
            var fillColor = rect.Contains(context.CursorPosition)
                ? context.PanelColor.DeriveAccentColor()
                : context.PanelColor;

            RectanglePanel.CreateSpritesFromRect(rect, sprites, fillColor, 0.2f);
            RenderDefaultText(rect, context, sprites);
        }

        protected void RenderDefaultText(RectangleF rect, InteractiveRenderContext context, List<MySprite> sprites)
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
                Color = context.TextColor,
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

        protected override void RenderDefault(InteractiveRenderContext context, List<MySprite> sprites)
        {
            var fillColor = Hit(context.CursorPosition)
                ? context.PanelColor.DeriveAccentColor()
                : context.PanelColor;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = Center,
                Size = new Vector2(Radius * 2f),
                Color = fillColor,
                Alignment = TextAlignment.CENTER
            });

            RenderDefaultText(Bounds, context, sprites);
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
