using System;
using System.Collections.Generic;
using Graph.Apps.Utility;
using Graph.Extensions;
using Graph.Panels;
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
    public abstract class InteractiveSurfaceScript : SurfaceScriptBase, IEyeTracking
    {
        const long CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES = 20;
        object _activeTooltipParentObject;
        RectangleF _tooltipRect;
        RectangleF _tooltipKeepOpenRect;
        bool _hasTooltipBounds;
        bool _cursorInsideClickableTooltipContent;
        long _lastVisualContactFrame = long.MinValue;
        
        protected InteractiveSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public abstract Vector2 CursorPosition { get; protected set; }

        public abstract List<InteractiveEntry> InteractiveEntries { get; }

        public abstract CursorType CursorType { get; protected set; }

        public void LookAt(Vector2 onScreenCoordinates)
        {
            _lastVisualContactFrame = MyAPIGateway.Session.GameplayFrameCounter;
            OnLookAt(onScreenCoordinates);
        }

        protected abstract void OnLookAt(Vector2 onScreenCoordinates);

        protected object ActiveTooltipParentObject => _activeTooltipParentObject;

        protected bool HasTooltipBounds => _hasTooltipBounds;

        protected bool CursorInsideTooltip => _hasTooltipBounds && _tooltipRect.Contains(CursorPosition);

        protected bool CursorInsideTooltipKeepOpenArea =>
            _hasTooltipBounds && _tooltipKeepOpenRect.Contains(CursorPosition);

        protected bool CursorInsideClickableTooltipContent => _cursorInsideClickableTooltipContent;

        protected bool HasRecentVisualContact => MyAPIGateway.Session.GameplayFrameCounter - _lastVisualContactFrame <= CURSOR_VISUAL_CONTACT_TIMEOUT_FRAMES;

        protected bool IsActiveTooltipParent(object parentObject)
        {
            return Equals(_activeTooltipParentObject, parentObject);
        }

        protected void ClearTooltip()
        {
            _activeTooltipParentObject = null;
            _hasTooltipBounds = false;
            _cursorInsideClickableTooltipContent = false;
            _tooltipRect = default(RectangleF);
            _tooltipKeepOpenRect = default(RectangleF);
        }

        protected bool DrawTooltip(
            List<MySprite> sprites,
            InteractiveEntry parentEntry,
            string title,
            List<object> lines,
            string footer)
        {
            if (parentEntry == null)
                return false;

            _cursorInsideClickableTooltipContent = false;

            const float spacing = 6f;
            Vector2 padding = new Vector2(8f, 4f) * Scale;
            var offset = 16f * Scale;


            float titleScale = 0.72f * Scale * FontScale;
            float lineScale = 0.52f * Scale * FontScale;
            float footerScale = 0.62f * Scale * FontScale;
            var textColor = ForegroundColor;
            var panelColor = ColorableConfig != null ? ColorableConfig.HeaderColor : BackgroundColor;
            var tooltipLines = lines ?? new List<object>();
            var tooltipFooter = footer ?? string.Empty;

            var titleSize = GetSizeInPixel(title ?? string.Empty, "White", titleScale, Surface);
            var footerSize = string.IsNullOrEmpty(tooltipFooter)
                ? Vector2.Zero
                : GetSizeInPixel(tooltipFooter, "White", footerScale, Surface);
            float lineStep = GetSizeInPixel("Ag", "White", lineScale, Surface).Y + 2f;
            var lineSizes = new Vector2[tooltipLines.Count];
            var lineTexts = new string[tooltipLines.Count];
            var clickables = new ClickableText[tooltipLines.Count];
            float maxLineWidth = 0f;
            for (int i = 0; i < tooltipLines.Count; i++)
            {
                var clickable = tooltipLines[i] as ClickableText;
                clickables[i] = clickable;
                lineTexts[i] = tooltipLines[i] != null ? tooltipLines[i].ToString() : string.Empty;
                lineSizes[i] = GetSizeInPixel(lineTexts[i], "White", lineScale, Surface);
                if (lineSizes[i].X > maxLineWidth)
                    maxLineWidth = lineSizes[i].X;
            }

            float contentWidth = Math.Max(titleSize.X, Math.Max(maxLineWidth, footerSize.X));
            float cardWidth = Math.Max(130f, contentWidth + 2f * padding.X);
            float contentHeight = titleSize.Y + spacing + tooltipLines.Count * lineStep;
            if (!string.IsNullOrEmpty(tooltipFooter))
                contentHeight += spacing + footerSize.Y;
            float cardHeight = Math.Max(60f, contentHeight + 2f * padding.Y);

            var parentBounds = parentEntry.Bounds;
            bool placeOnRight = parentBounds.Center.X <= ViewBox.Center.X;
            float anchorX = placeOnRight
                ? parentBounds.Right + (offset * Scale)
                : parentBounds.X - (offset * Scale) - cardWidth;
            float startX = MathHelper.Clamp(anchorX, ViewBox.X + padding.X, ViewBox.Right - cardWidth - padding.X);
            float startY = MathHelper.Clamp(parentBounds.Center.Y - cardHeight * 0.5f, ViewBox.Y + padding.Y, ViewBox.Bottom - cardHeight - padding.Y);

            var cardRect = new RectangleF(startX, startY, cardWidth, cardHeight);
            var shadowRect = new RectangleF(cardRect.Position + 2f, cardRect.Size);
            var shadowColor = panelColor.MulValue(0.2f);
            RectanglePanel.CreateSpritesFromRect(shadowRect, sprites, shadowColor, 0.2f);
            RectanglePanel.CreateSpritesFromRect(cardRect, sprites, panelColor, 0.2f);
            InteractiveEntries.Add(new InteractiveRectangleEntry(cardRect, CursorType.Default,
                parentEntry.DataContext));

            _activeTooltipParentObject = parentEntry.DataContext;
            _tooltipRect = cardRect;
            _tooltipKeepOpenRect = new RectangleF(
                Math.Min(parentBounds.X, cardRect.X),
                parentBounds.Y,
                Math.Max(parentBounds.Right, cardRect.Right) - Math.Min(parentBounds.X, cardRect.X),
                parentBounds.Height);
            _hasTooltipBounds = true;

            float currentY = cardRect.Y + padding.Y;
            float centerX = cardRect.Center.X;
            float leftX = cardRect.X + padding.X;

            var titleSprite = new MySprite
            {
                Type = SpriteType.TEXT,
                Data = title,
                Position = new Vector2(centerX, currentY),
                Color = textColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = titleScale
            };
            sprites.Add(titleSprite.Shadow(2 * titleScale, shadowColor));
            sprites.Add(titleSprite);
            currentY += titleSize.Y + spacing;

            for (int i = 0; i < tooltipLines.Count; i++)
            {
                var clickable = clickables[i];
                var lineBounds = new RectangleF(
                    leftX,
                    currentY - lineSizes[i].Y * 0.5f,
                    Math.Max(lineSizes[i].X, 1f),
                    lineStep);
                bool clickableHovered = clickable != null && lineBounds.Contains(CursorPosition);
                var lineColor = clickableHovered
                    ? panelColor.DeriveTextAscentColor()
                    : textColor;
                if (clickable != null)
                {
                    InteractiveEntries.Add(new InteractiveRectangleEntry(
                        lineBounds,
                        null,
                        clickable.DataContext ?? clickable,
                        clickable.OnClick)
                    {
                        ClickSound = clickable.ClickSound
                    });
                    if (clickableHovered)
                        _cursorInsideClickableTooltipContent = true;
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

                if (clickable != null)
                {
                    sprites.Add(new MySprite
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

            if (!string.IsNullOrEmpty(tooltipFooter))
            {
                currentY += spacing;

                var footerSprite = new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = tooltipFooter,
                    Position = new Vector2(centerX, currentY),
                    Color = textColor,
                    FontId = "White",
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = footerScale
                };

                sprites.Add(footerSprite.Shadow(2 * footerScale, shadowColor));
                sprites.Add(footerSprite);
            }

            return cardRect.Contains(CursorPosition);
        }

        protected override List<MySprite> RenderFrame(Func<List<MySprite>> sprites)
        {
            var spriteList = base.RenderFrame(sprites);
            var cursor = CursorType;
            var position = CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || !HasRecentVisualContact)
                cursor = CursorType.None;

            Cursor.AddCursor(spriteList,
                cursor,
                position,
                new Vector2(32), // hardcoded size
                Config != null ? Config.CursorScale : 1f);

            return spriteList;
        }
        
        
        public MyEntity3DSoundEmitter SoundEmitter { get; set; }
        
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