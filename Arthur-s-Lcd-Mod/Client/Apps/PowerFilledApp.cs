using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Helpers;
using LcdMod.Client.Gui.Controls.Interactive;
using LcdMod.Client.Gui.Models.Power;
using LcdMod.Client.Utility;
using VRage;
using VRage.Game.GUI.TextPanel;
using ScreenConfigPower = LcdMod.Common.Config.Models.Apps.ScreenConfigPower;
using VRageMath;

namespace LcdMod.Client.Apps
{
    internal sealed class PowerFilledApp
    {
        const float BatterySlotW = 100f;
        const float BatterySlotH = 100f;
        const float ScrollerW = 8f;
        const int ScrollTick = 12;
        const float IconTextureSize = 192f;

        readonly SurfaceScriptBase _surfaceHost;
        readonly InteractiveSurfaceScript _interactiveHost;
        readonly List<PowerCollector> _collectors = new List<PowerCollector>();
        readonly List<PowerEntry> _entries = new List<PowerEntry>();
        readonly List<InteractiveEntry> _interactiveList = new List<InteractiveEntry>();
        readonly Dictionary<long, PowerEntry> _entryById = new Dictionary<long, PowerEntry>();
        readonly Dictionary<long, InteractiveRectangleEntry> _entryHitboxById = new Dictionary<long, InteractiveRectangleEntry>();
        ScreenConfigPower _config;

        internal List<PowerCollector> Collectors => _collectors;
        internal List<PowerEntry> Entries => _entries;
        internal List<InteractiveEntry> InteractiveList => _interactiveList;
        internal Dictionary<long, PowerEntry> EntryById => _entryById;

        public PowerFilledApp(SurfaceScriptBase surfaceHost)
        {
            _surfaceHost = surfaceHost;
            _interactiveHost = surfaceHost as InteractiveSurfaceScript;
            if (_interactiveHost == null)
                throw new ArgumentException("PowerFilledApp requires an InteractiveSurfaceScript host.", "surfaceHost");
        }

        public void SetConfig(ScreenConfigPower config)
        {
            _config = config;
        }

        public void LayoutChanged()
        {
            _collectors.Clear();
            _entries.Clear();
            _entryById.Clear();
            ClearPowerEntryHitboxes();
        }

