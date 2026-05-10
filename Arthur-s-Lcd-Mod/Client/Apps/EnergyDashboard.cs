using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using ComboboxGraphWindow = LcdMod.Client.Terminal.Controls.Generic.ComboboxGraphWindow;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;

namespace LcdMod.Client.Apps
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class EnergyDashboardSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControl<ComboboxGraphWindow>
    {
        protected override ConfigKind ConfigKind => ConfigKind.Power;
        public const string ID = "LcdMod_EnergyDashboard";
        public const string TITLE = "LcdMod_EnergyDashboard";

        protected override string DefaultTitle => TITLE;

        static readonly MyDefinitionId ElectricityId =
            new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");

        // Graph time window options in seconds
        static readonly float[] WindowOptions = { 1f, 5f, 30f, 60f, 300f };

        // Ring buffer for the two line graphs
        const int GRAPH_POINTS = 90;

        struct Sample
        {
            public float TimeS;
            public double ProductionW;
            public double ConsumptionW;
        }

        readonly Sample[] _samples = new Sample[GRAPH_POINTS];
        int _sampleHead;
        int _sampleCount;
        float _lastSampleTime = -999f;
        float _lastWindowSeconds = -1f;

        // Per-category production
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

        // Battery aggregate
        float _avgBatteryCharge;
        bool _isCharging;
        string _timeLabel = "--";

        readonly List<IMyPowerProducer> _producers = new List<IMyPowerProducer>();
        readonly List<IMyTerminalBlock> _terminals = new List<IMyTerminalBlock>();
        readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();

        public EnergyDashboardSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        // -----------------------------------------------------------------------
        // Main loop
        // -----------------------------------------------------------------------

        public override void SafeRun()
        {
            if (AppConfig == null) return;

            Scale = GetAutoScaleUniform();
            UpdateViewBox();

            CollectData();
            TryAddSample();

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
            var grid = Block?.CubeGrid;

            _solar = new Category();
            _wind = new Category();
            _reactor = new Category();
            _engine = new Category();
            _batteryProd = new Category();
            _totalConsumptionW = 0;

            _producers.Clear();
            if (grid != null)
                GridHelper.GetAllLogicBlocksOfType(grid, _producers, GridLinkTypeEnum.Logical);

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
                            var tid = prod.BlockDefinition.TypeIdString ?? string.Empty;
                            if (tid.EndsWith("HydrogenEngine", StringComparison.OrdinalIgnoreCase))
                            {
                                _engine.CurrentW += cur;
                                _engine.MaxW += max;
                            }
                        }
                        catch (Exception e)
                        {
                            ErrorHandlerHelper.LogError(e, this);
                        }
                    }
                }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
            }

            _totalMaxW = _solar.MaxW + _wind.MaxW + _reactor.MaxW + _engine.MaxW + _batteryProd.MaxW;

            _terminals.Clear();
            if (grid != null)
                GridHelper.GetAllLogicBlocksOfType(grid, _terminals, GridLinkTypeEnum.Logical);

            for (int i = 0; i < _terminals.Count; i++)
            {
                // Skip power producers (batteries, reactors, solar, wind, hydrogen engines):
                // their internal electricity sink would double-count as consumption.
                if (_terminals[i] is IMyPowerProducer) continue;

                MyResourceSinkComponent sink = null;
                try
                {
                    _terminals[i].Components.TryGet(out sink);
                }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }

                if (sink == null) continue;

                double w = 0;
                try
                {
                    w = sink.CurrentInputByType(ElectricityId) * 1000000.0;
                }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }

                if (w > 0) _totalConsumptionW += w;
            }

            _batteries.Clear();
            if (GridLogic != null)
                _batteries.AddRange(GridLogic.GetBatteries());

            if (_batteries.Count > 0)
            {
                const float eps = 0.001f;
                float sumRatio = 0f;
                float totalStored = 0f;
                float totalMax = 0f;
                float netIn = 0f;
                float netOut = 0f;

                for (int i = 0; i < _batteries.Count; i++)
                {
                    var b = _batteries[i];
                    float r = b.MaxStoredPower > 0f
                        ? Math.Max(0f, Math.Min(1f, b.CurrentStoredPower / b.MaxStoredPower))
                        : 0f;
                    sumRatio += r;
                    totalStored += b.CurrentStoredPower;
                    totalMax += b.MaxStoredPower;
                    netIn += b.CurrentInput;
                    netOut += b.CurrentOutput;
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

        // -----------------------------------------------------------------------
        // Ring buffer sampling
        // -----------------------------------------------------------------------

        float GetWindowSeconds()
        {
            if (AppConfig == null) return 30f;
            int idx = Math.Max(0, Math.Min(AppConfig.GraphWindowIndex, WindowOptions.Length - 1));
            return WindowOptions[idx];
        }

        void TryAddSample()
        {
            float windowSeconds = GetWindowSeconds();

            if (Math.Abs(windowSeconds - _lastWindowSeconds) > 0.01f)
            {
                _sampleCount = 0;
                _sampleHead = 0;
                _lastSampleTime = -999f;
                _lastWindowSeconds = windowSeconds;
            }

            float now = 0f;
            try
            {
                var sess = MyAPIGateway.Session;
                if (sess != null) now = (float)sess.ElapsedPlayTime.TotalSeconds;
            }
            catch
            {
                return;
            }

            float interval = windowSeconds / GRAPH_POINTS;
            if (now - _lastSampleTime < interval) return;

            double totalProd = _solar.CurrentW + _wind.CurrentW + _reactor.CurrentW + _engine.CurrentW;

            _samples[_sampleHead] = new Sample
            {
                TimeS = now,
                ProductionW = totalProd,
                ConsumptionW = _totalConsumptionW
            };
            _sampleHead = (_sampleHead + 1) % GRAPH_POINTS;
            if (_sampleCount < GRAPH_POINTS) _sampleCount++;
            _lastSampleTime = now;
        }

        // -----------------------------------------------------------------------
        // Top-level layout
        // -----------------------------------------------------------------------

        void DrawDashboard(List<MySprite> sprites)
        {
            float margin = 0f;
            float xLeft = ViewBox.X + margin;
            float xRight = ViewBox.Right - margin;
            float contentW = xRight - xLeft;
            float gapH = 5f * Scale;
            float rowH = 21f * Scale;
            float bigBarH = 28f * Scale;
            float divH = Math.Max(1f, Scale);
            float batH = rowH * 2.4f;

            int prodRows = 0;
            if (_solar.MaxW > 0) prodRows++;
            if (_wind.MaxW > 0) prodRows++;
            if (_reactor.MaxW > 0) prodRows++;
            if (_engine.MaxW > 0) prodRows++;
            if (_batteryProd.MaxW > 0) prodRows++;

            float yBot = ViewBox.Bottom - margin;
            float y = CaretY + gapH;

            // Section A: power balance bar
            float secAh = rowH + bigBarH + rowH;
            DrawPowerBalanceSection(sprites, xLeft, xRight, contentW, y, bigBarH, rowH);
            y += secAh + gapH;

            DrawDivider(sprites, xLeft, xRight, y);
            y += divH + gapH;

            // Section B: production rows
            if (prodRows > 0)
            {
                y += gapH;
                DrawProductionSection(sprites, xLeft, contentW, y, rowH);
                y += prodRows * rowH + gapH;

                DrawDivider(sprites, xLeft, xRight, y);
                y += divH + gapH;
            }

            // Section C+D: two line graphs (use remaining height minus battery row)
            float graphAreaH = yBot - y - batH - gapH - divH - gapH;
            if (graphAreaH > 30f * Scale)
            {
                float singleH = (graphAreaH - gapH) / 2f;
                DrawLineGraph(sprites, xLeft, contentW, y, singleH, true);
                y += singleH + gapH;
                DrawLineGraph(sprites, xLeft, contentW, y, singleH, false);
                y += singleH + gapH;

                DrawDivider(sprites, xLeft, xRight, y);
                y += divH + gapH;
            }

            // Section E: battery summary
            if (y + batH <= yBot + 1f)
                DrawBatterySection(sprites, xLeft, xRight, contentW, y, batH);
        }

        // -----------------------------------------------------------------------
        // Section A: power balance bar
        // -----------------------------------------------------------------------

        void DrawPowerBalanceSection(List<MySprite> sprites, float xLeft, float xRight, float contentW,
            float y, float bigBarH, float rowH)
        {
            Color fg = Surface.ScriptForegroundColor;
            float ts = Scale * 0.72f * FontScale;

            string consumeLabel = "Consumo atual: " + FormatingHelper.WattsToString(_totalConsumptionW);
            string capLabel = "Capacidade max: " + FormatingHelper.WattsToString(_totalMaxW);

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
                RotationOrScale = ts, Color = fg,
                Alignment = TextAlignment.RIGHT, FontId = "White"
            });

            y += rowH;

            float ratio = _totalMaxW > 0 ? (float)Math.Min(1.0, _totalConsumptionW / _totalMaxW) : 0f;
            Color barBg = new Color(fg.R, fg.G, fg.B, 25);
            Color barFill = GetLoadColor(ratio);
            float barCx = xLeft + contentW / 2f;
            float barCy = y + bigBarH / 2f;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(barCx, barCy), Size = new Vector2(contentW, bigBarH),
                Color = barBg, Alignment = TextAlignment.CENTER
            });

            if (ratio > 0.005f)
            {
                float fillW = contentW * ratio;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(xLeft + fillW / 2f, barCy), Size = new Vector2(fillW, bigBarH),
                    Color = barFill, Alignment = TextAlignment.CENTER
                });
            }

            string pctText = FormatingHelper.PercentageToString(ratio);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = pctText,
                Position = new Vector2(barCx, y + bigBarH * 0.08f),
                RotationOrScale = Scale * 0.82f * FontScale, Color = fg,
                Alignment = TextAlignment.CENTER, FontId = "White"
            });

            y += bigBarH;

            double totalProd = _solar.CurrentW + _wind.CurrentW + _reactor.CurrentW
                               + _engine.CurrentW + _batteryProd.CurrentW;
            string prodLabel = "Produção: " + FormatingHelper.WattsToString(totalProd);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = prodLabel,
                Position = new Vector2(barCx, y),
                RotationOrScale = ts, Color = fg,
                Alignment = TextAlignment.CENTER, FontId = "White"
            });
        }

        // -----------------------------------------------------------------------
        // Section B: production breakdown
        // -----------------------------------------------------------------------

        void DrawProductionSection(List<MySprite> sprites, float xLeft, float contentW,
            float y, float rowH)
        {
            Color fg = Surface.ScriptForegroundColor;
            Color accent = AppConfig.HeaderColor;
            float labelW = contentW * 0.24f;
            float barW = contentW * 0.54f;
            float numW = contentW - labelW - barW;

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
            float ratio = cat.MaxW > 0 ? (float)Math.Min(1.0, cat.CurrentW / cat.MaxW) : 0f;
            float rowCy = y + rowH / 2f;
            float ts = Scale * 0.68f * FontScale;
            float tsBar = Scale * 0.62f * FontScale;
            float barH = rowH * 0.82f;
            Color barBg = new Color(fg.R, fg.G, fg.B, 25);
            float barXLeft = xLeft + labelW;

            // Source label (left column)
            Vector2 labelSz = FormatingHelper.GetSizeInPixel(label, "White", ts, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = label,
                Position = new Vector2(xLeft, rowCy - labelSz.Y / 2f),
                RotationOrScale = ts, Color = fg,
                Alignment = TextAlignment.LEFT, FontId = "White"
            });

            // Bar background
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(barXLeft + barW / 2f, rowCy),
                Size = new Vector2(barW, barH), Color = barBg, Alignment = TextAlignment.CENTER
            });

            // Bar fill
            if (ratio > 0.005f)
            {
                float fillW = barW * ratio;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(barXLeft + fillW / 2f, rowCy),
                    Size = new Vector2(fillW, barH), Color = accent, Alignment = TextAlignment.CENTER
                });
            }

            // Current production — centred inside the bar
            string curText = FormatingHelper.WattsToString(cat.CurrentW);
            Vector2 curSz = FormatingHelper.GetSizeInPixel(curText, "White", tsBar, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = curText,
                Position = new Vector2(barXLeft + barW / 2f, rowCy - curSz.Y / 2f),
                RotationOrScale = tsBar, Color = fg,
                Alignment = TextAlignment.CENTER, FontId = "White"
            });

            // Max capacity — outside, right of bar (dimmed)
            string maxText = FormatingHelper.WattsToString(cat.MaxW);
            Vector2 maxSz = FormatingHelper.GetSizeInPixel(maxText, "White", ts, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = maxText,
                Position = new Vector2(barXLeft + barW + numW, rowCy - maxSz.Y / 2f),
                RotationOrScale = ts, Color = new Color(fg.R, fg.G, fg.B, 170),
                Alignment = TextAlignment.RIGHT, FontId = "White"
            });
        }

        // -----------------------------------------------------------------------
        // Sections C+D: line graphs
        // -----------------------------------------------------------------------

        void DrawLineGraph(List<MySprite> sprites, float xLeft, float contentW,
            float y, float height, bool isProduction)
        {
            Color fg = Surface.ScriptForegroundColor;
            Color accent = AppConfig.HeaderColor;
            Color warn = AppConfig.WarningColor;
            Color lineColor = isProduction ? accent : warn;
            float ts = Scale * 0.62f * FontScale;

            string label = isProduction ? "Produção de energia" : "Consumo de energia";
            float labelH = FormatingHelper.GetSizeInPixel(label, "White", ts, Surface).Y;

            // ── scan samples to find max value ──
            float nowTime = GetCurrentTime();
            float windowSecs = GetWindowSeconds();
            float windowStart = nowTime - windowSecs;

            double maxData = 0;
            int validCnt = 0;
            if (_sampleCount >= 2)
            {
                for (int i = 0; i < _sampleCount; i++)
                {
                    int idx = (_sampleHead - _sampleCount + i + GRAPH_POINTS) % GRAPH_POINTS;
                    var s = _samples[idx];
                    if (s.TimeS < windowStart) continue;
                    double v = isProduction ? s.ProductionW : s.ConsumptionW;
                    if (v > maxData) maxData = v;
                    validCnt++;
                }
            }

            // ── nice-step Y axis ──
            double step = CalcNiceStep(maxData > 0 ? maxData : 1.0, 4);
            double axisMax = Math.Ceiling(maxData / step) * step;
            if (axisMax < step) axisMax = step;
            int numSteps = (int)Math.Round(axisMax / step);

            // ── axis column width based on widest label ──
            string topLabel = FormatAxisWatts(axisMax);
            float axisW = FormatingHelper.GetSizeInPixel(topLabel, "White", ts, Surface).X + 4f * Scale;

            float plotXLeft = xLeft + axisW;
            float plotW = Math.Max(1f, contentW - axisW);
            float plotY = y + labelH + 2f * Scale;
            float plotH = Math.Max(4f, height - labelH - 2f * Scale);

            // ── background ──
            Color graphBg = new Color(fg.R, fg.G, fg.B, 12);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(plotXLeft + plotW / 2f, plotY + plotH / 2f),
                Size = new Vector2(plotW, plotH),
                Color = graphBg, Alignment = TextAlignment.CENTER
            });

            // ── title label ──
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = label,
                Position = new Vector2(plotXLeft, y),
                RotationOrScale = ts, Color = lineColor,
                Alignment = TextAlignment.LEFT, FontId = "White"
            });

            // ── Y-axis labels + horizontal grid lines ──
            Color axisColor = new Color(fg.R, fg.G, fg.B, 170);
            Color gridColor = new Color(fg.R, fg.G, fg.B, 18);
            float labelHHalf = FormatingHelper.GetSizeInPixel("0", "White", ts, Surface).Y / 2f;

            for (int si = 0; si <= numSteps; si++)
            {
                double v = si * step;
                float lineY = plotY + plotH - (float)(v / axisMax) * plotH;
                lineY = Math.Max(plotY, Math.Min(plotY + plotH, lineY));

                // Grid line across plot area (skip baseline — just background)
                if (si > 0)
                {
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE, Data = "SquareSimple",
                        Position = new Vector2(plotXLeft + plotW / 2f, lineY),
                        Size = new Vector2(plotW, Math.Max(1f, Scale * 0.5f)),
                        Color = gridColor, Alignment = TextAlignment.CENTER
                    });
                }

                // Y-axis label (right-aligned into the axis column)
                string lbl = FormatAxisWatts(v);
                float lblY = lineY - labelHHalf;
                lblY = Math.Max(plotY, Math.Min(plotY + plotH - labelHHalf * 2f, lblY));
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT, Data = lbl,
                    Position = new Vector2(xLeft + axisW - 2f * Scale, lblY),
                    RotationOrScale = ts,
                    Color = si == 0 ? new Color(fg.R, fg.G, fg.B, 110) : axisColor,
                    Alignment = TextAlignment.RIGHT, FontId = "White"
                });
            }

            if (validCnt < 2 || maxData < 1.0) return;

            // ── plot line segments ──
            float prevX = 0f, prevY = 0f;
            bool hasPrev = false;
            float lineThickness = Math.Max(1.5f, Scale * 1.5f);

            for (int i = 0; i < _sampleCount; i++)
            {
                int idx = (_sampleHead - _sampleCount + i + GRAPH_POINTS) % GRAPH_POINTS;
                var s = _samples[idx];
                if (s.TimeS < windowStart)
                {
                    hasPrev = false;
                    continue;
                }

                double val = isProduction ? s.ProductionW : s.ConsumptionW;
                float px = plotXLeft + (s.TimeS - windowStart) / windowSecs * plotW;
                float py = plotY + plotH - (float)(val / axisMax) * plotH;
                py = Math.Max(plotY, Math.Min(plotY + plotH, py));

                if (hasPrev)
                    DrawLineSegment(sprites, new Vector2(prevX, prevY), new Vector2(px, py),
                        lineThickness, lineColor);

                prevX = px;
                prevY = py;
                hasPrev = true;
            }
        }

        // Returns a "nice" step value for ~targetDivisions equal intervals up to maxVal.
        // Steps follow the 1-2-5 pattern (e.g. 10, 20, 50, 100, 200, 500, 1000 ...).
        static double CalcNiceStep(double maxVal, int targetDivisions)
        {
            if (maxVal <= 0 || targetDivisions <= 0) return 1.0;

            double rawStep = maxVal / targetDivisions;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            double norm = rawStep / mag;

            double niceNorm;
            if (norm <= 1.0) niceNorm = 1;
            else if (norm <= 2.0) niceNorm = 2;
            else if (norm <= 5.0) niceNorm = 5;
            else niceNorm = 10;

            return niceNorm * mag;
        }

        // Formats a watt value as an integer with the appropriate unit (W / kW / MW).
        static string FormatAxisWatts(double watts)
        {
            if (watts <= 0) return "0";
            if (watts >= 1000000.0)
                return ((int)Math.Round(watts / 1000000.0)).ToString() + " MW";
            if (watts >= 1000.0)
                return ((int)Math.Round(watts / 1000.0)).ToString() + " kW";
            return ((int)Math.Round(watts)).ToString() + " W";
        }

        static void DrawLineSegment(List<MySprite> sprites, Vector2 p1, Vector2 p2,
            float thickness, Color color)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f) return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2((p1.X + p2.X) / 2f, (p1.Y + p2.Y) / 2f),
                Size = new Vector2(len, thickness),
                RotationOrScale = (float)Math.Atan2(dy, dx),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        static float GetCurrentTime()
        {
            try
            {
                var sess = MyAPIGateway.Session;
                return sess != null ? (float)sess.ElapsedPlayTime.TotalSeconds : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        // -----------------------------------------------------------------------
        // Section E: battery summary (horizontal icon + avg% + time)
        // -----------------------------------------------------------------------

        void DrawBatterySection(List<MySprite> sprites, float xLeft, float xRight, float contentW,
            float y, float sectionH)
        {
            Color fg = Surface.ScriptForegroundColor;
            Color iconColor = GetBatteryIconColor(_avgBatteryCharge);
            float cy = y + sectionH / 2f;
            float ts = Scale * 0.76f * FontScale;
            float tsSmall = Scale * 0.63f * FontScale;

            // Wide horizontal battery icon (38% of content width)
            float bodyH = sectionH * 0.72f;
            float bodyW = contentW * 0.38f;
            float iconCx = xLeft + bodyW / 2f + 2f * Scale;
            float pctScale = Math.Min(Scale * 0.90f * FontScale, bodyH * 0.55f / 14f);

            DrawHorizontalBatteryIcon(sprites, Surface, new Vector2(iconCx, cy),
                bodyW, bodyH, _avgBatteryCharge, iconColor, fg, pctScale);

            // State + time (right-aligned, two lines)
            string stateWord = _isCharging ? "Carregando" : "Descarregando";
            string stateText = stateWord + " — " + _timeLabel;
            Vector2 stSz = FormatingHelper.GetSizeInPixel(stateText, "White", ts, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = stateText,
                Position = new Vector2(xRight, cy - stSz.Y - 1f * Scale),
                RotationOrScale = ts, Color = fg,
                Alignment = TextAlignment.RIGHT, FontId = "White"
            });

            int batCount = _batteries.Count;
            string countText = batCount + (batCount == 1 ? " bateria" : " baterias");
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = countText,
                Position = new Vector2(xRight, cy + 1f * Scale),
                RotationOrScale = tsSmall, Color = new Color(fg.R, fg.G, fg.B, 170),
                Alignment = TextAlignment.RIGHT, FontId = "White"
            });
        }

        // -----------------------------------------------------------------------
        // Horizontal battery icon (rotated 90° — body is landscape)
        //   nub on right, fill from left to right, % text centred inside
        // -----------------------------------------------------------------------

        static void DrawHorizontalBatteryIcon(List<MySprite> sprites, Sandbox.ModAPI.Ingame.IMyTextSurface surf,
            Vector2 center, float bodyW, float bodyH, float ratio,
            Color fillColor, Color borderColor, float textScale)
        {
            const float border = 3f;
            var emptyBg = new Color(borderColor.R, borderColor.G, borderColor.B, 40);

            // Body background
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = center, Size = new Vector2(bodyW, bodyH),
                Color = emptyBg, Alignment = TextAlignment.CENTER
            });

            // Fill — left to right
            if (ratio > 0.005f)
            {
                float innerW = bodyW - border * 2f;
                float fillW = innerW * ratio;
                float fillCx = center.X - innerW / 2f + fillW / 2f;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(fillCx, center.Y),
                    Size = new Vector2(fillW, bodyH - border * 2f),
                    Color = fillColor, Alignment = TextAlignment.CENTER
                });
            }

            // Nub on the right side
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(center.X + bodyW / 2f + bodyW * 0.05f, center.Y),
                Size = new Vector2(bodyW * 0.07f, bodyH * 0.38f),
                Color = borderColor, Alignment = TextAlignment.CENTER
            });

            // Border — top, bottom, left, right
            float bw = Math.Max(1f, border * 0.8f);
            float halfW = bodyW / 2f;
            float halfH = bodyH / 2f;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(center.X, center.Y - halfH),
                Size = new Vector2(bodyW, bw), Color = borderColor, Alignment = TextAlignment.CENTER
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(center.X, center.Y + halfH),
                Size = new Vector2(bodyW, bw), Color = borderColor, Alignment = TextAlignment.CENTER
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(center.X - halfW, center.Y),
                Size = new Vector2(bw, bodyH), Color = borderColor, Alignment = TextAlignment.CENTER
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(center.X + halfW, center.Y),
                Size = new Vector2(bw, bodyH), Color = borderColor, Alignment = TextAlignment.CENTER
            });

            // Percentage text centred inside body
            string pct = FormatingHelper.PercentageToString(ratio);
            Vector2 pctSz = FormatingHelper.GetSizeInPixel(pct, "White", textScale, surf);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = pct,
                Position = new Vector2(center.X, center.Y - pctSz.Y / 2f),
                RotationOrScale = textScale, Color = borderColor,
                Alignment = TextAlignment.CENTER, FontId = "White"
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
            if (ratio >= 0.90f) return AppConfig.ErrorColor;
            if (ratio >= 0.70f) return AppConfig.WarningColor;
            return AppConfig.HeaderColor;
        }

        Color GetBatteryIconColor(float ratio)
        {
            if (ratio < 0.15f) return AppConfig.ErrorColor;
            if (ratio < 0.35f) return AppConfig.WarningColor;
            return AppConfig.HeaderColor;
        }

        static string FormatTimeHours(float hours)
        {
            if (hours < 0f) return "--";
            if (hours > 99.99f) return ">99h";
            int h = (int)hours;
            int m = (int)((hours - h) * 60f);
            return h > 0 ? h.ToString() + "h " + m.ToString() + "m" : m.ToString() + "m";
        }
    }
}
