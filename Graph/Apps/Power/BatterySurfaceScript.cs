using System;
using System.Collections.Generic;
using System.Text;
using Generated;
using Graph.Apps.Abstract;
using Graph.Helpers;
using Graph.System;
using Graph.System.Config;
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
    public partial class BatterySurfaceScript : SurfaceScriptBase,
        IUsesTerminalControlGroup<BaseTerminalControlGroup>,
        IUsesTerminalControl<CheckboxHideEmpty>
    {
        public const string ID    = "BatteryGraph";
        public const string TITLE = "DisplayName_BlockGroup_Batteries";

        protected override string DefaultTitle => TITLE;

        const float BATTERY_SLOT_W = 120f;
        const float BATTERY_SLOT_H = 140f;
        const float MAX_BATTERY_SLOT_W = 120f;
        const float MAX_BATTERY_SLOT_H = 140f;
        const float POWER_TEXT_H   = 16f;
        const float SCROLLER_W     = 8f;
        const int   SCROLL_TICK    = 12;

        const float WARN_THRESHOLD  = 0.35f;
        const float ERROR_THRESHOLD = 0.15f;

        readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        readonly List<IMyBatteryBlock> _visible   = new List<IMyBatteryBlock>();

        string _labelCharging;
        string _labelDischarging;
        bool   _labelsReady;

        // Aggregate charge state shown in the title (set each frame by CollectBatteries)
        string _aggregateStatus = string.Empty;

        // Footer aggregate values (set each frame by CollectBatteries)
        float  _avgCharge   = 0f;
        string _timeLabel   = "--:--";
        bool   _isCharging  = false;

        // -----------------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------------

        public BatterySurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size) { }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _labelsReady  = false;
            FooterHeight  = TITLE_BAR_HEIGHT_BASE * Scale;
        }

        public override void Run()
        {
            base.Run();
            if (Config == null) return;

            if (!_labelsReady)
            {
                _labelCharging    = MyTexts.GetString("HudEnergyGroupCharging");
                _labelDischarging = MyTexts.GetString("BlockActionTitle_Discharge");
                _labelsReady      = true;
            }

            CollectBatteries();

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                DrawFooter(sprites);

                if (_visible.Count == 0)
                    DrawMessage(sprites, LocHelper.Empty, "Warning", Config.WarningColor, Config.Scale);
                else
                    DrawBatteries(sprites);

                frame.AddRange(sprites);
            }
        }

        void CollectBatteries()
        {
            _batteries.Clear();
            if (GridLogic == null) return;
            _batteries.AddRange(GridLogic.GetBatteries());

            _visible.Clear();
            float totalIn = 0f, totalOut = 0f;
            for (int i = 0; i < _batteries.Count; i++)
            {
                var b = _batteries[i];
                if (!Config.HideEmpty || b.MaxStoredPower > 0f)
                {
                    _visible.Add(b);
                    totalIn  += b.CurrentInput;
                    totalOut += b.CurrentOutput;
                }
            }

            const float eps = 0.001f;
            if (totalIn > totalOut + eps)
                _aggregateStatus = _labelCharging;
            else if (totalOut > totalIn + eps)
                _aggregateStatus = _labelDischarging;
            else
                _aggregateStatus = string.Empty;

            // Footer aggregates
            if (_visible.Count > 0)
            {
                float sumRatio = 0f;
                float totalStored = 0f;
                float totalMax    = 0f;
                float totalNetIn  = 0f;
                float totalNetOut = 0f;
                for (int i = 0; i < _visible.Count; i++)
                {
                    var b = _visible[i];
                    sumRatio    += GetRatio(b);
                    totalStored += b.CurrentStoredPower;
                    totalMax    += b.MaxStoredPower;
                    totalNetIn  += b.CurrentInput;
                    totalNetOut += b.CurrentOutput;
                }
                _avgCharge  = sumRatio / _visible.Count;
                _isCharging = totalNetIn > totalNetOut + eps;

                float netRate = Math.Abs(totalNetIn - totalNetOut); // MW
                if (netRate < eps)
                {
                    _timeLabel = "--:--";
                }
                else if (_isCharging)
                {
                    float remainingMWh = totalMax - totalStored;
                    float hours = remainingMWh / netRate;
                    _timeLabel = FormatTimeHours(hours);
                }
                else
                {
                    float hours = totalStored / netRate;
                    _timeLabel = FormatTimeHours(hours);
                }
            }
            else
            {
                _avgCharge  = 0f;
                _isCharging = false;
                _timeLabel  = "--:--";
            }
        }

        static string FormatTimeHours(float hours)
        {
            if (hours < 0f) return "--:--";
            // Cap display at 99h 59m to avoid overflow
            if (hours > 99.99f) return ">99h";
            int h = (int)hours;
            int m = (int)((hours - h) * 60f);
            if (h > 0)
                return h.ToString() + "h " + m.ToString() + "m";
            return m.ToString() + "m";
        }

        // -----------------------------------------------------------------------
        // Title — appends aggregate charge state
        // -----------------------------------------------------------------------

        protected override void DrawTitle(List<MySprite> sprites)
        {
            var margin   = ViewBox.Size.X * Margin;
            var position = ViewBox.Position;
            position.X += margin;
            position.Y += (ViewBox.Size.Y * Margin) / 2;

            CaretY = position.Y;

            if (!TitleVisible)
                return;

            AddHeaderSprite(sprites, new MySprite
            {
                Type      = SpriteType.TEXTURE,
                Data      = Icon,
                Position  = position + new Vector2(20) * Scale,
                Size      = new Vector2(40 * Scale),
                Color     = Config.HeaderColor,
                Alignment = TextAlignment.CENTER
            });
            position.X += ViewBox.Width / 8f;

            sprites.Add(MySprite.CreateClipRect(new Rectangle(
                (int)position.X, (int)position.Y,
                (int)(ViewBox.Width - position.X + ViewBox.X),
                (int)(position.Y + 35 * Scale))));

            string baseTitle = MyTexts.GetString(DefaultTitle);
            string fullTitle = string.IsNullOrEmpty(_aggregateStatus)
                ? baseTitle
                : baseTitle + " (" + _aggregateStatus + ")";

            var sb = new StringBuilder(fullTitle);
            TrimText(ref sb, ViewBox.Width - position.X + ViewBox.X, 1.3f);

            AddHeaderSprite(sprites, new MySprite
            {
                Type            = SpriteType.TEXT,
                Data            = sb.ToString(),
                Position        = position,
                RotationOrScale = Scale * 1.3f,
                Color           = Config.HeaderColor,
                Alignment       = TextAlignment.LEFT,
                FontId          = "White"
            });

            sprites.Add(MySprite.CreateClearClipRect());
            CaretY += TITLE_BAR_HEIGHT_BASE * Scale;
        }

        // -----------------------------------------------------------------------
        // Footer — separator line + avg charge (left) + time remaining (right)
        // -----------------------------------------------------------------------

        protected override void DrawFooter(List<MySprite> sprites)
        {
            FooterHeight = TITLE_BAR_HEIGHT_BASE * Scale;
            float footerTop = ViewBox.Bottom - FooterHeight;
            float margin    = ViewBox.Size.X * Margin;
            Color fg        = Surface.ScriptForegroundColor;
            Color accent    = Config.HeaderColor;

            // Separator line — 1px tall, full content width
            var sepColor = new Color(fg.R, fg.G, fg.B, 160);
            sprites.Add(new MySprite
            {
                Type      = SpriteType.TEXTURE,
                Data      = "SquareSimple",
                Position  = new Vector2(ViewBox.Center.X, footerTop),
                Size      = new Vector2(ViewBox.Width - margin * 2f, Math.Max(1f, Scale)),
                Color     = sepColor,
                Alignment = TextAlignment.CENTER
            });

            // Vertical center of the footer band
            float bandCY = footerTop + FooterHeight / 2f;

            // --- Left side: battery icon + "Avg: 75%" ---
            float iconSize   = FooterHeight * 0.55f;
            float iconLeft   = ViewBox.X + margin;
            var   iconCenter = new Vector2(iconLeft + iconSize / 2f, bandCY);

            Color iconColor = GetBatteryIconColor(_avgCharge);
            DrawBatteryGeometry(sprites, iconCenter, iconSize * 0.55f, iconSize, _avgCharge, iconColor, fg);

            string avgText   = "Avg: " + FormatingHelper.PercentageToString(_avgCharge);
            float  textLeft  = iconLeft + iconSize + margin * 0.5f;
            float  textScale = Scale * 0.75f;

            sprites.Add(new MySprite
            {
                Type            = SpriteType.TEXT,
                Data            = avgText,
                Position        = new Vector2(textLeft, bandCY - GetSizeInPixel(avgText, "White", textScale, Surface).Y / 2f),
                RotationOrScale = textScale,
                Color           = fg,
                Alignment       = TextAlignment.LEFT,
                FontId          = "White"
            });

            // --- Right side: direction arrow + time label ---
            // Arrow: "+" when charging, "-" when discharging; use a small accent-colored glyph
            string arrow     = _isCharging ? "+" : "-";
            string rightText = arrow + " " + _timeLabel;
            float  rightX    = ViewBox.Right - margin;

            AddHeaderSprite(sprites, new MySprite
            {
                Type            = SpriteType.TEXT,
                Data            = rightText,
                Position        = new Vector2(rightX, bandCY - GetSizeInPixel(rightText, "White", textScale, Surface).Y / 2f),
                RotationOrScale = textScale,
                Color           = accent,
                Alignment       = TextAlignment.RIGHT,
                FontId          = "White"
            });
        }

        // -----------------------------------------------------------------------
        // Battery grid — fills available area, Scale controls slot size
        // -----------------------------------------------------------------------

        void DrawBatteries(List<MySprite> sprites)
        {
            float minW   = BATTERY_SLOT_W * Scale;
            float minH   = BATTERY_SLOT_H * Scale;
            float availW = ViewBox.Width - ViewBox.Width * Margin * 2f;
            float availH = ViewBox.Height - (CaretY - ViewBox.Y) - FooterHeight;

            float xLeft  = ViewBox.X + ViewBox.Width * Margin;
            float xRight = ViewBox.X + ViewBox.Width - ViewBox.Width * Margin;

            int count    = _visible.Count;
            // Columns: limited by how many fit at minimum size; capped at battery count
            int cols     = Math.Min(count, Math.Max(1, (int)Math.Floor(availW / minW)));
            int maxRows  = Math.Max(1, (int)Math.Floor(availH / minH));
            int totalRows = (int)Math.Ceiling(count / (float)cols);

            bool scroll   = totalRows > maxRows;
            int  startRow = 0;

            if (scroll)
            {
                int steps = Math.Max(1, totalRows - maxRows);
                int step  = GetScrollStep(SCROLL_TICK / 6);
                startRow  = step % (steps + 1);

                float vpH  = availH - SCROLLER_W * 2 * Scale;
                float barH = (float)maxRows / totalRows * vpH;
                float frac = (float)startRow / steps;
                float barY = frac * (vpH - barH);
                DrawScrollBar(sprites, Scale, CaretY + SCROLLER_W * Scale, vpH, barY + barH / 2f, barH);

                xRight -= SCROLLER_W * Scale;
                availW  = xRight - xLeft;
                cols    = Math.Min(count, Math.Max(1, (int)Math.Floor(availW / minW)));
                totalRows = (int)Math.Ceiling(count / (float)cols);
            }

            // Flex: divide available space equally among cols and visible rows
            int   rows   = scroll ? maxRows : Math.Min(maxRows, totalRows);
            float slotW  = availW / cols;
            float slotH  = availH / rows;

            // Clamp slot size so a small battery count does not stretch slots
            // across the entire viewport (e.g. a single battery filling the screen).
            float maxSlotW = MAX_BATTERY_SLOT_W * Scale;
            float maxSlotH = MAX_BATTERY_SLOT_H * Scale;
            slotW = Math.Min(slotW, maxSlotW);
            slotH = Math.Min(slotH, maxSlotH);

            // When the grid is narrower/shorter than the available area, center it.
            float gridW      = cols * slotW;
            float gridH      = rows * slotH;
            float gridLeft   = xLeft  + (availW - gridW) / 2f;
            float gridTop    = CaretY + (availH - gridH) / 2f;

            int startIdx = startRow * cols;
            int show     = Math.Min(rows * cols, count - startIdx);

            for (int i = 0; i < show; i++)
            {
                int   col    = i % cols;
                int   row    = i / cols;
                float xStart = gridLeft + col * slotW;
                float yStart = gridTop  + row * slotH;
                DrawBatterySlot(sprites, _visible[startIdx + i], xStart, yStart, slotW, slotH);
            }
        }

        void DrawBatterySlot(List<MySprite> sprites, IMyBatteryBlock bat,
                             float xStart, float yStart, float slotW, float slotH)
        {
            float ratio       = GetRatio(bat);
            Color fillColor   = GetBatteryIconColor(ratio);
            Color borderColor = Surface.ScriptForegroundColor;
            Color textColor   = Surface.ScriptForegroundColor;

            // Padding: 12% horizontal each side, 10% vertical top+bottom
            float hPad       = slotW * 0.12f;
            float vPad       = slotH * 0.10f;
            float powerTextH = POWER_TEXT_H * Scale;
            float innerH     = slotH - vPad * 2f;
            float iconAreaH  = innerH - powerTextH;
            float centerX    = xStart + slotW / 2f;
            float centerY    = yStart + vPad + iconAreaH / 2f;

            float bodyH = iconAreaH * 0.90f;
            float bodyW = Math.Min(bodyH * 0.60f, slotW - hPad * 2f);

            DrawBatteryGeometry(sprites, new Vector2(centerX, centerY),
                bodyW, bodyH, ratio, fillColor, borderColor);

            // Percentage centered on icon
            string  pctText  = FormatingHelper.PercentageToString(ratio);
            Vector2 pctRef   = GetSizeInPixel(pctText, "White", 1f, Surface);
            float   pctScale = pctRef.X > 0f
                ? Math.Min(bodyW * 0.78f / pctRef.X, bodyH * 0.38f / pctRef.Y)
                : Scale * 0.55f;
            sprites.Add(new MySprite
            {
                Type            = SpriteType.TEXT,
                Data            = pctText,
                Position        = new Vector2(centerX, centerY - pctRef.Y * pctScale / 2f),
                RotationOrScale = pctScale,
                Color           = textColor,
                Alignment       = TextAlignment.CENTER,
                FontId          = "White"
            });

            // Saída atual — vertically centered in the power text band
            double outputWatts = bat.CurrentOutput * 1000000.0;
            string powerText   = outputWatts > 1.0 ? FormatingHelper.WattsToString(outputWatts) : "-";
            sprites.Add(new MySprite
            {
                Type            = SpriteType.TEXT,
                Data            = powerText,
                Position        = new Vector2(centerX, yStart + vPad + iconAreaH + powerTextH * 0.10f),
                RotationOrScale = Scale * 0.70f,
                Color           = textColor,
                Alignment       = TextAlignment.CENTER,
                FontId          = "White"
            });
        }

        // -----------------------------------------------------------------------
        // Battery geometry
        // borderColor = outline + nub (text color); fillColor = charge fill only
        // -----------------------------------------------------------------------

        static void DrawBatteryGeometry(List<MySprite> sprites, Vector2 center,
                                        float bodyW, float bodyH, float ratio,
                                        Color fillColor, Color borderColor)
        {
            var emptyBg = new Color(borderColor.R, borderColor.G, borderColor.B, 40);
            const float border = 3f;

            // Background (empty body)
            sprites.Add(new MySprite
            {
                Type      = SpriteType.TEXTURE,
                Data      = "SquareSimple",
                Position  = center,
                Size      = new Vector2(bodyW, bodyH),
                Color     = emptyBg,
                Alignment = TextAlignment.CENTER
            });

            // Charge fill — bottom to top
            if (ratio > 0.005f)
            {
                float innerH = bodyH - border * 2f;
                float fillH  = innerH * ratio;
                float fillCY = center.Y + innerH / 2f - fillH / 2f;
                sprites.Add(new MySprite
                {
                    Type      = SpriteType.TEXTURE,
                    Data      = "SquareSimple",
                    Position  = new Vector2(center.X, fillCY),
                    Size      = new Vector2(bodyW - border * 2f, fillH),
                    Color     = fillColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            // Top terminal nub
            sprites.Add(new MySprite
            {
                Type      = SpriteType.TEXTURE,
                Data      = "SquareSimple",
                Position  = new Vector2(center.X, center.Y - bodyH / 2f - bodyH * 0.07f),
                Size      = new Vector2(bodyW * 0.35f, bodyH * 0.10f),
                Color     = borderColor,
                Alignment = TextAlignment.CENTER
            });

            // Border lines — top, bottom, left, right
            float bw    = Math.Max(1f, border * 0.8f);
            float halfW = bodyW / 2f;
            float halfH = bodyH / 2f;

            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(center.X, center.Y - halfH),
                Size = new Vector2(bodyW, bw), Color = borderColor, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(center.X, center.Y + halfH),
                Size = new Vector2(bodyW, bw), Color = borderColor, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(center.X - halfW, center.Y),
                Size = new Vector2(bw, bodyH), Color = borderColor, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(center.X + halfW, center.Y),
                Size = new Vector2(bw, bodyH), Color = borderColor, Alignment = TextAlignment.CENTER });
        }

        // -----------------------------------------------------------------------
        // Scrollbar
        // -----------------------------------------------------------------------

        void DrawScrollBar(List<MySprite> sprites, float scale, float initialY,
                           float viewportH, float barCenter, float barH)
        {
            float cx = ViewBox.X + ViewBox.Width - (SCROLLER_W / 2f) * scale;
            int   bw = (int)(SCROLLER_W * scale);

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

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        static float GetRatio(IMyBatteryBlock bat)
        {
            if (bat.MaxStoredPower <= 0f) return 0f;
            return Math.Max(0f, Math.Min(1f, bat.CurrentStoredPower / bat.MaxStoredPower));
        }

        Color GetBatteryIconColor(float ratio)
        {
            if (ratio < ERROR_THRESHOLD) return Config.ErrorColor;
            if (ratio < WARN_THRESHOLD)  return Config.WarningColor;
            return Config.HeaderColor;
        }
    }
}
