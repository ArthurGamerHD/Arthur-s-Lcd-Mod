using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.Tooltip
{
    public enum TooltipActivationMode
    {
        Auto,
        Click,
        RightClick
    }

    public enum TooltipPlacement
    {
        Auto,
        Above,
        Below,
        Left,
        Right
    }
    
    public sealed class InteractiveTooltip
    {
        readonly Func<string> _titleGetter;
        readonly Func<IList<ITooltipLine>> _linesGetter;
        readonly List<ITooltipLine> _staticLines;
        readonly Func<string> _footerGetter;
        readonly Func<CursorType?> _getCursor;
        readonly Func<string> _iconTextureGetter;
        readonly Func<ControlTemplate> _bodyGetter;
        readonly List<Control> _interactiveEntries = new List<Control>();
        readonly Dictionary<ITooltipLine, TooltipLineControl> _lineEntryByLine =
            new Dictionary<ITooltipLine, TooltipLineControl>();
        readonly HashSet<ITooltipLine> _linesUsedThisFrame = new HashSet<ITooltipLine>();

        TooltipContainerControl _containerControl;
        RectangleControl _cardControl;
        ControlTemplate _bodyControl;

        public InteractiveTooltip(
            Func<string> titleGetter,
            IList<ITooltipLine> lines,
            Func<string> footerGetter = null,
            Func<CursorType?> getCursor = null,
            TooltipActivationMode openMode = TooltipActivationMode.Auto,
            TooltipActivationMode closeMode = TooltipActivationMode.Auto,
            Func<string> iconGetter = null,
            Func<ControlTemplate> bodyGetter = null)
            : this(
                titleGetter,
                lines != null ? (Func<IList<ITooltipLine>>)(() => lines) : null,
                footerGetter,
                getCursor,
                openMode,
                closeMode,
                iconGetter,
                bodyGetter)
        {
        }

        public InteractiveTooltip(
            Func<string> titleGetter,
            Func<IList<ITooltipLine>> linesGetter,
            Func<string> footerGetter = null,
            Func<CursorType?> getCursor = null,
            TooltipActivationMode openMode = TooltipActivationMode.Auto,
            TooltipActivationMode closeMode = TooltipActivationMode.Auto,
            Func<string> iconGetter = null,
            Func<ControlTemplate> bodyGetter = null)
        {
            _titleGetter = titleGetter;
            _linesGetter = linesGetter;
            _staticLines = null;
            _footerGetter = footerGetter;
            _getCursor = getCursor;
            _iconTextureGetter = iconGetter;
            _bodyGetter = bodyGetter;
            OpenMode = openMode;
            CloseMode = closeMode;
        }

        public InteractiveTooltip(
            string title,
            IList<ITooltipLine> lines,
            string footer = null,
            TooltipActivationMode openMode = TooltipActivationMode.Auto,
            TooltipActivationMode closeMode = TooltipActivationMode.Auto,
            string iconTexture = null,
            Func<ControlTemplate> bodyGetter = null)
        {
            _titleGetter = () => title ?? string.Empty;
            _staticLines = lines != null ? new List<ITooltipLine>(lines) : new List<ITooltipLine>();
            _linesGetter = null;
            _footerGetter = footer != null ? (Func<string>)(() => footer) : null;
            _getCursor = null;
            _iconTextureGetter = iconTexture != null ? (Func<string>)(() => iconTexture) : null;
            _bodyGetter = bodyGetter;
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

        public TooltipPlacement Placement { get; set; } = TooltipPlacement.Auto;

        public Func<ControlTemplate, RectangleF> AnchorBoundsGetter { get; set; }

        public RectangleF Bounds { get; private set; }

        public RectangleF KeepOpenBounds { get; private set; }

        public bool HasBounds { get; private set; }

        public IList<Control> InteractiveEntries => _interactiveEntries;

        public ControlTemplate TooltipContainer => _containerControl;

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

        public ControlTemplate GetBodyControl()
        {
            return _bodyGetter != null ? _bodyGetter() : null;
        }

        public void Hide()
        {
            if (_containerControl != null)
            {
                _containerControl.ClearChildren();
                _containerControl.SetVisible(false);
            }

            if (_cardControl != null)
                _cardControl.SetVisible(false);

            if (_bodyControl != null)
                _bodyControl.SetVisible(false);

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
            ControlTemplate parentEntry,
            IApp parentApp,
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
            var styleScope = parentApp as IVisualStyleScope;
            Color resolvedColor;
            if (styleScope != null && ScopedResourceResolver.TryResolve(styleScope, ThemeResources.OnSurfaceColor, out resolvedColor))
                textColor = resolvedColor;
            if (styleScope != null && ScopedResourceResolver.TryResolve(styleScope, ThemeResources.SurfaceContainerHighColor, out resolvedColor))
                panelColor = resolvedColor;

            var tooltipLines = Lines;
            var title = GetTitle();
            var footer = GetFooter();
            var iconTexture = GetIconTexture();
            var cursor = GetCursor();
            var bodyControl = GetBodyControl();

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
                lineSizes[i] = FormatingHelper.GetSizeInPixel(lineTexts[i], parentEntry, lineScale, surface);

                if (lineSizes[i].X > maxLineWidth)
                    maxLineWidth = lineSizes[i].X;
            }

            const float spacing = 6f;
            Vector2 padding = new Vector2(8f, 4f) * scale;
            float offset = 16f * scale;

            float titleScale = 0.72f * scale * fontScale;
            float footerScale = 0.62f * scale * fontScale;

            var titleSize = FormatingHelper.GetSizeInPixel(title, parentEntry, titleScale, surface);
            var footerSize = string.IsNullOrEmpty(footer)
                ? Vector2.Zero
                : FormatingHelper.GetSizeInPixel(footer, parentEntry, footerScale, surface);

            float lineStep = FormatingHelper.LineHeight(lineScale, parentEntry, surface) + 2f;

            bool hasIcon = !string.IsNullOrEmpty(iconTexture);

            float linesHeight = tooltipLines.Count * lineStep;
            float titleFooterWidth = Math.Max(titleSize.X, footerSize.X);
            Vector2 bodySize = Vector2.Zero;
            if (bodyControl != null)
            {
                float maxBodyWidth = Math.Max(80f * scale, Math.Min(420f * scale, viewBox.Width - 4f * padding.X));
                float maxBodyHeight = Math.Max(40f * scale, viewBox.Height - titleSize.Y - footerSize.Y - 4f * spacing - 2f * padding.Y);
                bodySize = bodyControl.Measure(new Vector2(maxBodyWidth, maxBodyHeight));
                if (bodySize.X <= 0f || bodySize.Y <= 0f)
                    bodyControl = null;
            }

            float iconSize = hasIcon
                ? Math.Max(24f * scale, Math.Min(52f * scale, Math.Max(bodyControl != null ? bodySize.Y : linesHeight, 24f * scale)))
                : 0f;

            float iconGap = hasIcon ? 8f * scale : 0f;

            // Body is only icon + lines. Title/footer are centered over the whole card.
            float textBodyWidth = bodyControl != null ? bodySize.X : maxLineWidth;
            float bodyWidth = textBodyWidth + iconSize + iconGap;
            float contentWidth = Math.Max(titleFooterWidth, bodyWidth);

            float textBodyHeight = bodyControl != null ? bodySize.Y : linesHeight;
            float contentHeight = titleSize.Y + spacing + Math.Max(textBodyHeight, iconSize);
            if (!string.IsNullOrEmpty(footer))
                contentHeight += spacing + footerSize.Y;

            float cardWidth = Math.Max(20f * scale, contentWidth + 2f * padding.X);
            float cardHeight = Math.Max(20f * scale, contentHeight + 2f * padding.Y);

            var parentBounds = AnchorBoundsGetter != null
                ? AnchorBoundsGetter(parentEntry)
                : parentEntry.Bounds;

            TooltipPlacement resolvedPlacement = Placement;
            if (resolvedPlacement == TooltipPlacement.Auto)
            {
                resolvedPlacement = parentBounds.Center.X <= viewBox.Center.X
                    ? TooltipPlacement.Right
                    : TooltipPlacement.Left;
            }

            float minimumX = viewBox.X + padding.X;
            float maximumX = viewBox.Right - cardWidth - padding.X;
            float minimumY = viewBox.Y + padding.Y;
            float maximumY = viewBox.Bottom - cardHeight - padding.Y;
            float startX;
            float startY;

            switch (resolvedPlacement)
            {
                case TooltipPlacement.Above:
                    startX = ClampCardCoordinate(
                        parentBounds.Center.X - cardWidth * 0.5f,
                        minimumX,
                        maximumX);
                    startY = ClampCardCoordinate(
                        parentBounds.Y - offset - cardHeight,
                        minimumY,
                        maximumY);
                    break;
                case TooltipPlacement.Below:
                    startX = ClampCardCoordinate(
                        parentBounds.Center.X - cardWidth * 0.5f,
                        minimumX,
                        maximumX);
                    startY = ClampCardCoordinate(
                        parentBounds.Bottom + offset,
                        minimumY,
                        maximumY);
                    break;
                case TooltipPlacement.Left:
                    startX = ClampCardCoordinate(
                        parentBounds.X - offset - cardWidth,
                        minimumX,
                        maximumX);
                    startY = ClampCardCoordinate(
                        parentBounds.Center.Y - cardHeight * 0.5f,
                        minimumY,
                        maximumY);
                    break;
                default:
                    startX = ClampCardCoordinate(
                        parentBounds.Right + offset,
                        minimumX,
                        maximumX);
                    startY = ClampCardCoordinate(
                        parentBounds.Center.Y - cardHeight * 0.5f,
                        minimumY,
                        maximumY);
                    break;
            }

            var cardRect = new RectangleF(startX, startY, cardWidth, cardHeight);
            var shadowRect = new RectangleF(cardRect.Position + 2f, cardRect.Size);
            var shadowColor = panelColor.MulValue(0.2f);

            Bounds = cardRect;
            KeepOpenBounds = BuildKeepOpenBounds(cardRect, parentBounds, resolvedPlacement);
            HasBounds = true;

            var containerRect = CloseMode == TooltipActivationMode.Auto
                ? Union(cardRect, KeepOpenBounds)
                : viewBox;
            EnsureContainer(containerRect, parentApp);
            _containerControl.ClearChildren();
            _interactiveEntries.Add(_containerControl);

            BorderRenderer.CreateSpritesFromRect(shadowRect, sprites, shadowColor,
                radiusScale: scale);
            BorderRenderer.CreateSpritesFromRect(cardRect, sprites, panelColor,
                radiusScale: scale);

            if (_cardControl == null)
            {
                _cardControl = new RectangleControl(
                    cardRect,
                    cursor,
                    parentEntry.DataContext);
            }
            else if (!Equals(_cardControl.DataContext, parentEntry.DataContext))
            {
                _cardControl.SetVisible(false);
                _cardControl = new RectangleControl(
                    cardRect,
                    cursor,
                    parentEntry.DataContext);
            }
            else
            {
                _cardControl.SetRect(cardRect);
                _cardControl.SetCursor(cursor);
            }

            _cardControl.SetVisible(true);
            _containerControl.AddChild(_cardControl);

            float currentY = cardRect.Y + padding.Y;

            float contentLeftX = cardRect.X + padding.X;
            float contentCenterX = contentLeftX + contentWidth * 0.5f;

            float bodyLeftX = contentLeftX;
            float iconLeftX = bodyLeftX;
            float leftX = bodyLeftX + iconSize + iconGap;

            var titleSprite = new MySprite
            {
                Type = SpriteType.TEXT,
                Data = title,
                Position = new Vector2(contentCenterX, currentY),
                Color = textColor,
                FontId = parentEntry.TextFont,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = titleScale
            };

            sprites.Add(titleSprite.Shadow(2 * titleScale, shadowColor));
            sprites.Add(titleSprite);

            currentY += titleSize.Y + spacing;

            float bodyTopY = currentY;
            float bodyHeight = Math.Max(textBodyHeight, iconSize);

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

            if (bodyControl != null)
            {
                if (_bodyControl != null && !ReferenceEquals(_bodyControl, bodyControl))
                    _bodyControl.SetVisible(false);

                _bodyControl = bodyControl;
                var bodyRect = new RectangleF(
                    leftX,
                    bodyTopY + Math.Max(0f, (bodyHeight - bodySize.Y) * 0.5f),
                    bodySize.X,
                    bodySize.Y);
                bodyControl.Arrange(bodyRect);
                bodyControl.SetVisible(true);
                _containerControl.AddChild(bodyControl);
                bodyControl.Render(sprites);
            }
            else
            {
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
                    var lineColor = textColor;
                    if (lineHovered)
                    {
                        if (!ScopedResourceResolver.TryResolve(styleScope, ThemeResources.AccentColor, out lineColor))
                            throw new ResourceKeyNotFoundException(ThemeResources.AccentColor.Name, "ResourceTree");
                    }

                    if (hasLineEntry)
                    {
                        TooltipLineControl lineEntry;
                        var resolvedCursor = lineCursors[i] ?? (clickables[i] ? CursorType.Hand : CursorType.Default);

                        if (!_lineEntryByLine.TryGetValue(line, out lineEntry) || lineEntry == null)
                        {
                            lineEntry = new TooltipLineControl(lineBounds, line, resolvedCursor);
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
                        _containerControl.AddChild(lineEntry);
                    }

                    var position = new Vector2(leftX, currentY - lineSizes[i].Y * 0.25f * lineScale);

                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXT,
                        Data = lineTexts[i],
                        Position = position,
                        Color = lineColor,
                        FontId = parentEntry.TextFont,
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
                    FontId = parentEntry.TextFont,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = footerScale
                };

                sprites.Add(footerSprite.Shadow(2 * footerScale, shadowColor));
                sprites.Add(footerSprite);
            }

            PruneUnusedLineEntries();
            return sprites;
        }


        static float ClampCardCoordinate(float value, float minimum, float maximum)
        {
            if (maximum < minimum)
                return minimum;

            return MathHelper.Clamp(value, minimum, maximum);
        }

        static RectangleF BuildKeepOpenBounds(
            RectangleF cardBounds,
            RectangleF parentBounds,
            TooltipPlacement placement)
        {
            switch (placement)
            {
                case TooltipPlacement.Above:
                {
                    float left = Math.Min(parentBounds.X, cardBounds.X);
                    float right = Math.Max(parentBounds.Right, cardBounds.Right);
                    float top = Math.Min(cardBounds.Bottom, parentBounds.Y);
                    float bottom = Math.Max(cardBounds.Bottom, parentBounds.Y);
                    return new RectangleF(left, top, right - left, bottom - top);
                }
                case TooltipPlacement.Below:
                {
                    float left = Math.Min(parentBounds.X, cardBounds.X);
                    float right = Math.Max(parentBounds.Right, cardBounds.Right);
                    float top = Math.Min(parentBounds.Bottom, cardBounds.Y);
                    float bottom = Math.Max(parentBounds.Bottom, cardBounds.Y);
                    return new RectangleF(left, top, right - left, bottom - top);
                }
                case TooltipPlacement.Left:
                {
                    float left = Math.Min(cardBounds.Right, parentBounds.X);
                    float right = Math.Max(cardBounds.Right, parentBounds.X);
                    float top = Math.Min(parentBounds.Y, cardBounds.Y);
                    float bottom = Math.Max(parentBounds.Bottom, cardBounds.Bottom);
                    return new RectangleF(left, top, right - left, bottom - top);
                }
                default:
                {
                    float left = Math.Min(parentBounds.Right, cardBounds.X);
                    float right = Math.Max(parentBounds.Right, cardBounds.X);
                    float top = Math.Min(parentBounds.Y, cardBounds.Y);
                    float bottom = Math.Max(parentBounds.Bottom, cardBounds.Bottom);
                    return new RectangleF(left, top, right - left, bottom - top);
                }
            }
        }

        void EnsureContainer(RectangleF bounds, IApp parentApp)
        {
            if (_containerControl == null)
            {
                _containerControl = new TooltipContainerControl(bounds, parentApp);
            }
            else
            {
                _containerControl.SetRect(bounds);
            }

            _containerControl.SetDataContext(parentApp);
            _containerControl.SetVisible(true);
        }

        static RectangleF Union(RectangleF first, RectangleF second)
        {
            float x = Math.Min(first.X, second.X);
            float y = Math.Min(first.Y, second.Y);
            float right = Math.Max(first.Right, second.Right);
            float bottom = Math.Max(first.Bottom, second.Bottom);
            return new RectangleF(x, y, Math.Max(0f, right - x), Math.Max(0f, bottom - y));
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

    sealed class TooltipContainerControl : RectangleControl
    {
        public TooltipContainerControl(RectangleF rect, IApp parentApp)
            : base(rect, CursorType.Default, parentApp)
        {
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
        }
    }

    public class InteractiveCustomEntry : ControlTemplate
    {
        readonly Func<Vector2, bool> _hitGetter;
        readonly Func<RectangleF> _boundsGetter;
        RectangleF _bounds;

        public InteractiveCustomEntry(RectangleF bounds, Func<Vector2, bool> hitGetter,
            CursorType? cursor = null, object dataContext = null,
            Action<object, object> onClick = null, InteractiveTooltip tooltip = null)
            : base(cursor, dataContext, onClick, tooltip)
        {
            _bounds = bounds;
            _hitGetter = hitGetter;
        }

        public InteractiveCustomEntry(Func<RectangleF> boundsGetter, Func<Vector2, bool> hitGetter,
            CursorType? cursor = null, object dataContext = null,
            Action<object, object> onClick = null, InteractiveTooltip tooltip = null)
            : base(cursor, dataContext, onClick, tooltip)
        {
            _boundsGetter = boundsGetter;
            _hitGetter = hitGetter;
        }

        public override RectangleF Bounds => _boundsGetter?.Invoke() ?? _bounds;

        public void SetBounds(RectangleF bounds)
        {
            _bounds = bounds;
        }

        protected override bool HitCore(Vector2 point)
        {
            return _hitGetter != null && _hitGetter(point);
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var rect = Bounds;
            var fillColor = Hit(new Vector2(float.NaN, float.NaN))
                ? ResolveColor(ThemeResources.SurfaceContainerColor)
                : ResolveColor(ThemeResources.SurfaceColor);

            BorderRenderer.CreateSpritesFromRect(rect, sprites, fillColor,
                radiusScale: LayoutScale);
            RenderDefaultText(rect, sprites);
        }
    }

    public sealed class InteractiveCircleEntry : ControlTemplate
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

        public void SetCircle(Vector2 center, float radius)
        {
            if (Center == center && Math.Abs(Radius - radius) < 0.01)
                return;

            Center = center;
            Radius = radius;
            MarkDirty();
        }

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

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var fillColor = Hit(new Vector2(float.NaN, float.NaN))
                ? ResolveColor(ThemeResources.SurfaceContainerColor)
                : ResolveColor(ThemeResources.SurfaceColor);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = Center,
                Size = new Vector2(Radius * 2f),
                Color = fillColor,
                Alignment = TextAlignment.CENTER
            });

            RenderDefaultText(Bounds, sprites);
        }
    }
}
