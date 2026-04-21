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

        // Graph time window options in seconds
        static readonly float[] WindowOptions = { 1f, 5f, 30f, 60f, 300f };

        // Ring buffer for the two line graphs
        const int GRAPH_POINTS = 90;

        struct Sample
        {
            public float  TimeS;
            public double ProductionW;
            public double ConsumptionW;
        }

        readonly Sample[] _samples  = new Sample[GRAPH_POINTS];
        int   _sampleHead           = 0;
        int   _sampleCount          = 0;
        float _lastSampleTime       = -999f;
        float _lastWindowSeconds    = -1f;

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
        float  _avgBatteryCharge;
        bool   _isCharging;
        string _timeLabel = "--";

        readonly List<IMyPowerProducer> _producers = new List<IMyPowerProducer>();
        readonly List<IMyTerminalBlock> _terminals  = new List<IMyTerminalBlock>();
        readonly List<IMyBatteryBlock>  _batteries  = new List<IMyBatteryBlock>();

        public EnergyDashboardSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size) { }

        // -----------------------------------------------------------------------
        // Main loop
        // -----------------------------------------------------------------------

        public override void Run()
        {
            base.Run();
            if (Config == null) return;

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
                        _batteryProd.MaxW     += max;
                    }
                    else if (prod is IMySolarPanel)
                    {
                        _solar.CurrentW += cur;
                        _solar.MaxW     += max;
                    }
                    else if (prod is IMyWindTurbine)
                    {
                        _wind.CurrentW += cur;
                        _wind.MaxW     += max;
                    }
                    else if (prod is IMyReactor)
                    {
                        _reactor.CurrentW += cur;
                        _reactor.MaxW     += max;
                    }
                    else
                    {
                        try
                        {
                            var tid = prod.BlockDefinition.TypeIdString ?? string.Empty;
                            if (tid.EndsWith("HydrogenEngine", StringComparison.OrdinalIgnoreCase))
                            {
                                _engine.CurrentW += cur;
                                _engine.MaxW     += max;
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
                const float eps   = 0.001f;
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

        // -----------------------------------------------------------------------
        // Ring buffer sampling
        // -----------------------------------------------------------------------

        float GetWindowSeconds()
        {
            if (Config == null) return 30f;
            int idx = Math.Max(0, Math.Min(Config.GraphWindowIndex, WindowOptions.Length - 1));
            return WindowOptions[idx];
        }

        void TryAddSample()
        {
            float windowSeconds = GetWindowSeconds();

            if (Math.Abs(windowSeconds - _lastWindowSeconds) > 0.01f)
            {
                _sampleCount      = 0;
                _sampleHead       = 0;
                _lastSampleTime   = -999f;
                _lastWindowSeconds = windowSeconds;
            }

            float now = 0f;
            try
            {
                var sess = MyAPIGateway.Session;
                if (sess != null) now = (float)sess.ElapsedPlayTime.TotalSeconds;
            }
            catch { return; }

            float interval = windowSeconds / GRAPH_POINTS;
            if (now - _lastSampleTime < interval) return;

            double totalProd = _solar.CurrentW + _wind.CurrentW + _reactor.CurrentW
                             + _engine.CurrentW + _batteryProd.CurrentW;

            _samples[_sampleHead] = new Sample
            {
                TimeS       = now,
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
            float margin   = ViewBox.Width * Margin;
            float xLeft    = ViewBox.X + margin;
            float xRight   = ViewBox.Right - margin;
            float contentW = xRight - xLeft;
            float gapH     = 5f * Scale;
            float rowH     = 21f * Scale;
            float bigBarH  = 28f * Scale;
            float divH     = Math.Max(1f, Scale);
            float batH     = rowH * 1.9f;

            int prodRows = 0;
            if (_solar.MaxW > 0)       prodRows++;
            if (_wind.MaxW > 0)        prodRows++;
            if (_reactor.MaxW > 0)     prodRows++;
            if (_engine.MaxW > 0)      prodRows++;
            if (_batteryProd.MaxW > 0) prodRows++;

            float yBot = ViewBox.Bottom - margin;
            float y    = CaretY + gapH;

            // Section A: power balance bar
            float secAH = rowH + bigBarH + rowH;
            DrawPowerBalanceSection(sprites, xLeft, xRight, contentW, y, bigBarH, rowH);
            y += secAH + gapH;

            DrawDivider(sprites, xLeft, xRight, y);
            y += divH + gapH;

            // Section B: production rows
            if (prodRows > 0)
            {
                DrawProductionSection(sprites, xLeft, xRight, contentW, y, rowH);
                y += prodRows * rowH + gapH;

                DrawDivider(sprites, xLeft, xRight, y);
                y += divH + gapH;
            }

            // Section C+D: two line graphs (use remaining height minus battery row)
            float graphAreaH = yBot - y - batH - gapH - divH - gapH;
            if (graphAreaH > 30f * Scale)
            {
                float singleH = (graphAreaH - gapH) / 2f;
                DrawLineGraph(sprites, xLeft, xRight, contentW, y, singleH, true);
                y += singleH + gapH;
                DrawLineGraph(sprites, xLeft, xRight, contentW, y, singleH, false);
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
            Color fg     = Surface.ScriptForegroundColor;
            Color accent = Config.HeaderColor;
            float ts     = Scale * 0.72f;

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

            float ratio   = _totalMaxW > 0 ? (float)Math.Min(1.0, _totalConsumptionW / _totalMaxW) : 0f;
            Color barBg   = new Color(fg.R, fg.G, fg.B, 25);
            Color barFill = GetLoadColor(ratio);
            float barCX   = xLeft + contentW / 2f;
            float barCY   = y + bigBarH / 2f;

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

            string pctText = FormatingHelper.PercentageToString(ratio);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = pctText,
                Position = new Vector2(barCX, y + bigBarH * 0.08f),
                RotationOrScale = Scale * 0.82f, Color = fg,
                Alignment = TextAlignment.CENTER, FontId = "White"
            });

            y += bigBarH;

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
            float labelW = contentW * 0.28f;
            float barW   = contentW * 0.47f;
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
            float barH     = rowH * 0.50f;
            Color barBg    = new Color(fg.R, fg.G, fg.B, 25);
            float barXLeft = xLeft + labelW;

            Vector2 labelSz = GetSizeInPixel(label, "White", ts, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = label,
                Position = new Vector2(xLeft, rowCY - labelSz.Y / 2f),
                RotationOrScale = ts, Color = fg,
                Alignment = TextAlignment.LEFT, FontId = "White"
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(barXLeft + barW / 2f, rowCY),
                Size = new Vector2(barW, barH), Color = barBg, Alignment = TextAlignment.CENTER
            });

            if (ratio > 0.005f)
            {
                float fillW = barW * ratio;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(barXLeft + fillW / 2f, rowCY),
                    Size = new Vector2(fillW, barH), Color = accent, Alignment = TextAlignment.CENTER
                });
            }

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
        // Sections C+D: line graphs
        // -----------------------------------------------------------------------

        void DrawLineGraph(List<MySprite> sprites, float xLeft, float xRight, float contentW,
            float y, float height, bool isProduction)
        {
            Color fg     = Surface.ScriptForegroundColor;
            Color accent = Config.HeaderColor;
            Color warn   = Config.WarningColor;

            string label   = isProduction ? "Produção" : "Consumo";
            Color lineColor = isProduction ? accent : warn;
            float ts       = Scale * 0.65f;
            float labelH   = GetSizeInPixel(label, "White", ts, Surface).Y;

            // Background
            Color graphBg = new Color(fg.R, fg.G, fg.B, 12);
            float plotY   = y + labelH + 2f * Scale;
            float plotH   = Math.Max(4f, height - labelH - 2f * Scale);
            float plotW   = contentW;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple",
                Position = new Vector2(xLeft + plotW / 2f, plotY + plotH / 2f),
                Size = new Vector2(plotW, plotH),
                Color = graphBg, Alignment = TextAlignment.CENTER
            });

            // Label top-left
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = label,
                Position = new Vector2(xLeft, y),
                RotationOrScale = ts, Color = lineColor,
                Alignment = TextAlignment.LEFT, FontId = "White"
            });

            if (_sampleCount < 2)
                return;

            float nowTime     = GetCurrentTime();
            float windowSecs  = GetWindowSeconds();
            float windowStart = nowTime - windowSecs;

            // Collect samples within time window
            double maxVal = 0;
            int validCount = 0;

            for (int i = 0; i < _sampleCount; i++)
            {
                int idx = (_sampleHead - _sampleCount + i + GRAPH_POINTS) % GRAPH_POINTS;
                var s = _samples[idx];
                if (s.TimeS < windowStart) continue;

                double val = isProduction ? s.ProductionW : s.ConsumptionW;
                if (val > maxVal) maxVal = val;
                validCount++;
            }

            if (validCount < 2 || maxVal < 1.0) return;

            // Draw max-value label (top right)
            string maxLabel = FormatingHelper.WattsToString(maxVal);
            Vector2 maxSz   = GetSizeInPixel(maxLabel, "White", ts, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = maxLabel,
                Position = new Vector2(xRight, plotY),
                RotationOrScale = ts, Color = new Color(fg.R, fg.G, fg.B, 160),
                Alignment = TextAlignment.RIGHT, FontId = "White"
            });

            // Plot line segments
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

                double val  = isProduction ? s.ProductionW : s.ConsumptionW;
                float  px   = xLeft + (s.TimeS - windowStart) / windowSecs * plotW;
                float  py   = plotY + plotH - (float)(val / maxVal) * plotH;
                py = Math.Max(plotY, Math.Min(plotY + plotH, py));

                if (hasPrev)
                    DrawLineSegment(sprites, new Vector2(prevX, prevY), new Vector2(px, py),
                        lineThickness, lineColor);

                prevX   = px;
                prevY   = py;
                hasPrev = true;
            }
        }

        static void DrawLineSegment(List<MySprite> sprites, Vector2 p1, Vector2 p2,
            float thickness, Color color)
        {
            float dx  = p2.X - p1.X;
            float dy  = p2.Y - p1.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f) return;

            sprites.Add(new MySprite
            {
                Type            = SpriteType.TEXTURE,
                Data            = "SquareSimple",
                Position        = new Vector2((p1.X + p2.X) / 2f, (p1.Y + p2.Y) / 2f),
                Size            = new Vector2(len, thickness),
                RotationOrScale = (float)Math.Atan2(dy, dx),
                Color           = color,
                Alignment       = TextAlignment.CENTER
            });
        }

        static float GetCurrentTime()
        {
            try
            {
                var sess = MyAPIGateway.Session;
                return sess != null ? (float)sess.ElapsedPlayTime.TotalSeconds : 0f;
            }
            catch { return 0f; }
        }

        // -----------------------------------------------------------------------
        // Section E: battery summary (horizontal icon + avg% + time)
        // -----------------------------------------------------------------------

        void DrawBatterySection(List<MySprite> sprites, float xLeft, float xRight, float contentW,
            float y, float sectionH)
        {
            Color fg        = Surface.ScriptForegroundColor;
            Color accent    = Config.HeaderColor;
            Color iconColor = GetBatteryIconColor(_avgBatteryCharge);
            float cy        = y + sectionH / 2f;
            float ts        = Scale * 0.78f;

            // Horizontal battery icon: body lies on its side
            float bodyH = sectionH * 0.62f;
            float bodyW = bodyH * 2.8f;
            float iconCX = xLeft + bodyW / 2f + 2f * Scale;

            DrawHorizontalBatteryIcon(sprites, Surface, new Vector2(iconCX, cy),
                bodyW, bodyH, _avgBatteryCharge, iconColor, fg, Scale * 0.65f);

            float textX = iconCX + bodyW / 2f + 6f * Scale;

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
                float fillW  = innerW * ratio;
                float fillCX = center.X - innerW / 2f + fillW / 2f;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(fillCX, center.Y),
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

            // Percentage text centred inside body
            string pct    = FormatingHelper.PercentageToString(ratio);
            Vector2 pctSz = GetSizeInPixel(pct, "White", textScale, surf);
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