        public void Update(LcdMod.Client.Grid.GridLogic gridLogic)
        {
            if (_config == null)
                return;

            if (_collectors.Count == 0)
                BuildCollectors();

            if (gridLogic == null)
                return;

            _entries.Clear();
            _entryById.Clear();

            for (int i = 0; i < _collectors.Count; i++)
                _collectors[i].Collect(gridLogic, _entries);

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry != null)
                    _entryById[entry.EntryId] = entry;
            }
        }

        public bool HasVisibleItems()
        {
            for (int i = 0; i < _collectors.Count; i++)
            {
                if (_collectors[i] != null && _collectors[i].HasVisibleItems)
                    return true;
            }

            return false;
        }

        public PowerEntry GetPowerEntry(long entryId)
        {
            PowerEntry entry;
            return _entryById.TryGetValue(entryId, out entry) ? entry : null;
        }

        public List<MySprite> GetSprites()
        {
            BeginPowerEntryHitboxFrame();
            var sprites = new List<MySprite>();
            var owner = (PowerFilledSurfaceScript)_surfaceHost;
            DrawFooter(owner, sprites);
            DrawBatteries(owner, sprites);
            return sprites;
        }

        void BuildCollectors()
        {
            _collectors.Clear();

            var labelCharging = MyTexts.GetString("HudEnergyGroupCharging");
            var labelDischarging = MyTexts.GetString("BlockActionTitle_Discharge");
            var labelJumpNotReady = MyTexts.GetString("ScreenMedicals_RespawnShipNotReady");
            var labelJumpReady = MyTexts.GetString("ScreenMedicals_RespawnShipReady");
            var labelFull = MyTexts.GetString("RadialMenuAction_Signal_Full");

            _collectors.Add(new BatteryPowerCollector(_config)
            {
                ChargingLabel = labelCharging,
                DischargingLabel = labelDischarging,
                FullLabel = labelFull
            });

            _collectors.Add(new JumpDrivePowerCollector(_config)
            {
                ChargingLabel = labelCharging,
                ReadyLabel = labelJumpReady,
                NotReadyLabel = labelJumpNotReady
            });
        }

        void DrawBatteries(PowerFilledSurfaceScript owner, List<MySprite> sprites)
        {
            float minW = BatterySlotW * owner.Scale;
            float minH = BatterySlotH * owner.Scale;
            float contentTop = GetContentTop(owner) + 6f * owner.Scale;
            float availW = owner.ViewBox.Width;
            float availH = owner.ViewBox.Height - (contentTop - owner.ViewBox.Y) - GetFooterHeight(owner);
            float xLeft = owner.ViewBox.X;
            float xRight = owner.ViewBox.X + owner.ViewBox.Width;

            int count = _entries.Count;
            int cols = Math.Min(count, Math.Max(1, (int)Math.Floor(availW / minW)));
            int maxRows = Math.Max(1, (int)Math.Floor(availH / minH));
            int totalRows = (int)Math.Ceiling(count / (float)cols);
            bool scroll = totalRows > maxRows;
            int startRow = 0;

            if (scroll)
            {
                int steps = Math.Max(1, totalRows - maxRows);
                int step = GetScrollStep(ScrollTick / 6f);
                startRow = step % (steps + 1);

                float vpH = availH - ScrollerW * 2 * owner.Scale;
                float barH = (float)maxRows / totalRows * vpH;
                float frac = (float)startRow / steps;
                float barY = frac * (vpH - barH);
                DrawScrollBar(owner, sprites, owner.Scale, contentTop + ScrollerW * owner.Scale, vpH, barY + barH / 2f, barH);

                xRight -= ScrollerW * owner.Scale;
                availW = xRight - xLeft;
                cols = Math.Min(count, Math.Max(1, (int)Math.Floor(availW / minW)));
            }

            int rows = scroll ? maxRows : Math.Min(maxRows, totalRows);
            float slotW = availW / cols;
            float slotH = minH;
            int startIdx = startRow * cols;
            int show = Math.Min(rows * cols, count - startIdx);

            for (int i = 0; i < show; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float xStart = xLeft + col * slotW;
                float yStart = contentTop + row * slotH;
                RegisterPowerEntryHitbox(_entries[startIdx + i], new RectangleF(xStart, yStart, slotW, slotH));
            }
        }

        void BeginPowerEntryHitboxFrame()
        {
            _interactiveList.Clear();
            foreach (var kv in _entryHitboxById)
                kv.Value?.SetVisible(false);
        }

        void ClearPowerEntryHitboxes()
        {
            foreach (var kv in _entryHitboxById)
                kv.Value?.SetVisible(false);
            _entryHitboxById.Clear();
            _interactiveList.Clear();
        }

        void RegisterPowerEntryHitbox(PowerEntry entry, RectangleF bounds)
        {
            if (entry == null)
                return;

            InteractiveRectangleEntry hitbox;
            if (!_entryHitboxById.TryGetValue(entry.EntryId, out hitbox) || hitbox == null)
            {
                hitbox = new InteractiveRectangleEntry(bounds, CursorType.Hand, entry.EntryId, null, BuildPowerEntryTooltip(entry.EntryId))
                {
                    ClickSound = AudioHelper.HudClick,
                    CustomRender = RenderPowerEntryHitbox
                };
                _entryHitboxById[entry.EntryId] = hitbox;
            }
            else
            {
                hitbox.SetRect(bounds);
                hitbox.SetCursor(CursorType.Hand);
                hitbox.SetTooltip(BuildPowerEntryTooltip(entry.EntryId));
                hitbox.CustomRender = RenderPowerEntryHitbox;
            }

            hitbox.SetVisible(true);
            _interactiveList.Add(hitbox);
        }

        void RenderPowerEntryHitbox(InteractiveEntry hitbox, InteractiveRenderContext context, List<MySprite> sprites)
        {
            if (hitbox == null)
                return;

            var entry = GetPowerEntry((long)hitbox.DataContext);
            if (entry == null)
                return;

            DrawPowerSlotVisual((PowerFilledSurfaceScript)_surfaceHost, sprites, entry, hitbox.Bounds);
        }

        InteractiveTooltip BuildPowerEntryTooltip(long entryId)
        {
            return new InteractiveTooltip(
                delegate
                {
                    var entry = GetPowerEntry(entryId);
                    if (entry != null && entry.Entity != null && !string.IsNullOrEmpty(entry.Entity.CustomName))
                        return entry.Entity.CustomName;
                    return entry != null ? entry.PercentText : string.Empty;
                },
                delegate
                {
                    var entry = GetPowerEntry(entryId);
                    return entry != null ? entry.GetDetails() : new List<ITooltipLine>();
                },
                null,
                null,
                TooltipActivationMode.Click,
                TooltipActivationMode.Click,
                delegate
                {
                    var entry = GetPowerEntry(entryId);
                    return entry != null ? entry.Icon : string.Empty;
                });
        }

        void DrawFooter(PowerFilledSurfaceScript owner, List<MySprite> sprites)
        {
            int rows = 0;
            for (int i = 0; i < _collectors.Count; i++)
                if (_collectors[i] != null && _collectors[i].HasVisibleItems)
                    rows++;

            if (rows == 0)
            {
                return;
            }

            float rowHeight = 40f * owner.Scale * owner.Surface.FontSize;
            float footerHeight = rowHeight * rows;
            float footerTop = owner.ViewBox.Bottom - footerHeight;
            float footerLeft = owner.ViewBox.X;
            float footerWidth = Math.Max(1f, owner.ViewBox.Width);
            float footerPad = 6f * owner.Scale;
            float contentLeft = footerLeft + footerPad;
            float contentRight = footerLeft + footerWidth - footerPad;
            Color fg = owner.Surface.ScriptForegroundColor;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(owner.ViewBox.X + owner.ViewBox.Width * 0.5f, footerTop + footerHeight * 0.5f),
                Size = new Vector2(owner.ViewBox.Width, footerHeight),
                Color = new Color(owner.BackgroundColor.MulValue(0.8f), 0.5f),
                Alignment = TextAlignment.CENTER
            });

            float iconLeft = contentLeft;
            float textScale = owner.Scale * 0.75f * owner.Surface.FontSize;
            float textLeftPad = Math.Max(2f, owner.Scale * 2f);
            int rowIndex = 0;

            for (int i = 0; i < _collectors.Count; i++)
            {
                var collector = _collectors[i];
                if (collector == null || !collector.HasVisibleItems)
                    continue;
                DrawFooterRow(owner, sprites, collector, footerTop, rowHeight, rowIndex, iconLeft, contentRight, textScale, textLeftPad, fg);
                rowIndex++;
            }
        }

        void DrawFooterRow(PowerFilledSurfaceScript owner, List<MySprite> sprites, PowerCollector collector, float footerTop, float rowHeight,
            int rowIndex, float iconLeft, float contentRight, float textScale, float textLeftPad, Color fg)
        {
            float bandCy = footerTop + rowHeight * (rowIndex + 0.5f);
            float iconSize = rowHeight * 0.55f;
            var iconCenter = new Vector2(iconLeft + iconSize / 2f, bandCy);
            float displayedRatio = (float)Math.Round(collector.AverageCharge * 100f, MidpointRounding.AwayFromZero) / 100f;

            DrawFillableTexture(sprites, collector.FillableTexture, iconCenter, iconSize, displayedRatio, collector.StatusColor, fg,
                collector.DrawCenterIcon, 0f, collector.CenterIconScale);

            string avgText = FormatingHelper.PercentageToString(displayedRatio) + " " + collector.FooterPrefix;
            float textLeft = iconLeft + iconSize + textLeftPad;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = avgText,
                Position = new Vector2(textLeft, bandCy - FormatingHelper.GetSizeInPixel(avgText, "White", textScale, owner.Surface).Y / 2f),
                RotationOrScale = textScale,
                Color = fg,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            if (!string.IsNullOrEmpty(collector.StatusText))
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = collector.StatusText,
                    Position = new Vector2(owner.ViewBox.Center.X, bandCy - FormatingHelper.GetSizeInPixel(collector.StatusText, "White", textScale, owner.Surface).Y / 2f),
                    RotationOrScale = textScale,
                    Color = collector.StatusColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }

            if (!collector.HasRightSideText)
                return;

            var rightText = collector.RightSideText;
            var size = FormatingHelper.GetSizeInPixel(rightText, "White", textScale, owner.Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = rightText,
                Position = new Vector2(contentRight, bandCy - size.Y / 2f),
                RotationOrScale = textScale,
                Color = collector.RightSideColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });
        }

        public void DrawPowerSlotVisual(PowerFilledSurfaceScript owner, List<MySprite> sprites, PowerEntry slot, RectangleF bounds)
        {
            float width = bounds.Width;
            float height = bounds.Height;
            float labelGap = Math.Max(1f, owner.Scale * 2f);
            Vector2 pctRef = FormatingHelper.GetSizeInPixel(slot.PercentText, "White", 1f, owner.Surface);
            float pctScale = Math.Min((width * 0.6f) / Math.Max(1f, pctRef.X), (height * 0.22f) / Math.Max(1f, pctRef.Y)) * Math.Min(owner.Surface.FontSize, 1f);
            float pctH = pctRef.Y * pctScale;
            float iconSize = Math.Max(0f, Math.Min(width, height - pctH - labelGap));
            float centerX = bounds.X + width / 2f;
            float centerY = bounds.Y + iconSize / 2f;

            DrawFillableTexture(sprites, slot.FillableTexture, new Vector2(centerX, centerY), iconSize, slot.Ratio, slot.FillColor,
                owner.Surface.ScriptForegroundColor, slot.DrawCenterIcon, slot.CenterIconRotation, slot.CenterIconScale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = slot.PercentText,
                Position = new Vector2(centerX, bounds.Y + iconSize + labelGap),
                RotationOrScale = pctScale,
                Color = owner.Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });
        }

        static void DrawFillableTexture(List<MySprite> sprites, FillableTexture texture, Vector2 center, float iconSize, float ratio, Color fillColor, Color iconColor,
            bool drawCenterIcon = true, float centerIconRotation = 0f, float centerIconScale = 1f)
        {
            ratio = MathHelper.Clamp(ratio, 0f, 1f);
            float texScale = iconSize / IconTextureSize;
            float spriteLeft = center.X - iconSize / 2f;
            float spriteTop = center.Y - iconSize / 2f;
            float innerLeft = spriteLeft + (texture.Left + texture.Margin) * texScale;
            float innerTop = spriteTop + (texture.Top + texture.Margin) * texScale;
            float innerRight = center.X + iconSize / 2f - (texture.Right + texture.Margin) * texScale;
            float innerBottom = center.Y + iconSize / 2f - (texture.Bottom + texture.Margin) * texScale;
            float innerW = Math.Max(0f, innerRight - innerLeft);
            float innerH = Math.Max(0f, innerBottom - innerTop);

            if (ratio > 0.005f && innerW > 0f && innerH > 0f)
            {
                float fillH = innerH * ratio;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2((innerLeft + innerRight) / 2f, innerBottom - fillH / 2f),
                    Size = new Vector2(innerW, fillH),
                    Color = fillColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = texture.Name, Position = center, Size = new Vector2(iconSize), Color = iconColor, Alignment = TextAlignment.CENTER });

            if (!drawCenterIcon || string.IsNullOrEmpty(texture.CenterIconTexture))
                return;

            float innerMin = Math.Min(innerW, innerH);
            float centerIconSize = innerMin > 0f ? innerMin * centerIconScale : iconSize * centerIconScale;
            Vector2 centerIconPos = innerMin > 0f ? new Vector2((innerLeft + innerRight) / 2f, (innerTop + innerBottom) / 2f) : center;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = texture.CenterIconTexture,
                Position = centerIconPos,
                Size = new Vector2(centerIconSize),
                RotationOrScale = centerIconRotation,
                Color = iconColor,
                Alignment = TextAlignment.CENTER
            });
        }

        static int GetScrollStep(float secondsPerStep)
        {
            var sess = Sandbox.ModAPI.MyAPIGateway.Session;
            if (sess == null)
                return 0;
            if (secondsPerStep <= 0f)
                secondsPerStep = 1f / 60f;
            int ticksPerStep = Math.Max(1, (int)Math.Round(secondsPerStep * 60f));
            return (int)(sess.GameplayFrameCounter / ticksPerStep);
        }

        float GetContentTop(PowerFilledSurfaceScript owner)
        {
            return owner.TitleVisible ? owner.ViewBox.Y + (40f * owner.Scale * owner.Surface.FontSize) : owner.ViewBox.Y;
        }

        float GetFooterHeight(PowerFilledSurfaceScript owner)
        {
            int rows = 0;
            for (int i = 0; i < _collectors.Count; i++)
                if (_collectors[i] != null && _collectors[i].HasVisibleItems)
                    rows++;
            if (rows == 0)
                return 0f;
            return (40f * owner.Scale * owner.Surface.FontSize) * rows;
        }

        void DrawScrollBar(PowerFilledSurfaceScript owner, List<MySprite> sprites, float scale, float initialY, float viewportH, float barCenter, float barH)
        {
            float cx = owner.ViewBox.X + owner.ViewBox.Width - (ScrollerW / 2f) * scale;
            int bw = (int)(ScrollerW * scale);
            var trackCtr = new Vector2(cx, (float)Math.Round(initialY + viewportH / 2f, MidpointRounding.ToEven));
            DrawCapsule(sprites, trackCtr, bw, viewportH, new Color(owner.Surface.ScriptForegroundColor.R, owner.Surface.ScriptForegroundColor.G, owner.Surface.ScriptForegroundColor.B, 127));
            var thumbCtr = new Vector2(cx, (float)Math.Round(initialY + barCenter, MidpointRounding.ToEven));
            DrawCapsule(sprites, thumbCtr, bw, barH, new Color(_config.HeaderColor.R, _config.HeaderColor.G, _config.HeaderColor.B, 250));
        }

        static void DrawCapsule(List<MySprite> sprites, Vector2 center, int width, float height, Color color)
        {
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = center, Size = new Vector2(width, height + 0.5f), Color = color, Alignment = TextAlignment.CENTER });
            var caps = new Vector2(width);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SemiCircle", Position = new Vector2(center.X, center.Y - height / 2f), Size = caps, RotationOrScale = 0f, Color = color, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SemiCircle", Position = new Vector2(center.X, center.Y + height / 2f), Size = caps, RotationOrScale = (float)Math.PI, Color = color, Alignment = TextAlignment.CENTER });
        }
    }
}
