using System;
using System.Collections.Generic;
using Graph.Apps.Abstract;
using Graph.Helpers;
using Graph.System;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;

namespace Graph.Apps.Power
{
    [MyTextSurfaceScript(ID, TITLE)]
    public class EnergyDashboardSurfaceScript : SurfaceScriptBase
    {
        public const string ID    = "LCDMod_EnergyDashboard";
        public const string TITLE = "LCDMod_EnergyDashboard";

        protected override string DefaultTitle => TITLE;

        static readonly MyDefinitionId ElectricityId =
            new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");

        struct Category
        {
            public double CurrentW;
            public double MaxW;
        }

        Category _solar;
        Category _wind;
        Category _reactor;
        Category _engine;
        Category _batteryProd;
        double _totalMaxW;
        double _totalConsumptionW;

        float  _avgBatteryCharge;
        bool   _isCharging;
        string _timeLabel = "--";

        readonly List<IMyPowerProducer> _producers = new List<IMyPowerProducer>();
        readonly List<IMyTerminalBlock> _terminals  = new List<IMyTerminalBlock>();
        readonly List<IMyBatteryBlock>  _batteries  = new List<IMyBatteryBlock>();

        public EnergyDashboardSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size) { }

        public override void Run()
        {
            base.Run();
            if (Config == null) return;

            Scale = GetAutoScaleUniform();
            UpdateViewBox();

            CollectData();

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                DrawDashboard(sprites);
                frame.AddRange(sprites);
            }
        }

        // -----------------------------------------------------------------------
        // Data collection
        // -----------------------------------------------------------------------

        void CollectData()
        {
            var grid = Block?.CubeGrid as IMyCubeGrid;

            _solar       = new Category();
            _wind        = new Category();
            _reactor     = new Category();
            _engine      = new Category();
            _batteryProd = new Category();
            _totalConsumptionW = 0;

            _producers.Clear();
            if (grid != null)
                GridGroupsHelper.GetAllLogicBlocksOfType(grid, _producers, GridLinkTypeEnum.Logical);

            for (int i = 0; i < _producers.Count; i++)
            {
                var prod = _producers[i];
                try
                {
                    double cur = prod.CurrentOutput * 1000000.0;
                    double max = prod.MaxOutput * 1000000.0;

                    if (prod is IMyBatteryBlock)
                    {
                        _batteryProd.CurrentW += cur;
                        _batteryProd.MaxW += max;
                    }
                    else if (prod is IMySolarPanel)
                    {
                        _solar.CurrentW += cur;
                        _solar.MaxW += max;
                    }
                    else if (prod is IMyWindTurbine)
                    {
                        _wind.CurrentW += cur;
                        _wind.MaxW += max;
                    }
                    else if (prod is IMyReactor)
                    {
                        _reactor.CurrentW += cur;
                        _reactor.MaxW += max;
                    }
                    else
                    {
                        try
                        {
                            var typeId = prod.BlockDefinition.TypeIdString ?? string.Empty;
                            if (typeId.EndsWith("HydrogenEngine", StringComparison.OrdinalIgnoreCase))
                            {
                                _engine.CurrentW += cur;
                                _engine.MaxW += max;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            _totalMaxW = _solar.MaxW + _wind.MaxW + _reactor.MaxW + _engine.MaxW + _batteryProd.MaxW;

            _terminals.Clear();
            if (grid != null)
                GridGroupsHelper.GetAllLogicBlocksOfType(grid, _terminals, GridLinkTypeEnum.Logical);

            for (int i = 0; i < _terminals.Count; i++)
            {
                MyResourceSinkComponent sink = null;
                try { _terminals[i].Components.TryGet(out sink); } catch { }
                if (sink == null) continue;

                double w = 0;
                try { w = sink.CurrentInputByType(ElectricityId) * 1000000.0; } catch { }
                if (w > 0) _totalConsumptionW += w;
            }

            _batteries.Clear();
            if (GridLogic != null)
                _batteries.AddRange(GridLogic.GetBatteries());

            if (_batteries.Count > 0)
            {
                const float eps = 0.001f;
                float sumRatio    = 0f;
                float totalStored = 0f;
                float totalMax    = 0f;
                float netIn       = 0f;
                float netOut      = 0f;

                for (int i = 0; i < _batteries.Count; i++)
                {
                    var b = _batteries[i];
                    float r = b.MaxStoredPower > 0f
                        ? Math.Max(0f, Math.Min(1f, b.CurrentStoredPower / b.MaxStoredPower))
                        : 0f;
                    sumRatio    += r;
                    totalStored += b.CurrentStoredPower;
                    totalMax    += b.MaxStoredPower;
                    netIn       += b.CurrentInput;
                    netOut      += b.CurrentOutput;
                }

                _avgBatteryCharge = sumRatio / _batteries.Count;
                _isCharging = netIn > netOut + eps;

                float netRate = Math.Abs(netIn - netOut);
                if (netRate < eps)
                    _timeLabel = "--";
                else if (_isCharging)
                    _timeLabel = FormatTimeHours((totalMax - totalStored) / netRate);
                else
                    _timeLabel = FormatTimeHours(totalStored / netRate);
            }
            else
            {
                _avgBatteryCharge = 0f;
                _isCharging = false;
                _timeLabel = "--";
            }
        }

        static string FormatTimeHours(float hours)
        {
            if (hours < 0f) return "--";
            if (hours > 99.99f) return ">99h";
            int h = (int)hours;
            int m = (int)((hours - h) * 60f);
            return h > 0 ? h.ToString() + "h " + m.ToString() + "m" : m.ToString() + "m";
        }

        // -----------------------------------------------------------------------
        // Layout
        // -----------------------------------------------------------------------

        void DrawDashboard(List<MySprite> sprites)
        {
            float margin   = ViewBox.Width * Margin;
            float xLeft    = ViewBox.X + margin;
            float xRight   = ViewBox.Right - margin;
            float contentW = xRight - xLeft;
            float gapH     = 6f * Scale;
            float rowH     = 22f * Scale;
            float bigBarH  = 32f * Scale;
            float divH     = Math.Max(1f, Scale);

            int prodRows = 0;
            if (_solar.MaxW > 0)       prodRows++;
            if (_wind.MaxW > 0)        prodRows++;
            if (_reactor.MaxW > 0)     prodRows++;
            if (_engine.MaxW > 0)      prodRows++;
            if (_batteryProd.MaxW > 0) prodRows++;

            float y = CaretY + gapH;

            // Section A: power balance bar (2 label rows + bar)
            DrawPowerBalanceSection(sprites, xLeft, xRight, contentW, y, bigBarH, rowH);
            y += rowH + bigBarH + rowH + gapH;

            DrawDivider(sprites, xLeft, xRight, y);
            y += divH + gapH;

            // Section B: production breakdown
            if (prodRows > 0)
            {
                DrawProductionSection(sprites, xLeft, xRight, contentW, y, rowH);
                y += prodRows * rowH + gapH;

                DrawDivider(sprites, xLeft, xRight, y);
                y += divH + gapH;
            }

            // Section C: battery summary
            float batH = rowH * 1.8f;
            if (y + batH <= ViewBox.Bottom - margin)
                DrawBatterySection(sprites, xLeft, xRight, contentW, y, batH);
        }

        // -----------------------------------------------------------------------
        // Section A: power balance bar
        // -----------------------------------------------------------------------

        void DrawPowerBalanceSection(List<MySprite> sprites, float xLeft, float xRight, float contentW,
            float y, float bigBarH, float rowH)
        {
            Color fg     = Surface.ScriptForegroundColor;
            Color accent = Config.HeaderColor;
            float ts     = Scale * 0.72f;

            // Top row: consumption (left) vs capacity (right)
            string consumeLabel = "Consumo: " + FormatingHelper.WattsToString(_totalConsumptionW);
            string capLabel     = "Max: " + FormatingHelper.WattsToString(_totalMaxW);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = consumeLabel,
                Position = new Vector2(xLeft, y),
                RotationOrScale = ts, Color = fg,
                Alignment = TextAlignment.LEFT, FontId = "White"
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = capLabel,
                Position = new Vector2(xRight, y),
                RotationOrScale = ts, Color = accent,
                Alignment = TextAlignment.RIGHT, FontId = "White"
            });

            y += rowH;

            // Big horizontal bar: consumption / max-capacity
            float ratio   = _totalMaxW > 0 ? (float)Math.Min(1.0, _totalConsumptionW / _totalMaxW) : 0f;
            Color barBg   = new Color(fg.R, fg.G, fg.B, 25);
            Color barFill = GetLoadColor(ratio);

            float barCX = xLeft + contentW / 2f;
            float barCY = y + bigBarH / 2f;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(barCX, barCY), Size = new Vector2(contentW, bigBarH),
                Color = barBg, Alignment = TextAlignment.CENTER
            });

            if (ratio > 0.005f)
            {
                float fillW = contentW * ratio;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(xLeft + fillW / 2f, barCY), Size = new Vector2(fillW, bigBarH),
                    Color = barFill, Alignment = TextAlignment.CENTER
                });
            }

            // Percentage centered inside bar
            string pctText = FormatingHelper.PercentageToString(ratio);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = pctText,
                Position = new Vector2(barCX, y + bigBarH * 0.08f),
                RotationOrScale = Scale * 0.85f, Color = fg,
                Alignment = TextAlignment.CENTER, FontId = "White"
            });

            y += bigBarH;

            // Bottom row: current production total (centered)
            double totalProd = _solar.CurrentW + _wind.CurrentW + _reactor.CurrentW
                             + _engine.CurrentW + _batteryProd.CurrentW;
            string prodLabel = "Produção: " + FormatingHelper.WattsToString(totalProd);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = prodLabel,
                Position = new Vector2(barCX, y),
                RotationOrScale = ts, Color = fg,
                Alignment = TextAlignment.CENTER, FontId = "White"
            });
        }

        // -----------------------------------------------------------------------
        // Section B: production breakdown
        // -----------------------------------------------------------------------

        void DrawProductionSection(List<MySprite> sprites, float xLeft, float xRight, float contentW,
            float y, float rowH)
        {
            Color fg     = Surface.ScriptForegroundColor;
            Color accent = Config.HeaderColor;
            float labelW = contentW * 0.30f;
            float barW   = contentW * 0.45f;
            float numW   = contentW - labelW - barW;

            if (_solar.MaxW > 0)
            {
                DrawProductionRow(sprites, "Solar", _solar, xLeft, y, labelW, barW, numW, rowH, fg, accent);
                y += rowH;
            }
            if (_wind.MaxW > 0)
            {
                DrawProductionRow(sprites, "Wind", _wind, xLeft, y, labelW, barW, numW, rowH, fg, accent);
                y += rowH;
            }
            if (_reactor.MaxW > 0)
            {
                DrawProductionRow(sprites, "Reactor", _reactor, xLeft, y, labelW, barW, numW, rowH, fg, accent);
                y += rowH;
            }
            if (_engine.MaxW > 0)
            {
                DrawProductionRow(sprites, "Engine", _engine, xLeft, y, labelW, barW, numW, rowH, fg, accent);
                y += rowH;
            }
            if (_batteryProd.MaxW > 0)
            {
                DrawProductionRow(sprites, "Battery", _batteryProd, xLeft, y, labelW, barW, numW, rowH, fg, accent);
            }
        }

        void DrawProductionRow(List<MySprite> sprites, string label, Category cat,
            float xLeft, float y, float labelW, float barW, float numW, float rowH,
            Color fg, Color accent)
        {
            float ratio    = cat.MaxW > 0 ? (float)Math.Min(1.0, cat.CurrentW / cat.MaxW) : 0f;
            float rowCY    = y + rowH / 2f;
            float ts       = Scale * 0.70f;
            float barH     = rowH * 0.52f;
            Color barBg    = new Color(fg.R, fg.G, fg.B, 25);
            float barXLeft = xLeft + labelW;

            // Label
            Vector2 labelSz = GetSizeInPixel(label, "White", ts, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = label,
                Position = new Vector2(xLeft, rowCY - labelSz.Y / 2f),
                RotationOrScale = ts, Color = fg,
                Alignment = TextAlignment.LEFT, FontId = "White"
            });

            // Bar background
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(barXLeft + barW / 2f, rowCY),
                Size = new Vector2(barW, barH),
                Color = barBg, Alignment = TextAlignment.CENTER
            });

            // Bar fill
            if (ratio > 0.005f)
            {
                float fillW = barW * ratio;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(barXLeft + fillW / 2f, rowCY),
                    Size = new Vector2(fillW, barH),
                    Color = accent, Alignment = TextAlignment.CENTER
                });
            }

            // Value: current watts
            string valText = FormatingHelper.WattsToString(cat.CurrentW);
            Vector2 valSz  = GetSizeInPixel(valText, "White", ts, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = valText,
                Position = new Vector2(barXLeft + barW + numW, rowCY - valSz.Y / 2f),
                RotationOrScale = ts, Color = accent,
                Alignment = TextAlignment.RIGHT, FontId = "White"
            });
        }

        // -----------------------------------------------------------------------
        // Section C: battery summary
        // -----------------------------------------------------------------------

        void DrawBatterySection(List<MySprite> sprites, float xLeft, float xRight, float contentW,
            float y, float sectionH)
        {
            Color fg     = Surface.ScriptForegroundColor;
            Color accent = Config.HeaderColor;
            float cy     = y + sectionH / 2f;
            float ts     = Scale * 0.78f;

            Color iconColor = GetBatteryIconColor(_avgBatteryCharge);
            float iconH  = sectionH * 0.75f;
            float iconW  = iconH * 0.55f;
            float iconCX = xLeft + iconW / 2f + 2f * Scale;

            DrawBatteryIcon(sprites, new Vector2(iconCX, cy), iconW, iconH,
                _avgBatteryCharge, iconColor, fg);

            float textX = iconCX + iconW / 2f + 6f * Scale;

            string avgText = "Avg: " + FormatingHelper.PercentageToString(_avgBatteryCharge);
            Vector2 avgSz  = GetSizeInPixel(avgText, "White", ts, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = avgText,
                Position = new Vector2(textX, cy - avgSz.Y / 2f),
                RotationOrScale = ts, Color = iconColor,
                Alignment = TextAlignment.LEFT, FontId = "White"
            });

            string stateText = (_isCharging ? "+" : "-") + " " + _timeLabel;
            Vector2 stSz     = GetSizeInPixel(stateText, "White", ts, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = stateText,
                Position = new Vector2(xRight, cy - stSz.Y / 2f),
                RotationOrScale = ts, Color = accent,
                Alignment = TextAlignment.RIGHT, FontId = "White"
            });
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        void DrawDivider(List<MySprite> sprites, float xLeft, float xRight, float y)
        {
            Color fg = Surface.ScriptForegroundColor;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2((xLeft + xRight) / 2f, y + Math.Max(1f, Scale) / 2f),
                Size = new Vector2(xRight - xLeft, Math.Max(1f, Scale)),
                Color = new Color(fg.R, fg.G, fg.B, 80),
                Alignment = TextAlignment.CENTER
            });
        }

        Color GetLoadColor(float ratio)
        {
            if (ratio >= 0.90f) return Config.ErrorColor;
            if (ratio >= 0.70f) return Config.WarningColor;
            return Config.HeaderColor;
        }

        Color GetBatteryIconColor(float ratio)
        {
            if (ratio < 0.15f) return Config.ErrorColor;
            if (ratio < 0.35f) return Config.WarningColor;
            return Config.HeaderColor;
        }

        static void DrawBatteryIcon(List<MySprite> sprites, Vector2 center, float bodyW, float bodyH,
            float ratio, Color fillColor, Color borderColor)
        {
            var emptyBg = new Color(borderColor.R, borderColor.G, borderColor.B, 40);
            const float border = 3f;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = center, Size = new Vector2(bodyW, bodyH),
                Color = emptyBg, Alignment = TextAlignment.CENTER
            });

            if (ratio > 0.005f)
            {
                float innerH = bodyH - border * 2f;
                float fillH  = innerH * ratio;
                float fillCY = center.Y + innerH / 2f - fillH / 2f;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(center.X, fillCY),
                    Size = new Vector2(bodyW - border * 2f, fillH),
                    Color = fillColor, Alignment = TextAlignment.CENTER
                });
            }

            // Nub on top
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(center.X, center.Y - bodyH / 2f - bodyH * 0.07f),
                Size = new Vector2(bodyW * 0.35f, bodyH * 0.10f),
                Color = borderColor, Alignment = TextAlignment.CENTER
            });

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
    }
}
