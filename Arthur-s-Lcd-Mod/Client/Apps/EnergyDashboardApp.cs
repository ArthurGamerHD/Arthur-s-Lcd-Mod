using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Grid;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using ScreenConfigPower = LcdMod.Common.Config.Models.Apps.ScreenConfigPower;

namespace LcdMod.Client.Apps
{
    internal sealed class EnergyDashboardApp : AppBase
    {
        static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        static readonly float[] WindowOptions = { 1f, 5f, 30f, 60f, 300f };
        const int GRAPH_POINTS = 90;

        struct Sample
        {
            public float TimeS;
            public double ProductionW;
            public double ConsumptionW;
        }

        struct Category
        {
            public double CurrentW;
            public double MaxW;
        }

        readonly Sample[] _samples = new Sample[GRAPH_POINTS];
        int _sampleHead;
        int _sampleCount;
        float _lastSampleTime = -999f;
        float _lastWindowSeconds = -1f;

        Category _solar;
        Category _wind;
        Category _reactor;
        Category _engine;
        Category _batteryProd;
        double _totalMaxW;
        double _totalConsumptionW;
        float _avgBatteryCharge;
        bool _isCharging;
        string _timeLabel = string.Empty;

        readonly List<IMyPowerProducer> _producers = new List<IMyPowerProducer>();
        readonly List<IMyTerminalBlock> _terminals = new List<IMyTerminalBlock>();
        readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        ScreenConfigPower _config;
        public ScreenConfigPower Config => _config;

        public EnergyDashboardApp(ScreenConfigPower config, IAppHost host) : base(config, host)
        {
            _config = config;
        }

        public override void Update()
        {
            CollectData(Host.GridLogic);
            TryAddSample();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            DrawDashboard(sprites);
            return sprites;
        }

        void CollectData(GridLogic gridLogic)
        {
            var owner = Host;

            _solar = new Category();
            _wind = new Category();
            _reactor = new Category();
            _engine = new Category();
            _batteryProd = new Category();
            _totalConsumptionW = 0;

            _producers.Clear();
            if (gridLogic != null)
                _producers.AddRange(gridLogic.GetTerminalBlocks<IMyPowerProducer>());

            for (int i = 0; i < _producers.Count; i++)
            {
                var prod = _producers[i];
                try
                {
                    double cur = MegaWattsToWatts(prod.CurrentOutput);
                    double max = MegaWattsToWatts(prod.MaxOutput);
                    if (prod is IMyBatteryBlock)
                    {
                        _batteryProd.CurrentW += cur; _batteryProd.MaxW += max;
                    }
                    else if (prod is IMySolarPanel)
                    {
                        _solar.CurrentW += cur; _solar.MaxW += max;
                    }
                    else if (prod is IMyWindTurbine)
                    {
                        _wind.CurrentW += cur; _wind.MaxW += max;
                    }
                    else if (prod is IMyReactor)
                    {
                        _reactor.CurrentW += cur; _reactor.MaxW += max;
                    }
                    else
                    {
                        try
                        {
                            var tid = prod.BlockDefinition.TypeIdString ?? string.Empty;
                            if (tid.EndsWith("HydrogenEngine", StringComparison.OrdinalIgnoreCase))
                            {
                                _engine.CurrentW += cur; _engine.MaxW += max;
                            }
                        }
                        catch (Exception e)
                        {
                            ErrorHandlerHelper.LogError(e, owner);
                        }
                    }
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, owner);
                }
            }

            _totalMaxW = _solar.MaxW + _wind.MaxW + _reactor.MaxW + _engine.MaxW + _batteryProd.MaxW;

            _terminals.Clear();
            if (gridLogic != null)
                _terminals.AddRange(gridLogic.GetTerminalBlocks<IMyTerminalBlock>());

            for (int i = 0; i < _terminals.Count; i++)
            {
                if (_terminals[i] is IMyPowerProducer)
                    continue;

                MyResourceSinkComponent sink = null;
                try
                {
                    _terminals[i].Components.TryGet(out sink);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, owner);
                }

