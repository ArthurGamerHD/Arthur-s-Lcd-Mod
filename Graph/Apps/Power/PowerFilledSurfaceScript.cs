using System;
using System.Collections.Generic;
using Generated;
using Graph.Apps.Abstract;
using Graph.Extensions;
using Graph.Helpers;
using Graph.System;
using Graph.System.Config;
using Graph.System.Power;
using Graph.System.TerminalControls.Color;
using Graph.System.TerminalControls.Generic;
using Graph.System.TerminalControls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Graph.Apps.Power
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class PowerFilledSurfaceScript : SurfaceScriptBase
    {
        public const string ID = "BatteryGraph";
        public const string TITLE = "LCDMod_PowerFilled";

        protected override string DefaultTitle => TITLE;

        const float BATTERY_SLOT_W = 100;
        const float BATTERY_SLOT_H = 100;
        const float POWER_TEXT_H = 16f;
        const float SCROLLER_W = 8f;
        const int SCROLL_TICK = 12;

        const float ICON_TEXTURE_SIZE = 192f;

        readonly List<PowerCollector> _collectors = new List<PowerCollector>();
        readonly List<PowerEntry> _entries = new List<PowerEntry>();

        public PowerFilledSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _collectors.Clear();
            _entries.Clear();
            FooterHeight = TITLE_BAR_HEIGHT_BASE * LayoutScale;
        }

        public override void Run()
        {
            base.Run();
            if (Config == null) return;
            
            if (_collectors.Count == 0)
                BuildCollectors();

            CollectPower();

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                DrawFooter(sprites);

                if (!HasVisibleItems())
                    DrawMessage(sprites, LocHelper.Empty, "Warning", Config.WarningColor, Config.Scale);
                else
                    DrawBatteries(sprites);

                frame.AddRange(sprites);
            }
        }
        
        protected override void DrawFooter(List<MySprite> sprites)
        {
            int rows = 0;
            for (int i = 0; i < _collectors.Count; i++)
            {
                if (_collectors[i] != null && _collectors[i].HasVisibleItems)
                    rows++;
            }

            if (rows == 0)
            {
                FooterHeight = 0f;
                return;
            }

            float rowHeight = TITLE_BAR_HEIGHT_BASE * LayoutScale;
            FooterHeight = rowHeight * rows;
            float footerTop = ViewBox.Bottom - FooterHeight;
            float margin = 6f * Scale;
            float footerLeft = ViewBox.X + margin;
            float footerWidth = Math.Max(1f, ViewBox.Width - margin * 2f);
            float footerPad = 6f * Scale;
            float contentLeft = footerLeft + footerPad;
            float contentRight = footerLeft + footerWidth - footerPad;
            Color fg = Surface.ScriptForegroundColor;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(ViewBox.X + ViewBox.Width * 0.5f, footerTop + FooterHeight * 0.5f),
                Size = new Vector2(ViewBox.Width, FooterHeight),
                Color = new Color(BackgroundColor.MulValue(0.8f), 0.5f),
                Alignment = TextAlignment.CENTER
            });

            float iconLeft = contentLeft;
            float textScale = Scale * 0.75f * FontScale;
            float textLeftPad = Math.Max(2f, Scale * 2f);
            int rowIndex = 0;

            for (int i = 0; i < _collectors.Count; i++)
            {
                var collector = _collectors[i];
                if (collector == null || !collector.HasVisibleItems)
                    continue;

                DrawFooterRow(sprites, collector, footerTop, rowHeight, rowIndex, iconLeft, contentRight, textScale, textLeftPad, fg);
                rowIndex++;
            }
        }

        // -----------------------------------------------------------------------
        // Battery grid — fills available area, Scale controls slot size
        // -----------------------------------------------------------------------

        void DrawBatteries(List<MySprite> sprites)
        {
            var slots = _entries;

            float minW = BATTERY_SLOT_W * Scale;
            float minH = BATTERY_SLOT_H * Scale;
            float topMargin = 6f * Scale;
            float contentTop = CaretY + topMargin;
            float availW = ViewBox.Width;
            float availH = ViewBox.Height - (contentTop - ViewBox.Y) - FooterHeight;

            float xLeft = ViewBox.X;
            float xRight = ViewBox.X + ViewBox.Width;

            int count = slots.Count;
            int cols = Math.Min(count, Math.Max(1, (int)Math.Floor(availW / minW)));
            int maxRows = Math.Max(1, (int)Math.Floor(availH / minH));
            int totalRows = (int)Math.Ceiling(count / (float)cols);

            bool scroll = totalRows > maxRows;
            int startRow = 0;

            if (scroll)
            {
                int steps = Math.Max(1, totalRows - maxRows);
                int step = GetScrollStep(SCROLL_TICK / 6);
                startRow = step % (steps + 1);

                float vpH = availH - SCROLLER_W * 2 * Scale;
                float barH = (float)maxRows / totalRows * vpH;
                float frac = (float)startRow / steps;
                float barY = frac * (vpH - barH);
                DrawScrollBar(sprites, Scale, contentTop + SCROLLER_W * Scale, vpH, barY + barH / 2f, barH);

                xRight -= SCROLLER_W * Scale;
                availW = xRight - xLeft;
                cols = Math.Min(count, Math.Max(1, (int)Math.Floor(availW / minW)));
                totalRows = (int)Math.Ceiling(count / (float)cols);
            }

            int rows = scroll ? maxRows : Math.Min(maxRows, totalRows);
            float slotW = availW / cols;
            float slotH = minH;

            float gridLeft = xLeft;
            float gridTop = contentTop;

            int startIdx = startRow * cols;
            int show = Math.Min(rows * cols, count - startIdx);

            for (int i = 0; i < show; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float xStart = gridLeft + col * slotW;
                float yStart = gridTop + row * slotH;
                DrawPowerSlot(sprites, slots[startIdx + i], xStart, yStart, slotW, slotH);
            }
        }

        void DrawPowerSlot(List<MySprite> sprites, PowerEntry slot, float xStart, float yStart, float width, float height)
        {
            float powerScale = Scale * 0.70f * FontScale;
            Vector2 powerRef = GetSizeInPixel("999.9 kW", "White", powerScale, Surface);
            float powerTextH = Math.Max(POWER_TEXT_H * Scale, powerRef.Y * 1.15f);
            float labelGap = Math.Max(1f, Scale * 2f);
            float iconSize = Math.Max(0f, height - powerTextH - labelGap);
            float centerX = xStart + width / 2f;
            float centerY = yStart + iconSize / 2f;

            DrawFillableTexture(sprites, slot.FillableTexture, new Vector2(centerX, centerY), iconSize, slot.Ratio, slot.FillColor, ForegroundColor);

            float pctScale = iconSize * 0.35f / Math.Max(GetSizeInPixel(slot.PercentText, "White", 1f, Surface).X, GetSizeInPixel(slot.PercentText, "White", 1f, Surface).Y) * Math.Min(FontScale, 1f);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = slot.PercentText,
                Position = new Vector2(centerX, centerY),
                RotationOrScale = pctScale,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = slot.PowerText,
                Position = new Vector2(centerX, yStart + iconSize + labelGap + (powerTextH - powerRef.Y) / 2f),
                RotationOrScale = powerScale,
                Color = ForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });
        }
        
        static void DrawFillableTexture(List<MySprite> sprites, FillableTexture texture, Vector2 center, float iconSize, float ratio, Color fillColor, Color iconColor)
        {
            ratio = MathHelper.Clamp(ratio, 0f, 1f);

            float texScale = iconSize / ICON_TEXTURE_SIZE;
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
                float fillCenterX = (innerLeft + innerRight) / 2f;
                float fillCy = innerBottom - fillH / 2f;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(fillCenterX, fillCy),
                    Size = new Vector2(innerW, fillH),
                    Color = fillColor,
                    Alignment = TextAlignment.CENTER
                });
            }
            
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = texture.Name,
                Position = center,
                Size = new Vector2(iconSize),
                Color = iconColor,
                Alignment = TextAlignment.CENTER
            });
        }

        void DrawScrollBar(List<MySprite> sprites, float scale, float initialY,
            float viewportH, float barCenter, float barH)
        {
            float cx = ViewBox.X + ViewBox.Width - (SCROLLER_W / 2f) * scale;
            int bw = (int)(SCROLLER_W * scale);

            var trackCtr = new Vector2(cx, (float)Math.Round(initialY + viewportH / 2f, MidpointRounding.ToEven));
            DrawCapsule(sprites, trackCtr, bw, viewportH,
                new Color(Surface.ScriptForegroundColor.R,
                    Surface.ScriptForegroundColor.G,
                    Surface.ScriptForegroundColor.B, 127));

            var thumbCtr = new Vector2(cx, (float)Math.Round(initialY + barCenter, MidpointRounding.ToEven));
            DrawCapsule(sprites, thumbCtr, bw, barH,
                new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B, 250));
        }

        static void DrawCapsule(List<MySprite> sprites, Vector2 center, int width, float height, Color color)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = center, Size = new Vector2(width, height + 0.5f),
                Color = color, Alignment = TextAlignment.CENTER
            });
            var caps = new Vector2(width);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y - height / 2f),
                Size = caps, RotationOrScale = 0f,
                Color = color, Alignment = TextAlignment.CENTER
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y + height / 2f),
                Size = caps, RotationOrScale = (float)Math.PI,
                Color = color, Alignment = TextAlignment.CENTER
            });
        }

        void BuildCollectors()
        {
            _collectors.Clear();
            
            var labelCharging = MyTexts.GetString("HudEnergyGroupCharging");
            var labelDischarging = MyTexts.GetString("BlockActionTitle_Discharge");
            var labelJumpNotReady = MyTexts.GetString("ScreenMedicals_RespawnShipNotReady");
            var labelJumpReady = MyTexts.GetString("ScreenMedicals_RespawnShipReady");
            var labelFull = MyTexts.GetString("RadialMenuAction_Signal_Full");
            
            _collectors.Add(new BatteryPowerCollector(Config)
            {
                ChargingLabel = labelCharging,
                DischargingLabel = labelDischarging,
                FullLabel = labelFull
            });

            _collectors.Add(new JumpDrivePowerCollector(Config)
            {
                ChargingLabel = labelCharging,
                ReadyLabel = labelJumpReady,
                NotReadyLabel = labelJumpNotReady,
            });
        }

        void CollectPower()
        {
            if (GridLogic == null)
                return;

            _entries.Clear();

            foreach (var collector in _collectors) 
                collector.Collect(GridLogic, _entries);
        }

        bool HasVisibleItems()
        {
            foreach (var collector in _collectors)
            {
                if (collector != null && collector.HasVisibleItems)
                    return true;
            }

            return false;
        }

        void DrawFooterRow(List<MySprite> sprites, PowerCollector collector, float footerTop, float rowHeight,
            int rowIndex, float iconLeft, float contentRight, float textScale, float textLeftPad, Color fg)
        {
            float bandCY = footerTop + rowHeight * (rowIndex + 0.5f);
            float iconSize = rowHeight * 0.55f;
            var iconCenter = new Vector2(iconLeft + iconSize / 2f, bandCY);
            float displayedRatio = (float)Math.Round(collector.AverageCharge * 100f, MidpointRounding.AwayFromZero) / 100f;
            DrawFillableTexture(sprites, collector.FillableTexture, iconCenter, iconSize, displayedRatio, collector.StatusColor, fg);

            string avgText = FormatingHelper.PercentageToString(displayedRatio) + " " + collector.FooterPrefix;
            float textLeft = iconLeft + iconSize + textLeftPad;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = avgText,
                Position = new Vector2(textLeft, bandCY - GetSizeInPixel(avgText, "White", textScale, Surface).Y / 2f),
                RotationOrScale = textScale,
                Color = fg,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            string statusText = collector.StatusText;
            if (!string.IsNullOrEmpty(statusText))
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = statusText,
                    Position = new Vector2(ViewBox.Center.X, bandCY - GetSizeInPixel(statusText, "White", textScale, Surface).Y / 2f),
                    RotationOrScale = textScale,
                    Color = collector.StatusColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }

            if (collector.HasRightSideText)
            {
                float rightX = contentRight;
                string rightText = collector.RightSideText;
                Vector2 size = GetSizeInPixel(rightText, "White", textScale, Surface);
                AddHeaderSprite(sprites, new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = rightText,
                    Position = new Vector2(rightX, bandCY - size.Y / 2f),
                    RotationOrScale = textScale,
                    Color = collector.RightSideColor,
                    Alignment = TextAlignment.RIGHT,
                    FontId = "White"
                });
            }
        }
    }
}
