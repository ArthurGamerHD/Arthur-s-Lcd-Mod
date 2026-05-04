using System;
using System.Collections.Generic;
using System.Linq;
using Generated;
using Graph.Apps.Utility;
using Graph.Extensions;
using Graph.Helpers;
using Graph.Panels;
using Graph.System.Config.Models;
using Graph.System.Controls;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.Entities.Blocks;
using VRage.Game.Entity;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace Graph.Apps.Abstract
{
    public abstract partial class InteractiveSurfaceScript : SurfaceScriptBase, IEyeTracking
    {
        const long CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES = 6;
        object _activeTooltipParentObject;
        RectangleF _tooltipRect;
        RectangleF _tooltipKeepOpenRect;
        bool _hasTooltipBounds;
        long _lastVisualContactFrame = long.MinValue;

        protected override ConfigKind ConfigKind => ConfigKind.Interactive;

        readonly List<MySprite> _tooltipLayerSprites = new List<MySprite>();
        readonly List<InteractiveEntry> _tooltipLayerEntries = new List<InteractiveEntry>();

        protected InteractiveSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public Vector2 CursorPosition { get; protected set; }

        RectangleF _baseViewBox;

        readonly List<InteractiveEntry> _interactiveEntriesWithOverlay = new List<InteractiveEntry>();

        public ICollection<InteractiveEntry> InteractiveEntries
        {
            get
            {
                _interactiveEntriesWithOverlay.Clear();

                if (_messageBox != null)
                {
                    if (_messageBox.Dismissed)
                    {
                        _messageBox = null;
                    }
                    else
                    {
                        _messageBox.AddInteractiveEntries(_interactiveEntriesWithOverlay);
                        return _interactiveEntriesWithOverlay;
                    }
                }

                _interactiveEntriesWithOverlay.AddRange(InteractiveList);
                _globalMenu?.AddInteractiveEntries(_interactiveEntriesWithOverlay);
                return _interactiveEntriesWithOverlay;
            }
        }

        public virtual List<InteractiveEntry> InteractiveList { get; } = new List<InteractiveEntry>();

        protected override void UpdateViewBox()
        {
            var sizeOffset = (Surface.TextureSize - Surface.SurfaceSize) / 2;
            _userPadding = Surface.TextPadding;

            var padding = (Surface.TextPadding / 100f) * Surface.SurfaceSize;
            sizeOffset += padding / 2f;

            _baseViewBox = new RectangleF(
                sizeOffset.X,
                sizeOffset.Y,
                Surface.SurfaceSize.X - padding.X,
                Surface.SurfaceSize.Y - padding.Y);

            ViewBox = _baseViewBox;

            if (_globalMenu == null || !_globalMenu.Visible)
                return;

            float reservedHeight = _globalMenu.GetReservedHeight(this, Scale, FontScale, Surface);
            ViewBox = new RectangleF(
                _baseViewBox.X,
                _baseViewBox.Y + reservedHeight,
                _baseViewBox.Width,
                Math.Max(0f, _baseViewBox.Height - reservedHeight));
        }

        public abstract CursorType CursorType { get; protected set; }

        public void LookAt(Vector2 onScreenCoordinates)
        {
            _lastVisualContactFrame = MyAPIGateway.Session.GameplayFrameCounter;
            CursorPosition = onScreenCoordinates;
            OnLookAt(onScreenCoordinates);
            RenderSprites();
        }

        InteractiveEntry _activeTooltipParentEntry;
        InteractiveEntry _manualTooltipParentEntry;
        object _manualTooltipParentObject;
        InteractiveRectangleEntry _tooltipCardEntry;

        readonly Dictionary<ITooltipLine, TooltipLineInteractiveEntry> _tooltipLineEntryByLine =
            new Dictionary<ITooltipLine, TooltipLineInteractiveEntry>();

        readonly HashSet<ITooltipLine> _tooltipLinesUsedThisFrame =
            new HashSet<ITooltipLine>();

        MessageBox _messageBox;
        GlobalMenu _globalMenu;

        protected virtual void OnLookAt(Vector2 onScreenCoordinates)
        {
        }

        protected bool CursorInsideTooltip => _hasTooltipBounds && _tooltipRect.Contains(CursorPosition);

        protected bool CursorInsideTooltipKeepOpenArea =>
            _hasTooltipBounds && _tooltipKeepOpenRect.Contains(CursorPosition);

        protected bool HasRecentVisualContact => MyAPIGateway.Session.GameplayFrameCounter - _lastVisualContactFrame <=
                                                 CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES;

        protected void ClearTooltip()
        {
            HideAttachedTooltip();
        }

        protected void ClearAllTooltips()
        {
            HideAttachedTooltip();
        }

        public override void Run()
        {
            base.Run();
            if (!HasRecentVisualContact)
            {
                CursorPosition = new Vector2(float.NaN, float.NaN);
                ClearTooltip();
            }
        }

        void HideAttachedTooltip()
        {
            if (_tooltipCardEntry != null)
                _tooltipCardEntry.SetVisible(false);

            foreach (var kv in _tooltipLineEntryByLine)
            {
                if (kv.Value != null)
                    kv.Value.SetVisible(false);
            }

            _hasTooltipBounds = false;
            //_cursorInsideClickableTooltipContent = false;
            _tooltipRect = default(RectangleF);
            _tooltipKeepOpenRect = default(RectangleF);

            _tooltipLayerSprites.Clear();
            _tooltipLayerEntries.Clear();
            _tooltipLinesUsedThisFrame.Clear();

            // Keep tooltip entries permanently attached to their parent entry.
            // Invisible entries are non-interactive because InteractiveEntry.Hit(),
            // InteractiveEntry.Click(), CanClick, and ResolveTopHitEntry() are visibility-gated.
        }


        void ClearManualTooltipState()
        {
            _manualTooltipParentEntry = null;
            _manualTooltipParentObject = null;
        }

        static bool TooltipButtonMatches(TooltipActivationMode mode, bool rightClick)
        {
            if (mode == TooltipActivationMode.Click)
                return !rightClick;

            if (mode == TooltipActivationMode.RightClick)
                return rightClick;

            return false;
        }

        InteractiveEntry FindVisibleTooltipEntryByContext(object dataContext)
        {
            for (int i = InteractiveList.Count - 1; i >= 0; i--)
            {
                var entry = InteractiveList[i];
                if (entry != null &&
                    entry.Visible &&
                    entry.Tooltip != null &&
                    Equals(entry.DataContext, dataContext))
                {
                    return entry;
                }
            }

            return null;
        }

        InteractiveEntry ResolveManualTooltipParent()
        {
            if (_manualTooltipParentEntry != null &&
                _manualTooltipParentEntry.Visible &&
                _manualTooltipParentEntry.Tooltip != null)
            {
                return _manualTooltipParentEntry;
            }

            if (_manualTooltipParentObject != null)
            {
                var entry = FindVisibleTooltipEntryByContext(_manualTooltipParentObject);
                if (entry != null)
                {
                    _manualTooltipParentEntry = entry;
                    return entry;
                }
            }

            ClearManualTooltipState();
            return null;
        }

        InteractiveEntry FindTooltipHitTarget()
        {
            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return null;

            for (int i = InteractiveList.Count - 1; i >= 0; i--)
            {
                var entry = InteractiveList[i];
                if (entry == null || !entry.Visible || entry.Tooltip == null)
                    continue;

                if (entry.Hit(position))
                    return entry;
            }

            return null;
        }

        public bool HasTooltipInputAtCursor(bool rightClick)
        {
            var target = FindTooltipHitTarget();
            if (target != null && target.Tooltip != null &&
                (TooltipButtonMatches(target.Tooltip.OpenMode, rightClick) ||
                 TooltipButtonMatches(target.Tooltip.CloseMode, rightClick)))
            {
                return true;
            }

            var active = ResolveManualTooltipParent() ?? _activeTooltipParentEntry;
            if (active != null && active.Visible && active.Tooltip != null && _hasTooltipBounds &&
                TooltipButtonMatches(active.Tooltip.CloseMode, rightClick) &&
                (CursorInsideTooltip || CursorInsideTooltipKeepOpenArea || active.Hit(CursorPosition)))
            {
                return true;
            }

            return false;
        }

        public bool TryHandleTooltipActivationClick(bool rightClick)
        {
            InteractiveEntry tooltipParent;
            return TryHandleTooltipActivationClick(rightClick, out tooltipParent);
        }

        public bool TryHandleTooltipActivationClick(bool rightClick, out InteractiveEntry tooltipParent)
        {
            tooltipParent = null;

            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return false;

            var active = ResolveManualTooltipParent() ?? _activeTooltipParentEntry;
            if (active != null && active.Visible && active.Tooltip != null && _hasTooltipBounds &&
                TooltipButtonMatches(active.Tooltip.CloseMode, rightClick) &&
                (CursorInsideTooltip || CursorInsideTooltipKeepOpenArea || active.Hit(position)))
            {
                tooltipParent = active;
                HideAttachedTooltip();
                ClearManualTooltipState();
                return true;
            }

            var target = FindTooltipHitTarget();
            if (target == null || target.Tooltip == null)
                return false;

            if (!TooltipButtonMatches(target.Tooltip.OpenMode, rightClick))
                return false;

            if (_activeTooltipParentEntry != null && !ReferenceEquals(_activeTooltipParentEntry, target))
                HideAttachedTooltip();

            _manualTooltipParentEntry = target;
            _manualTooltipParentObject = target.DataContext;
            tooltipParent = target;
            return true;
        }

        InteractiveEntry FindTooltipTarget()
        {
            var manualParent = ResolveManualTooltipParent();
            if (manualParent != null)
            {
                var manualTooltip = manualParent.Tooltip;
                if (manualTooltip != null && manualTooltip.CloseMode != TooltipActivationMode.Auto)
                    return manualParent;

                var positionForManual = CursorPosition;
                if (!float.IsNaN(positionForManual.X) && !float.IsNaN(positionForManual.Y) && HasRecentVisualContact &&
                    (manualParent.Hit(positionForManual) || CursorInsideTooltip || CursorInsideTooltipKeepOpenArea))
                {
                    return manualParent;
                }

                ClearManualTooltipState();
                HideAttachedTooltip();
            }

            if (_hasTooltipBounds && _activeTooltipParentEntry != null &&
                _activeTooltipParentEntry.Visible &&
                _activeTooltipParentEntry.Tooltip != null &&
                _activeTooltipParentEntry.Tooltip.CloseMode != TooltipActivationMode.Auto)
            {
                return _activeTooltipParentEntry;
            }

            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return null;

            if (_hasTooltipBounds && (CursorInsideTooltip || CursorInsideTooltipKeepOpenArea))
            {
                var activeParent = _activeTooltipParentObject;
                for (int i = InteractiveList.Count - 1; i >= 0; i--)
                {
                    var entry = InteractiveList[i];
                    if (entry != null &&
                        entry.Visible &&
                        entry.Tooltip != null &&
                        entry.Tooltip.OpenMode == TooltipActivationMode.Auto &&
                        Equals(entry.DataContext, activeParent))
                    {
                        return entry;
                    }
                }
            }

            for (int i = InteractiveList.Count - 1; i >= 0; i--)
            {
                var entry = InteractiveList[i];
                if (entry == null || !entry.Visible || entry.Tooltip == null)
                    continue;

                if (entry.Tooltip.OpenMode != TooltipActivationMode.Auto)
                    continue;

                if (entry.Hit(position))
                    return entry;
            }

            return null;
        }

        void RenderAttachedTooltip(List<MySprite> sprites)
        {
            //_cursorInsideClickableTooltipContent = false;

            var parentEntry = FindTooltipTarget();
            if (parentEntry == null || parentEntry.Tooltip == null)
            {
                HideAttachedTooltip();
                return;
            }

            if (_activeTooltipParentEntry != null && !ReferenceEquals(_activeTooltipParentEntry, parentEntry))
                HideAttachedTooltip();

            var tooltip = parentEntry.Tooltip;
            var tooltipLines = tooltip.Lines ?? new List<ITooltipLine>();
            var tooltipTitle = tooltip.GetTitle();
            var tooltipFooter = tooltip.GetFooter();
            var tooltipIconTexture = tooltip.GetIconTexture();
            var cursor = tooltip.GetCursor();
            var textColor = ForegroundColor;
            var panelColor = ColorableConfig != null ? ColorableConfig.HeaderColor : BackgroundColor;

            int lineCount = tooltipLines.Count;
            float lineScale = 0.52f * Scale * FontScale;

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
                lineSizes[i] = FormatingHelper.GetSizeInPixel(lineTexts[i], "White", lineScale, Surface);

                if (lineSizes[i].X > maxLineWidth)
                    maxLineWidth = lineSizes[i].X;
            }

            RedrawTooltipLayer(
                parentEntry,
                tooltipTitle,
                tooltipLines,
                tooltipFooter,
                tooltipIconTexture,
                cursor,
                lineTexts,
                clickables,
                lineCursors,
                lineSizes,
                maxLineWidth,
                textColor,
                panelColor);

            _activeTooltipParentEntry = parentEntry;
            _activeTooltipParentObject = parentEntry.DataContext;
            _hasTooltipBounds = true;

            sprites.AddRange(_tooltipLayerSprites);

            parentEntry.AddChildren(_tooltipLayerEntries);
        }

        void RedrawTooltipLayer(InteractiveEntry parentEntry,
            string title,
            List<ITooltipLine> tooltipLines,
            string footer,
            string iconTexture,
            CursorType cursor,
            string[] lineTexts,
            bool[] clickables,
            CursorType?[] lineCursors,
            Vector2[] lineSizes,
            float maxLineWidth,
            Color textColor,
            Color panelColor)
        {
            _tooltipLayerSprites.Clear();
            _tooltipLayerEntries.Clear();
            _tooltipLinesUsedThisFrame.Clear();

            const float spacing = 6f;
            Vector2 padding = new Vector2(8f, 4f) * Scale;
            float offset = 16f * Scale;

            float titleScale = 0.72f * Scale * FontScale;
            float lineScale = 0.52f * Scale * FontScale;
            float footerScale = 0.62f * Scale * FontScale;

            var titleSize = FormatingHelper.GetSizeInPixel(title, "White", titleScale, Surface);
            var footerSize = string.IsNullOrEmpty(footer)
                ? Vector2.Zero
                : FormatingHelper.GetSizeInPixel(footer, "White", footerScale, Surface);

            float lineStep = FormatingHelper.GetSizeInPixel("Ag", "White", lineScale, Surface).Y + 2f;

            bool hasIcon = !string.IsNullOrEmpty(iconTexture);

            float linesHeight = tooltipLines.Count * lineStep;
            float titleFooterWidth = Math.Max(titleSize.X, footerSize.X);

            float iconSize = hasIcon
                ? Math.Max(24f * Scale, Math.Min(52f * Scale, Math.Max(linesHeight, 24f * Scale)))
                : 0f;

            float iconGap = hasIcon ? 8f * Scale : 0f;

            // Body is only icon + lines. Title/footer are centered over the whole card.
            float bodyWidth = maxLineWidth + iconSize + iconGap;
            float contentWidth = Math.Max(titleFooterWidth, bodyWidth);

            float contentHeight = titleSize.Y + spacing + Math.Max(linesHeight, iconSize);
            if (!string.IsNullOrEmpty(footer))
                contentHeight += spacing + footerSize.Y;

            float cardWidth = Math.Max(20f * Scale, contentWidth + 2f * padding.X);
            float cardHeight = Math.Max(20f * Scale, contentHeight + 2f * padding.Y);

            var parentBounds = parentEntry.Bounds;

            bool placeOnRight = parentBounds.Center.X <= ViewBox.Center.X;
            float anchorX = placeOnRight
                ? parentBounds.Right + offset
                : parentBounds.X - offset - cardWidth;

            float startX = MathHelper.Clamp(
                anchorX,
                ViewBox.X + padding.X,
                ViewBox.Right - cardWidth - padding.X);

            float startY = MathHelper.Clamp(
                parentBounds.Center.Y - cardHeight * 0.5f,
                ViewBox.Y + padding.Y,
                ViewBox.Bottom - cardHeight - padding.Y);

            var cardRect = new RectangleF(startX, startY, cardWidth, cardHeight);
            var shadowRect = new RectangleF(cardRect.Position + 2f, cardRect.Size);
            var shadowColor = panelColor.MulValue(0.2f);

            _tooltipRect = cardRect;
            _tooltipKeepOpenRect = new RectangleF(
                Math.Min(parentBounds.X, cardRect.X),
                parentBounds.Y,
                Math.Max(parentBounds.Right, cardRect.Right) - Math.Min(parentBounds.X, cardRect.X),
                parentBounds.Height);

            RectanglePanel.CreateSpritesFromRect(shadowRect, _tooltipLayerSprites, shadowColor, 0.2f);
            RectanglePanel.CreateSpritesFromRect(cardRect, _tooltipLayerSprites, panelColor, 0.2f);

            if (_tooltipCardEntry == null || !Equals(_tooltipCardEntry.DataContext, parentEntry.DataContext))
            {
                _tooltipCardEntry = new InteractiveRectangleEntry(
                    cardRect,
                    cursor,
                    parentEntry.DataContext);
            }
            else
            {
                _tooltipCardEntry.SetRect(cardRect);
                _tooltipCardEntry.SetCursor(cursor);
            }

            _tooltipCardEntry.SetVisible(true);
            _tooltipLayerEntries.Add(_tooltipCardEntry);

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

            _tooltipLayerSprites.Add(titleSprite.Shadow(2 * titleScale, shadowColor));
            _tooltipLayerSprites.Add(titleSprite);

            currentY += titleSize.Y + spacing;

            float bodyTopY = currentY;
            float bodyHeight = Math.Max(linesHeight, iconSize);

            if (hasIcon)
            {
                _tooltipLayerSprites.Add(new MySprite
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

                bool lineHovered = hasLineEntry && lineBounds.Contains(CursorPosition);
                var lineColor = lineHovered
                    ? panelColor.DeriveTextAccentColor()
                    : textColor;

                if (hasLineEntry)
                {
                    TooltipLineInteractiveEntry lineEntry;
                    var resolvedCursor = lineCursors[i] ?? (clickables[i] ? CursorType.Hand : CursorType.Default);

                    if (!_tooltipLineEntryByLine.TryGetValue(line, out lineEntry) || lineEntry == null)
                    {
                        lineEntry = new TooltipLineInteractiveEntry(lineBounds, line, resolvedCursor);
                        _tooltipLineEntryByLine[line] = lineEntry;
                    }
                    else
                    {
                        lineEntry.SetRect(lineBounds);
                        lineEntry.SetCursor(resolvedCursor);
                    }

                    lineEntry.SetVisible(true);
                    lineEntry.ClickSound = line.GetClickSound();

                    _tooltipLinesUsedThisFrame.Add(line);
                    _tooltipLayerEntries.Add(lineEntry);
                }

                var position = new Vector2(leftX, currentY - lineSizes[i].Y * 0.25f * lineScale);

                _tooltipLayerSprites.Add(new MySprite
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
                    _tooltipLayerSprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(position.X, position.Y + lineSizes[i].Y),
                        Size = new Vector2(Math.Max(1f, lineSizes[i].X), Math.Max(1f, Scale)),
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

                _tooltipLayerSprites.Add(footerSprite.Shadow(2 * footerScale, shadowColor));
                _tooltipLayerSprites.Add(footerSprite);
            }

            PruneUnusedTooltipLineEntries();
        }

        void PruneUnusedTooltipLineEntries()
        {
            if (_tooltipLineEntryByLine.Count == 0)
                return;

            var remove = new List<ITooltipLine>();

            foreach (var kv in _tooltipLineEntryByLine)
            {
                if (!_tooltipLinesUsedThisFrame.Contains(kv.Key))
                {
                    if (kv.Value != null)
                        kv.Value.SetVisible(false);

                    remove.Add(kv.Key);
                }
            }

            for (int i = 0; i < remove.Count; i++)
                _tooltipLineEntryByLine.Remove(remove[i]);
        }


        public void SetGlobalMenu(List<GlobalMenuEntry> entries)
        {
            if (_globalMenu != null)
                _globalMenu.HideEntries();

            _globalMenu = entries == null || entries.Count == 0 ? null : new GlobalMenu(entries);

            UpdateViewBox();
        }

        public void SetGlobalMenu(params GlobalMenuEntry[] entries) =>
            SetGlobalMenu(entries != null ? new List<GlobalMenuEntry>(entries) : null);

        public void ShowMessageBox(
            string title,
            string content,
            string button1,
            string button2,
            Action<object, object> button1Callback,
            Action<object, object> button2Callback = null,
            string icon = null)
        {
            _messageBox = new MessageBox();
            _messageBox.Show(title, content, button1, button2, button1Callback, button2Callback, icon);
        }

        public bool IsInsideContainer(InteractiveEntry entry, Vector2 position)
        {
            if (entry == null || !entry.Visible || entry.Children == null || entry.Children.Count == 0)
                return false;

            return entry.Hit(position) || _hasTooltipBounds && ReferenceEquals(entry, _activeTooltipParentEntry);
        }

        void UpdateCursorFromTopHit()
        {
            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                return;

            var entries = InteractiveEntries as IList<InteractiveEntry>;
            if (entries == null)
                return;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = ResolveTopHitEntry(entries[i], position);
                if (entry == null)
                    continue;

                CursorType = entry.Cursor;
                return;
            }

            CursorType = CursorType.Default;
        }

        InteractiveEntry ResolveTopHitEntry(InteractiveEntry entry, Vector2 position)
        {
            if (entry == null || !entry.Visible)
                return null;

            bool selfHit = entry.Hit(position);

            // Tooltip child entries are allowed to resolve only through the currently
            // active tooltip parent. This prevents old parents that still reference
            // shared/reused tooltip children from producing ghost hitboxes or cursor styles.
            bool mayCheckChildren =
                entry.Children != null &&
                entry.Children.Count > 0 &&
                (selfHit || (_hasTooltipBounds && ReferenceEquals(entry, _activeTooltipParentEntry)));

            if (mayCheckChildren)
            {
                for (int i = entry.Children.Count - 1; i >= 0; i--)
                {
                    var childHit = ResolveTopHitEntry(entry.Children[i], position);
                    if (childHit != null)
                        return childHit;
                }
            }

            return selfHit ? entry : null;
        }

        protected override List<MySprite> RenderFrame(Func<List<MySprite>> sprites)
        {
            var baseViewBox = _baseViewBox;

            var spriteList = base.RenderFrame(sprites);
            RenderAttachedTooltip(spriteList);

            _globalMenu?.Render(
                this,
                spriteList,
                baseViewBox,
                Scale,
                FontScale,
                Surface,
                ForegroundColor,
                ColorableConfig?.HeaderColor ?? BackgroundColor,
                CursorPosition);

            _messageBox?.Render(
                this,
                spriteList,
                baseViewBox,
                Scale,
                FontScale,
                Surface,
                ForegroundColor,
                BackgroundColor,
                ColorableConfig?.HeaderColor ?? BackgroundColor,
                CursorPosition);

            UpdateCursorFromTopHit();

            if (AppConfig?.CursorScale == 0 || CursorType == CursorType.None)
                return spriteList;

            var cursor = CursorType;
            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                cursor = CursorType.None;

            Cursor.AddCursor(spriteList,
                cursor,
                position,
                new Vector2(32), // hardcoded size
                AppConfig?.CursorScale ?? 1f);

            return spriteList;
        }


        public MyEntity3DSoundEmitter SoundEmitter { get; set; }
        public bool RequiresAlt => AppConfig?.RequiresAlt ?? true;

        public void PlaySounds(MySoundPair sound, bool playIn2D = false)
        {
            if (SoundEmitter == null)
            {
                SoundEmitter = new MyEntity3DSoundEmitter((MyEntity)Block, dopplerScaler: 0.0f)
                {
                    Force3D = true,
                    VolumeMultiplier = 1,
                    CustomVolume = 1.5f,
                    CustomMaxDistance = 30
                };
                SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CanHear].ClearImmediate();
                //SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ShouldPlay2D].ClearImmediate();
                //SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CueType].ClearImmediate();
                SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ImplicitEffect].ClearImmediate();
            }

            SoundEmitter.PlaySound(sound, force2D: playIn2D);
        }

        public override void Dispose()
        {
            SoundEmitter?.Cleanup();
            SoundEmitter = null;
            base.Dispose();
        }
    }
}