                if (sink == null)
                    continue;

                double w = 0;
                try
                {
                    w = MegaWattsToWatts(sink.CurrentInputByType(ElectricityId));
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, owner);
                }

                if (w > 0)
                    _totalConsumptionW += w;
            }

            _batteries.Clear();
            if (gridLogic != null)
                _batteries.AddRange(gridLogic.GetTerminalBlocks<IMyBatteryBlock>());

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
                    _timeLabel = LocHelper.GetLoc("LcdMod_NotAvailable");
                else if (_isCharging)
                    _timeLabel = FormatingHelper.FormatTimeHours((totalMax - totalStored) / netRate);
                else
                    _timeLabel = FormatingHelper.FormatTimeHours(totalStored / netRate);
            }
            else
            {
                _avgBatteryCharge = 0f;
                _isCharging = false;
                _timeLabel = LocHelper.GetLoc("LcdMod_NotAvailable");
            }
        }

        float GetWindowSeconds()
        {
            if (_config == null) return 30f;
            int idx = Math.Max(0, Math.Min(_config.GraphWindowIndex, WindowOptions.Length - 1));
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

            float now;
            try
            {
                var sess = MyAPIGateway.Session;
                now = sess != null ? (float)sess.ElapsedPlayTime.TotalSeconds : 0f;
            }
            catch
            {
                return;
            }

            float interval = windowSeconds / GRAPH_POINTS;
            if (now - _lastSampleTime < interval)
                return;

            double totalProd = _solar.CurrentW + _wind.CurrentW + _reactor.CurrentW + _engine.CurrentW;
            _samples[_sampleHead] = new Sample { TimeS = now, ProductionW = totalProd, ConsumptionW = _totalConsumptionW };
            _sampleHead = (_sampleHead + 1) % GRAPH_POINTS;
            if (_sampleCount < GRAPH_POINTS) _sampleCount++;
            _lastSampleTime = now;
        }

        void DrawDashboard(List<MySprite> sprites)
        {
            var owner = Host;
            float xLeft = owner.ViewBox.X;
            float xRight = owner.ViewBox.Right;
            float contentW = xRight - xLeft;
            float gapH = 5f * owner.Scale;
            float rowH = 21f * owner.Scale;
            float bigBarH = 28f * owner.Scale;
            float divH = Math.Max(1f, owner.Scale);
            float batH = rowH * 2.4f;

            int prodRows = 0;
            if (_solar.MaxW > 0) prodRows++;
            if (_wind.MaxW > 0) prodRows++;
            if (_reactor.MaxW > 0) prodRows++;
            if (_engine.MaxW > 0) prodRows++;
            if (_batteryProd.MaxW > 0) prodRows++;

            float yBot = owner.ViewBox.Bottom;
            float y = GetContentTop() + gapH;
            float secAh = rowH + bigBarH + rowH;
            DrawPowerBalanceSection(owner, sprites, xLeft, xRight, contentW, y, bigBarH, rowH);
            y += secAh + gapH;
            DrawDivider(owner, sprites, xLeft, xRight, y);
            y += divH + gapH;

            if (prodRows > 0)
            {
                y += gapH;
                DrawProductionSection(owner, sprites, xLeft, contentW, y, rowH);
                y += prodRows * rowH + gapH;
                DrawDivider(owner, sprites, xLeft, xRight, y);
                y += divH + gapH;
            }

            float graphAreaH = yBot - y - batH - gapH - divH - gapH;
            if (graphAreaH > 30f * owner.Scale)
            {
                float singleH = (graphAreaH - gapH) / 2f;
                DrawLineGraph(owner, sprites, xLeft, contentW, y, singleH, true);
                y += singleH + gapH;
                DrawLineGraph(owner, sprites, xLeft, contentW, y, singleH, false);
                y += singleH + gapH;
                DrawDivider(owner, sprites, xLeft, xRight, y);
                y += divH + gapH;
            }

            if (y + batH <= yBot + 1f)
                DrawBatterySection(owner, sprites, xLeft, xRight, contentW, y, batH);
        }

        void DrawPowerBalanceSection(IAppHost owner, List<MySprite> sprites, float xLeft, float xRight, float contentW, float y, float bigBarH, float rowH)
        {
            Color fg = owner.Surface.ScriptForegroundColor;
            float ts = owner.Scale * 0.72f * owner.Surface.FontSize;
            string consumeLabel = FormatLoc("LcdMod_EnergyDashboard_CurrentConsumption", FormatingHelper.WattsToString(_totalConsumptionW));
            string capLabel = FormatLoc("LcdMod_EnergyDashboard_MaxCapacity", FormatingHelper.WattsToString(_totalMaxW));
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = consumeLabel, Position = new Vector2(xLeft, y), RotationOrScale = ts, Color = fg, Alignment = TextAlignment.LEFT, FontId = "White" });
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = capLabel, Position = new Vector2(xRight, y), RotationOrScale = ts, Color = fg, Alignment = TextAlignment.RIGHT, FontId = "White" });
            y += rowH;

            float ratio = _totalMaxW > 0 ? (float)Math.Min(1.0, _totalConsumptionW / _totalMaxW) : 0f;
            Color barBg = new Color(fg.R, fg.G, fg.B, 25);
            Color barFill = GetLoadColor(ratio);
            float barCx = xLeft + contentW / 2f;
            float barCy = y + bigBarH / 2f;

            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(barCx, barCy), Size = new Vector2(contentW, bigBarH), Color = barBg, Alignment = TextAlignment.CENTER });
            if (ratio > 0.005f)
            {
                float fillW = contentW * ratio;
                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(xLeft + fillW / 2f, barCy), Size = new Vector2(fillW, bigBarH), Color = barFill, Alignment = TextAlignment.CENTER });
            }

            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = FormatingHelper.PercentageToString(ratio), Position = new Vector2(barCx, y + bigBarH * 0.08f), RotationOrScale = owner.Scale * 0.82f * owner.Surface.FontSize, Color = fg, Alignment = TextAlignment.CENTER, FontId = "White" });

            y += bigBarH;
            double totalProd = _solar.CurrentW + _wind.CurrentW + _reactor.CurrentW + _engine.CurrentW + _batteryProd.CurrentW;
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = FormatLoc("LcdMod_EnergyDashboard_Production", FormatingHelper.WattsToString(totalProd)), Position = new Vector2(barCx, y), RotationOrScale = ts, Color = fg, Alignment = TextAlignment.CENTER, FontId = "White" });
        }

        void DrawProductionSection(IAppHost owner, List<MySprite> sprites, float xLeft, float contentW, float y, float rowH)
        {
            Color fg = owner.Surface.ScriptForegroundColor;
            Color accent = _config.HeaderColor;
            float labelW = contentW * 0.24f;
            float barW = contentW * 0.54f;
            float numW = contentW - labelW - barW;

            if (_solar.MaxW > 0) { DrawProductionRow(owner, sprites, LocHelper.GetLoc("LcdMod_EnergyDashboard_Solar"), _solar, xLeft, y, labelW, barW, numW, rowH, fg, accent); y += rowH; }
            if (_wind.MaxW > 0) { DrawProductionRow(owner, sprites, LocHelper.GetLoc("LcdMod_EnergyDashboard_Wind"), _wind, xLeft, y, labelW, barW, numW, rowH, fg, accent); y += rowH; }
            if (_reactor.MaxW > 0) { DrawProductionRow(owner, sprites, LocHelper.GetLoc("LcdMod_EnergyDashboard_Reactor"), _reactor, xLeft, y, labelW, barW, numW, rowH, fg, accent); y += rowH; }
            if (_engine.MaxW > 0) { DrawProductionRow(owner, sprites, LocHelper.GetLoc("LcdMod_EnergyDashboard_Engine"), _engine, xLeft, y, labelW, barW, numW, rowH, fg, accent); y += rowH; }
            if (_batteryProd.MaxW > 0) DrawProductionRow(owner, sprites, LocHelper.GetLoc("LcdMod_EnergyDashboard_Battery"), _batteryProd, xLeft, y, labelW, barW, numW, rowH, fg, accent);
        }

        void DrawProductionRow(IAppHost owner, List<MySprite> sprites, string label, Category cat, float xLeft, float y, float labelW, float barW, float numW, float rowH, Color fg, Color accent)
        {
            float ratio = cat.MaxW > 0 ? (float)Math.Min(1.0, cat.CurrentW / cat.MaxW) : 0f;
            float rowCy = y + rowH / 2f;
            float ts = owner.Scale * 0.68f * owner.Surface.FontSize;
            float tsBar = owner.Scale * 0.62f * owner.Surface.FontSize;
            float barH = rowH * 0.82f;
            Color barBg = new Color(fg.R, fg.G, fg.B, 25);
            float barXLeft = xLeft + labelW;

            Vector2 labelSz = FormatingHelper.GetSizeInPixel(label, "White", ts, owner.Surface);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = label, Position = new Vector2(xLeft, rowCy - labelSz.Y / 2f), RotationOrScale = ts, Color = fg, Alignment = TextAlignment.LEFT, FontId = "White" });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(barXLeft + barW / 2f, rowCy), Size = new Vector2(barW, barH), Color = barBg, Alignment = TextAlignment.CENTER });
            if (ratio > 0.005f)
            {
                float fillW = barW * ratio;
                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(barXLeft + fillW / 2f, rowCy), Size = new Vector2(fillW, barH), Color = accent, Alignment = TextAlignment.CENTER });
            }
            string curText = FormatingHelper.WattsToString(cat.CurrentW);
            Vector2 curSz = FormatingHelper.GetSizeInPixel(curText, "White", tsBar, owner.Surface);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = curText, Position = new Vector2(barXLeft + barW / 2f, rowCy - curSz.Y / 2f), RotationOrScale = tsBar, Color = fg, Alignment = TextAlignment.CENTER, FontId = "White" });
            string maxText = FormatingHelper.WattsToString(cat.MaxW);
            Vector2 maxSz = FormatingHelper.GetSizeInPixel(maxText, "White", ts, owner.Surface);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = maxText, Position = new Vector2(barXLeft + barW + numW, rowCy - maxSz.Y / 2f), RotationOrScale = ts, Color = new Color(fg.R, fg.G, fg.B, 170), Alignment = TextAlignment.RIGHT, FontId = "White" });
        }

        void DrawLineGraph(IAppHost owner, List<MySprite> sprites, float xLeft, float contentW, float y, float height, bool isProduction)
        {
            Color fg = owner.Surface.ScriptForegroundColor;
            Color lineColor = isProduction ? _config.HeaderColor : _config.WarningColor;
            float ts = owner.Scale * 0.62f * owner.Surface.FontSize;
            string label = isProduction ? LocHelper.GetLoc("LcdMod_EnergyDashboard_ProductionGraph") : LocHelper.GetLoc("LcdMod_EnergyDashboard_ConsumptionGraph");
            float labelH = FormatingHelper.GetSizeInPixel(label, "White", ts, owner.Surface).Y;

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

            double step = CalcNiceStep(maxData > 0 ? maxData : 1.0, 4);
            double axisMax = Math.Ceiling(maxData / step) * step;
            if (axisMax < step) axisMax = step;
            int numSteps = (int)Math.Round(axisMax / step);

            string topLabel = FormatingHelper.WattsToString(axisMax);
            float axisW = FormatingHelper.GetSizeInPixel(topLabel, "White", ts, owner.Surface).X + 4f * owner.Scale;
            float plotXLeft = xLeft + axisW;
            float plotW = Math.Max(1f, contentW - axisW);
            float plotY = y + labelH + 2f * owner.Scale;
            float plotH = Math.Max(4f, height - labelH - 2f * owner.Scale);

            Color graphBg = new Color(fg.R, fg.G, fg.B, 12);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(plotXLeft + plotW / 2f, plotY + plotH / 2f), Size = new Vector2(plotW, plotH), Color = graphBg, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = label, Position = new Vector2(plotXLeft, y), RotationOrScale = ts, Color = lineColor, Alignment = TextAlignment.LEFT, FontId = "White" });

            Color axisColor = new Color(fg.R, fg.G, fg.B, 170);
            Color gridColor = new Color(fg.R, fg.G, fg.B, 18);
            float labelHHalf = FormatingHelper.GetSizeInPixel("0", "White", ts, owner.Surface).Y / 2f;

            for (int si = 0; si <= numSteps; si++)
            {
                double v = si * step;
                float lineY = plotY + plotH - (float)(v / axisMax) * plotH;
                lineY = Math.Max(plotY, Math.Min(plotY + plotH, lineY));

                if (si > 0)
                    sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(plotXLeft + plotW / 2f, lineY), Size = new Vector2(plotW, Math.Max(1f, owner.Scale * 0.5f)), Color = gridColor, Alignment = TextAlignment.CENTER });

                string lbl = FormatingHelper.WattsToString(v);
                float lblY = Math.Max(plotY, Math.Min(plotY + plotH - labelHHalf * 2f, lineY - labelHHalf));
                sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = lbl, Position = new Vector2(xLeft + axisW - 2f * owner.Scale, lblY), RotationOrScale = ts, Color = si == 0 ? new Color(fg.R, fg.G, fg.B, 110) : axisColor, Alignment = TextAlignment.RIGHT, FontId = "White" });
            }

            if (validCnt < 2 || maxData < 1.0) return;

            float prevX = 0f, prevY = 0f;
            bool hasPrev = false;
            float lineThickness = Math.Max(1.5f, owner.Scale * 1.5f);

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
                    DrawLineSegment(sprites, new Vector2(prevX, prevY), new Vector2(px, py), lineThickness, lineColor);

                prevX = px;
                prevY = py;
                hasPrev = true;
            }
        }

        void DrawBatterySection(IAppHost owner, List<MySprite> sprites, float xLeft, float xRight, float contentW, float y, float sectionH)
        {
            Color fg = owner.Surface.ScriptForegroundColor;
            Color iconColor = GetBatteryIconColor(_avgBatteryCharge);
            float cy = y + sectionH / 2f;
            float ts = owner.Scale * 0.76f * owner.Surface.FontSize;
            float tsSmall = owner.Scale * 0.63f * owner.Surface.FontSize;

            float bodyH = sectionH * 0.72f;
            float bodyW = contentW * 0.38f;
            float iconCx = xLeft + bodyW / 2f + 2f * owner.Scale;
            float pctScale = Math.Min(owner.Scale * 0.90f * owner.Surface.FontSize, bodyH * 0.55f / 14f);

            DrawHorizontalBatteryIcon(sprites, owner.Surface, new Vector2(iconCx, cy), bodyW, bodyH, _avgBatteryCharge, iconColor, fg, pctScale);

            string stateWord = _isCharging ? LocHelper.GetLoc("LcdMod_EnergyDashboard_Charging") : LocHelper.GetLoc("LcdMod_EnergyDashboard_Discharging");
            string stateText = stateWord + " — " + _timeLabel;
            Vector2 stSz = FormatingHelper.GetSizeInPixel(stateText, "White", ts, owner.Surface);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = stateText, Position = new Vector2(xRight, cy - stSz.Y - owner.Scale), RotationOrScale = ts, Color = fg, Alignment = TextAlignment.RIGHT, FontId = "White" });

            int batCount = _batteries.Count;
            string countText = FormatLoc(batCount == 1 ? "LcdMod_EnergyDashboard_BatteryCountSingular" : "LcdMod_EnergyDashboard_BatteryCountPlural", batCount);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = countText, Position = new Vector2(xRight, cy + owner.Scale), RotationOrScale = tsSmall, Color = new Color(fg.R, fg.G, fg.B, 170), Alignment = TextAlignment.RIGHT, FontId = "White" });
        }

        static void DrawHorizontalBatteryIcon(List<MySprite> sprites, Sandbox.ModAPI.Ingame.IMyTextSurface surf, Vector2 center, float bodyW, float bodyH, float ratio, Color fillColor, Color borderColor, float textScale)
        {
            const float border = 3f;
            var emptyBg = new Color(borderColor.R, borderColor.G, borderColor.B, 40);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = center, Size = new Vector2(bodyW, bodyH), Color = emptyBg, Alignment = TextAlignment.CENTER });
            if (ratio > 0.005f)
            {
                float innerW = bodyW - border * 2f;
                float fillW = innerW * ratio;
                float fillCx = center.X - innerW / 2f + fillW / 2f;
                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(fillCx, center.Y), Size = new Vector2(fillW, bodyH - border * 2f), Color = fillColor, Alignment = TextAlignment.CENTER });
            }
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(center.X + bodyW / 2f + bodyW * 0.05f, center.Y), Size = new Vector2(bodyW * 0.07f, bodyH * 0.38f), Color = borderColor, Alignment = TextAlignment.CENTER });
            float bw = Math.Max(1f, border * 0.8f);
            float halfW = bodyW / 2f;
            float halfH = bodyH / 2f;
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(center.X, center.Y - halfH), Size = new Vector2(bodyW, bw), Color = borderColor, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(center.X, center.Y + halfH), Size = new Vector2(bodyW, bw), Color = borderColor, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(center.X - halfW, center.Y), Size = new Vector2(bw, bodyH), Color = borderColor, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(center.X + halfW, center.Y), Size = new Vector2(bw, bodyH), Color = borderColor, Alignment = TextAlignment.CENTER });
            string pct = FormatingHelper.PercentageToString(ratio);
            Vector2 pctSz = FormatingHelper.GetSizeInPixel(pct, "White", textScale, surf);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = pct, Position = new Vector2(center.X, center.Y - pctSz.Y / 2f), RotationOrScale = textScale, Color = borderColor, Alignment = TextAlignment.CENTER, FontId = "White" });
        }

        static double CalcNiceStep(double maxVal, int targetDivisions)
        {
            if (maxVal <= 0 || targetDivisions <= 0) return 1.0;
            double rawStep = maxVal / targetDivisions;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            double norm = rawStep / mag;
            double niceNorm = norm <= 1.0 ? 1 : norm <= 2.0 ? 2 : norm <= 5.0 ? 5 : 10;
            return niceNorm * mag;
        }

        static void DrawLineSegment(List<MySprite> sprites, Vector2 p1, Vector2 p2, float thickness, Color color)
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

        void DrawDivider(IAppHost owner, List<MySprite> sprites, float xLeft, float xRight, float y)
        {
            Color fg = owner.Surface.ScriptForegroundColor;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2((xLeft + xRight) / 2f, y + Math.Max(1f, owner.Scale) / 2f),
                Size = new Vector2(xRight - xLeft, Math.Max(1f, owner.Scale)),
                Color = new Color(fg.R, fg.G, fg.B, 80),
                Alignment = TextAlignment.CENTER
            });
        }

        Color GetLoadColor(float ratio)
        {
            if (ratio >= 0.90f) return _config.ErrorColor;
            if (ratio >= 0.70f) return _config.WarningColor;
            return _config.HeaderColor;
        }

        Color GetBatteryIconColor(float ratio)
        {
            if (ratio < 0.15f) return _config.ErrorColor;
            if (ratio < 0.35f) return _config.WarningColor;
            return _config.HeaderColor;
        }

        static double MegaWattsToWatts(float megawatts) => megawatts * 1000000.0;
        static string FormatLoc(string key, object arg) => string.Format(FormatingHelper.Culture, LocHelper.GetLoc(key), arg);

        float GetContentTop()
        {
            return Host.TitleVisible ? Host.ViewBox.Y + (40f * Host.Scale * Host.Surface.FontSize) : Host.ViewBox.Y;
        }
    }
}
