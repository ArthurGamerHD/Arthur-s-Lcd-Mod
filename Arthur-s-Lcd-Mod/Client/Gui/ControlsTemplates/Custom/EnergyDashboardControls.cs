using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Power;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using GridControl = LcdMod.Client.Gui.ControlsTemplates.Panels.Grid;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom
{
    internal sealed class EnergyDashboardPowerRow
    {
        public PowerSubtypeSnapshot Entry;
        public EnergyDashboardPowerCategory Category;
        public Color Color;
        public string SpriteName;
        public double CurrentW;
        public double MaxW;
        public double RatioDenominatorW;
        public bool IsCharge;
        public bool Selected;
        public bool Hover;
        public long UpdateToken;
    }

    internal enum EnergyDashboardPowerCategory
    {
        Consumer,
        Producer,
        Charge
    }

    internal static class EnergyDashboardPowerMetrics
    {
        public const int MaxGraphSeries = 8;

        public static double GetCurrentProducerTotal(PowerSnapshot snapshot)
        {
            return snapshot.Producers.KnownCurrentOutputW;
        }

        public static double GetCurrentConsumerTotal(PowerSnapshot snapshot)
        {
            return snapshot.TotalRequiredInputW;
        }

        public static double GetMaxConsumerTotal(PowerSnapshot snapshot)
        {
            double total = 0.0;
            var entries = snapshot.ConsumerSubtypes;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry != null && entry.MaxW > 0.0)
                        total += entry.MaxW;
                }
            }

            return Math.Max(snapshot.TotalRequiredInputW, total);
        }

        public static double GetChargeTotal(PowerSnapshot snapshot)
        {
            double total = 0.0;
            var entries = snapshot.ChargeSubtypes;
            if (entries == null)
                return total;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null)
                    total += entry.CurrentW;
            }

            return total;
        }

        public static double GetGraphMax(List<PowerSnapshot> snapshots, List<EnergyDashboardPowerRow> rows,
            bool producers, bool charge, EnergyDashboardPowerRow selectedRow = null)
        {
            if (charge)
                return GetMaxChargeSubtypeCapacity(rows);

            double max = 0;
            if (snapshots != null)
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    double total = producers
                        ? GetCurrentProducerTotal(snapshots[i])
                        : GetCurrentConsumerTotal(snapshots[i]);
                    if (total > max)
                        max = total;
                }
            }

            int seriesCount = Math.Min(MaxGraphSeries, rows == null ? 0 : rows.Count);
            for (int r = 0; r < seriesCount; r++)
            {
                var row = rows[r];
                if (row.CurrentW > max)
                    max = row.CurrentW;

                if (snapshots == null || row.Entry == null)
                    continue;

                for (int i = 0; i < snapshots.Count; i++)
                {
                    double value = GetSubtypeValue(snapshots[i], row.Entry.Key, producers, false);
                    if (value > max)
                        max = value;
                }
            }

            if (selectedRow != null)
                AddSubtypeMax(snapshots, selectedRow, producers, false, ref max);

            return max;
        }

        static void AddSubtypeMax(List<PowerSnapshot> snapshots, EnergyDashboardPowerRow row,
            bool producers, bool charge, ref double max)
        {
            if (row == null || row.Entry == null || string.IsNullOrEmpty(row.Entry.Key))
                return;

            if (row.CurrentW > max)
                max = row.CurrentW;

            if (snapshots == null)
                return;

            var key = row.Entry.Key;
            for (int i = 0; i < snapshots.Count; i++)
            {
                double value = GetSubtypeValue(snapshots[i], key, producers, charge);
                if (value > max)
                    max = value;
            }
        }

        static double GetMaxChargeSubtypeCapacity(List<EnergyDashboardPowerRow> rows)
        {
            double max = 0.0;
            if (rows == null)
                return max;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].MaxW > max)
                    max = rows[i].MaxW;
            }

            return max;
        }

        public static double GetSubtypeValue(PowerSnapshot snapshot, string key, bool producers, bool charge)
        {
            var entries = charge ? snapshot.ChargeSubtypes :
                producers ? snapshot.ProducerSubtypes : snapshot.ConsumerSubtypes;
            if (entries == null || string.IsNullOrEmpty(key))
                return 0;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && string.Equals(entry.Key, key, StringComparison.Ordinal))
                    return entry.CurrentW;
            }

            return 0;
        }

        public static float Ratio(double current, double max)
        {
            if (max <= 0)
                return 0f;
            return (float)Math.Max(0, Math.Min(1, current / max));
        }
    }

    internal sealed class EnergyStatBarControl : RectangleControl
    {
        readonly IAppHost _host;

        public EnergyStatBarControl(IAppHost host) : base(default(RectangleF))
        {
            _host = host;
        }

        public string Label { get; set; }
        public double Current { get; set; }
        public double Max { get; set; }
        public Color FillColor { get; set; }
        public bool ShowPercentage { get; set; }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var rect = Bounds;
            Color fg = _host.Surface.ScriptForegroundColor;
            Color bg = new Color(fg.R, fg.G, fg.B, 24);
            float ratio = EnergyDashboardPowerMetrics.Ratio(Current, Max);
            float scale = _host.Config.Scale;
            float titleTs = scale * 0.52f * _host.Surface.FontSize;
            float valueTs = scale * 0.56f * _host.Surface.FontSize;
            string title = string.IsNullOrEmpty(Label) ? string.Empty : Label + ":";
            Vector2 titleSz = FormatingHelper.GetSizeInPixel(string.IsNullOrEmpty(title) ? " " : title, this,
                titleTs, _host.Surface);
            float titleGap = 2f * scale;
            float barY = Math.Min(rect.Bottom, rect.Y + titleSz.Y + titleGap);
            var bar = new RectangleF(rect.X, barY, rect.Width, Math.Max(1f, rect.Bottom - barY));

            if (!string.IsNullOrEmpty(title))
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT, Data = title, Position = new Vector2(rect.X, rect.Y),
                    RotationOrScale = titleTs, Color = new Color(fg.R, fg.G, fg.B, 180), Alignment = TextAlignment.LEFT,
                    FontId = TextFont
                });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = bar.Center, Size = bar.Size, Color = bg,
                Alignment = TextAlignment.CENTER
            });
            if (ratio > 0.004f)
            {
                float fillW = bar.Width * ratio;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(bar.X + fillW * 0.5f, bar.Center.Y), Size = new Vector2(fillW, bar.Height),
                    Color = new Color(FillColor.R, FillColor.G, FillColor.B, 180), Alignment = TextAlignment.CENTER
                });
            }

            string value = FormatCurrentAndMax(ratio);
            valueTs = GetFittedTextScale(value, valueTs, Math.Max(1f, bar.Width - 8f * scale),
                Math.Max(1f, bar.Height - 2f * scale));
            Vector2 valueSz = FormatingHelper.GetSizeInPixel(value, this, valueTs, _host.Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = value,
                Position = new Vector2(bar.Right - 4f * scale, bar.Center.Y - valueSz.Y * 0.5f),
                RotationOrScale = valueTs, Color = fg, Alignment = TextAlignment.RIGHT, FontId = TextFont
            });
        }

        float GetFittedTextScale(string text, float baseScale, float availableWidth, float availableHeight)
        {
            Vector2 size = FormatingHelper.GetSizeInPixel(text ?? string.Empty, this, baseScale, _host.Surface);
            if (size.X <= 0f || size.Y <= 0f || (size.X <= availableWidth && size.Y <= availableHeight))
                return baseScale;

            float widthRatio = availableWidth / size.X;
            float heightRatio = availableHeight / size.Y;
            return Math.Max(0.01f, baseScale * Math.Min(widthRatio, heightRatio));
        }

        string FormatCurrentAndMax(float ratio)
        {
            string current = ShowPercentage ? FormatWattHours(Current) : FormatingHelper.WattsToString(Current);
            string max = ShowPercentage ? FormatWattHours(Max) : FormatingHelper.WattsToString(Max);
            string value = current + "/" + max;
            return ShowPercentage ? value + " (" + FormatingHelper.PercentageToString(ratio) + ")" : value;
        }

        static string FormatWattHours(double wattHours)
        {
            return Math.Abs(wattHours) < 1e-12 ? "0 Wh" : FormatingHelper.WattHoursToString((float)wattHours);
        }
    }

    internal sealed class EnergySubtypeGraphControl : RectangleControl
    {
        readonly IAppHost _host;
        List<PowerSnapshot> _snapshots = new List<PowerSnapshot>();
        List<EnergyDashboardPowerRow> _rows = new List<EnergyDashboardPowerRow>();
        static readonly Color GraphBackgroundColor = new Color(0, 0, 0, 64);
        const string GRID_TEXTURE = "Grid";

        // Grid texture is 2048x2048, has 24 "lines", meaning 23 source intervals.
        const float GRID_TEXTURE_SIZE_PIXELS = 2048f;

        const int GRID_TEXTURE_LINE_COUNT = 24;

        // The graph has 10 tick positions, so it has 9 interval.
        const int GRAPH_TICK_COUNT = 10;

        public EnergySubtypeGraphControl(IAppHost host) : base(default(RectangleF))
        {
            _host = host;
        }

        public string Title { get; set; }
        public bool Producers { get; set; }
        public bool Charge { get; set; }
        public float WindowSeconds { get; set; }
        public bool UseTimeSpacing { get; set; }
        public EnergyDashboardPowerRow SelectedRow { get; set; }

        public void Bind(List<PowerSnapshot> snapshots, List<EnergyDashboardPowerRow> rows)
        {
            _snapshots = snapshots ?? new List<PowerSnapshot>();
            _rows = rows ?? new List<EnergyDashboardPowerRow>();
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var rect = Bounds;
            Color fg = _host.Surface.ScriptForegroundColor;
            float scale = _host.Config.Scale;
            float ts = scale * 0.56f * _host.Surface.FontSize;
            float titleTs = ts * 1.08f;
            float labelH = FormatingHelper.GetSizeInPixel("0", this, ts, _host.Surface).Y;
            float titleH = FormatingHelper
                .GetSizeInPixel(string.IsNullOrEmpty(Title) ? " " : Title, this, titleTs, _host.Surface).Y;
            float headerH = titleH + 6f * scale;
            float timeScaleH = labelH + 5f * scale;
            float axisW = 43f * scale;
            RectangleF plot = new RectangleF(rect.X + axisW, rect.Y + headerH, Math.Max(1f, rect.Width - axisW),
                Math.Max(1f, rect.Height - headerH - timeScaleH));
            Color axis = new Color(fg.R, fg.G, fg.B, 150);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = plot.Center, Size = plot.Size,
                Color = GraphBackgroundColor, Alignment = TextAlignment.CENTER
            });
            RenderPlotGrid(sprites, plot, new Color(fg.R, fg.G, fg.B, 26));
            if (!string.IsNullOrEmpty(Title))
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT, Data = Title,
                    Position = new Vector2(rect.X + 4f * scale, rect.Y + 2f * scale), RotationOrScale = titleTs,
                    Color = fg, Alignment = TextAlignment.LEFT, FontId = TextFont
                });

            RenderTimeScale(sprites, plot, ts * 0.82f, axis);

            double max = EnergyDashboardPowerMetrics.GetGraphMax(_snapshots, _rows, Producers, Charge, SelectedRow);
            if (max <= 0 || _snapshots.Count <= 0 || _rows.Count == 0)
            {
                RenderEmptyLabel(sprites, plot, "No data");
                return;
            }

            float axisLabelScale = GetAxisLabelScale(max, ts, Math.Max(1f, axisW - 6f * scale), Charge);
            for (int i = 0; i <= 3; i++)
            {
                double ratio = i / 3.0;
                float yy = plot.Bottom - (float)ratio * plot.Height;

                string label = Charge
                    ? FormatingHelper.WattHoursToString(max * ratio, "0.#")
                    : FormatingHelper.WattsToString(max * ratio, "0.#");
                float axisLabelH = FormatingHelper.GetSizeInPixel(label, this, axisLabelScale, _host.Surface).Y;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT, Data = label,
                    Position = new Vector2(plot.X - 3f * scale, yy - axisLabelH * 0.5f),
                    RotationOrScale = axisLabelScale, Color = axis, Alignment = TextAlignment.RIGHT, FontId = TextFont
                });
            }

            int seriesCount = Math.Min(EnergyDashboardPowerMetrics.MaxGraphSeries, _rows.Count);
            for (int r = 0; r < seriesCount; r++)
            {
                if (ReferenceEquals(_rows[r], SelectedRow))
                    continue;

                RenderSeries(sprites, plot, _snapshots, _rows[r], Producers, max);
            }

            if (SelectedRow != null)
                RenderSeries(sprites, plot, _snapshots, SelectedRow, Producers, max);
        }

        void RenderPlotGrid(List<MySprite> sprites, RectangleF plot, Color color)
        {
            if (sprites == null || plot.Width <= 0f || plot.Height <= 0f)
                return;

            float sourceIntervalPixels = GRID_TEXTURE_SIZE_PIXELS / (GRID_TEXTURE_LINE_COUNT - 1);
            float graphTickSpacing = plot.Width / (GRAPH_TICK_COUNT - 1);
            float textureScale = graphTickSpacing / sourceIntervalPixels;
            float tileSize = GRID_TEXTURE_SIZE_PIXELS * textureScale;
            if (tileSize <= 0f)
                return;

            if (!BeginContentClip(sprites, plot))
                return;

            for (float bottom = plot.Bottom; bottom > plot.Y; bottom -= tileSize)
            {
                for (float x = plot.X; x < plot.Right; x += tileSize)
                {
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = GRID_TEXTURE,
                        Position = new Vector2(x + tileSize * 0.5f, bottom - tileSize * 0.5f),
                        Size = new Vector2(tileSize),
                        Color = color,
                        Alignment = TextAlignment.CENTER
                    });
                }
            }

            EndContentClip(sprites);
        }

        void RenderSeries(List<MySprite> sprites, RectangleF plot, List<PowerSnapshot> snapshots,
            EnergyDashboardPowerRow row, bool producers, double max)
        {
            if (snapshots == null || snapshots.Count == 0 || row.Entry == null)
                return;

            float thickness = Math.Max(1f, _host.Config.Scale * 1.25f);
            bool hasPrev = false;
            Vector2 prev = Vector2.Zero;
            for (int i = 0; i < snapshots.Count; i++)
            {
                double value =
                    EnergyDashboardPowerMetrics.GetSubtypeValue(snapshots[i], row.Entry.Key, producers, Charge);
                float x = UseTimeSpacing
                    ? GetTimeSpacedX(plot, snapshots, i)
                    : GetIndexSpacedX(plot, snapshots.Count, i);
                float y = plot.Bottom - (float)Math.Min(1.0, value / max) * plot.Height;
                var current = new Vector2(x, y);
                if (hasPrev)
                    RenderLineSegment(sprites, prev, current, thickness, row.Color);
                prev = current;
                hasPrev = true;
            }
        }

        static float GetIndexSpacedX(RectangleF plot, int count, int index)
        {
            return count <= 1 ? plot.X : plot.X + (float)index / (count - 1) * plot.Width;
        }

        static float GetTimeSpacedX(RectangleF plot, List<PowerSnapshot> snapshots, int index)
        {
            int count = snapshots != null ? snapshots.Count : 0;
            if (count <= 1)
                return plot.X;

            long first = snapshots[0].GameplayFrame;
            long last = snapshots[count - 1].GameplayFrame;
            if (last <= first)
                return GetIndexSpacedX(plot, count, index);

            double ratio = (snapshots[index].GameplayFrame - first) / (double)(last - first);
            return plot.X + (float)Math.Max(0.0, Math.Min(1.0, ratio)) * plot.Width;
        }

        void RenderEmptyLabel(List<MySprite> sprites, RectangleF plot, string text)
        {
            float ts = _host.Config.Scale * 0.56f * _host.Surface.FontSize;
            Color fg = new Color(_host.Surface.ScriptForegroundColor.R, _host.Surface.ScriptForegroundColor.G,
                _host.Surface.ScriptForegroundColor.B, 140);
            Vector2 sz = FormatingHelper.GetSizeInPixel(text, this, ts, _host.Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = text, Position = new Vector2(plot.Center.X, plot.Center.Y - sz.Y * 0.5f),
                RotationOrScale = ts, Color = fg, Alignment = TextAlignment.CENTER, FontId = TextFont
            });
        }

        void RenderTimeScale(List<MySprite> sprites, RectangleF plot, float baseScale, Color color)
        {
            if (_snapshots == null || _snapshots.Count == 0)
                return;

            float scale = _host.Config.Scale;
            float tickH = Math.Max(2f, 3f * scale);
            float y = plot.Bottom + 2f * scale;
            float labelY = y + tickH + scale;
            string left = WindowSeconds > 0f
                ? FormatRelativeSeconds(WindowSeconds)
                : FormatRelativeTime(GetInterpolatedFrame(0f));
            string middle = WindowSeconds > 0f
                ? FormatRelativeSeconds(WindowSeconds * 0.5f)
                : FormatRelativeTime(GetInterpolatedFrame(0.5f));
            const string right = "now";
            float textScale = GetTimeLabelScale(baseScale, Math.Max(1f, plot.Width * 0.32f), left, middle, right);

            DrawTimeTick(sprites, plot.X, y, tickH, color);
            DrawTimeTick(sprites, plot.Center.X, y, tickH, color);
            DrawTimeTick(sprites, plot.Right, y, tickH, color);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = left, Position = new Vector2(plot.X, labelY),
                RotationOrScale = textScale, Color = color, Alignment = TextAlignment.LEFT, FontId = TextFont
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = middle, Position = new Vector2(plot.Center.X, labelY),
                RotationOrScale = textScale, Color = color, Alignment = TextAlignment.CENTER, FontId = TextFont
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = right, Position = new Vector2(plot.Right, labelY),
                RotationOrScale = textScale, Color = color, Alignment = TextAlignment.RIGHT, FontId = TextFont
            });
        }

        long GetInterpolatedFrame(float ratio)
        {
            int count = _snapshots != null ? _snapshots.Count : 0;
            if (count <= 0)
                return 0L;
            if (count == 1)
                return _snapshots[0].GameplayFrame;

            long first = _snapshots[0].GameplayFrame;
            long last = _snapshots[count - 1].GameplayFrame;
            return first + (long)Math.Round((last - first) * Math.Max(0f, Math.Min(1f, ratio)));
        }

        string FormatRelativeTime(long frame)
        {
            int count = _snapshots != null ? _snapshots.Count : 0;
            if (count <= 1)
                return "now";

            long latest = _snapshots[count - 1].GameplayFrame;
            double secondsAgo = Math.Max(0.0, (latest - frame) / 60.0);
            if (secondsAgo < 0.5)
                return "now";
            if (secondsAgo < 60.0)
                return "-" + Math.Max(1, (int)Math.Round(secondsAgo)).ToString(FormatingHelper.Culture) + "s";

            int minutes = Math.Max(1, (int)Math.Round(secondsAgo / 60.0));
            return "-" + minutes.ToString(FormatingHelper.Culture) + "m";
        }

        static string FormatRelativeSeconds(float secondsAgo)
        {
            if (secondsAgo <= 0.05f)
                return "now";
            if (secondsAgo < 60f)
                return "-" + secondsAgo.ToString(secondsAgo < 10f ? "0.#" : "0", FormatingHelper.Culture) + "s";

            float minutes = secondsAgo / 60f;
            return "-" + minutes.ToString(minutes < 10f ? "0.#" : "0", FormatingHelper.Culture) + "m";
        }

        float GetTimeLabelScale(float baseScale, float availableWidth, string left, string middle, string right)
        {
            float widest = Math.Max(
                FormatingHelper.GetSizeInPixel(left, this, baseScale, _host.Surface).X,
                Math.Max(
                    FormatingHelper.GetSizeInPixel(middle, this, baseScale, _host.Surface).X,
                    FormatingHelper.GetSizeInPixel(right, this, baseScale, _host.Surface).X));

            if (widest <= 0f || widest <= availableWidth)
                return baseScale;

            return Math.Max(0.01f, baseScale * availableWidth / widest);
        }

        static void DrawTimeTick(List<MySprite> sprites, float x, float y, float height, Color color)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x, y + height * 0.5f),
                Size = new Vector2(1f, height), Color = color, Alignment = TextAlignment.CENTER
            });
        }

        float GetAxisLabelScale(double max, float baseScale, float availableWidth, bool charge)
        {
            float widest = 0f;
            for (int i = 0; i <= 3; i++)
            {
                string label = charge
                    ? FormatingHelper.WattHoursToString(max * (i / 3.0), "0.#")
                    : FormatingHelper.WattsToString(max * (i / 3.0), "0.#");
                float width = FormatingHelper.GetSizeInPixel(label, this, baseScale, _host.Surface).X;
                if (width > widest)
                    widest = width;
            }

            if (widest <= 0f || widest <= availableWidth)
                return baseScale;

            return Math.Max(0.01f, baseScale * availableWidth / widest);
        }

        static void RenderLineSegment(List<MySprite> sprites, Vector2 p1, Vector2 p2, float thickness, Color color)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2((p1.X + p2.X) * 0.5f, (p1.Y + p2.Y) * 0.5f),
                Size = new Vector2(len, thickness),
                RotationOrScale = (float)Math.Atan2(dy, dx),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }
    }

    internal sealed class EnergyPowerRowControl : Panel
    {
        readonly GridControl _layout;
        readonly GridControl _barCell;
        readonly EnergyPowerIconControl _icon;
        readonly TextBlock _name;
        readonly ProgressBar _bar;
        readonly TextBlock _value;
        EnergyDashboardPowerRow _row;

        public EnergyPowerRowControl(IAppHost host) : base(default(RectangleF), CursorType.Hand)
        {
            _layout = new GridControl(default(RectangleF), new[] { 16f, 52f, 32f }, new[] { 42f, 58f })
            {
                BackgroundTexture = null
            };
            _barCell = new GridControl(default(RectangleF), new[] { 1f, 24f, 1f }, new[] { 31f, 38f, 31f })
            {
                BackgroundTexture = null
            };
            _icon = new EnergyPowerIconControl(host);
            _name = new TextBlock(default(RectangleF))
            {
                FontScale = 0.48f,
                HorizontalAlignment = TextAlignment.LEFT,
                VerticalAlignment = TextBlockVerticalAlignment.Center,
                Wrapping = TextBlockWrapping.NoWrap,
                Ellipsize = true
            };
            _bar = new ProgressBar(default(RectangleF));
            _value = new TextBlock(default(RectangleF))
            {
                FontScale = 0.60f,
                HorizontalAlignment = TextAlignment.RIGHT,
                VerticalAlignment = TextBlockVerticalAlignment.Center,
                Wrapping = TextBlockWrapping.NoWrap,
                Ellipsize = true
            };

            _layout.Set(_icon, 0, 0, 1, 2);
            _layout.Set(_name, 1, 0, 2, 1);
            _layout.Set(_barCell, 1, 1);
            _barCell.Set(_bar, 1, 1);
            _layout.Set(_value, 2, 1);
            AddChild(_layout);
        }

        public Action<EnergyDashboardPowerRow> RowClicked { get; set; }
        public Action<EnergyDashboardPowerRow> RowHovered { get; set; }

        public override bool CanPrimaryClick => Visible && Enabled && _row != null && _row.Entry != null && RowClicked != null;

        public override bool CanHover => Visible && Enabled && _row != null && _row.Entry != null && RowHovered != null;

        public void SetRow(EnergyDashboardPowerRow row)
        {
            _row = row;
            var entry = row != null ? row.Entry : null;
            _icon.Label = GetRowLabel(entry);
            _icon.Amount = entry != null ? entry.BlockCount.ToString(FormatingHelper.Culture) : "0";
            _icon.Color = row != null ? row.Color : Color.White;
            _icon.SpriteName = row != null ? row.SpriteName : string.Empty;
            _name.Text = GetRowLabel(entry);

            _bar.Fraction = row != null ? EnergyDashboardPowerMetrics.Ratio(row.CurrentW, row.RatioDenominatorW) : 0f;
            _bar.FillColor = row != null ? row.Color : Color.White;
            _bar.FillColorOverride = null;
            _bar.CornerRadius = -1f;

            _value.Text = row == null
                ? string.Empty
                : row.IsCharge
                    ? FormatingHelper.WattHoursToString(row.CurrentW)
                    : FormatingHelper.WattsToString(row.CurrentW);
        }

        public override bool Click(object sender)
        {
            if (!CanPrimaryClick)
                return false;

            RowClicked(_row);
            return true;
        }

        public override bool Hover(object sender)
        {
            if (!CanHover)
                return false;

            RowHovered(_row);
            return true;
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            _bar.BackgroundColor = ResolveColor(ThemeResources.SurfaceContainerHighColor);

            if (_row != null)
            {
                ResourceKey<Color> panelColorKey = null;
                if (_row.Selected && _row.Hover)
                    panelColorKey = ThemeResources.SecondaryContainerColor;
                else if (_row.Selected)
                    panelColorKey = ThemeResources.SecondaryContainerColor;
                else if (_row.Hover)
                    panelColorKey = ThemeResources.SurfaceContainerColor;

                if (panelColorKey != null)
                    Border.CreateSpritesFromRect(Bounds, sprites, ResolveColor(panelColorKey),
                        radiusScale: LayoutScale);
            }

            base.RenderDefault(sprites);
        }

        protected override void ArrangeChildren()
        {
            _layout.Arrange(Bounds);
        }

        static string GetRowLabel(PowerSubtypeSnapshot entry)
        {
            if (entry == null)
                return "Unknown";
            if (!string.IsNullOrEmpty(entry.DisplayName))
                return entry.DisplayName;
            if (!string.IsNullOrEmpty(entry.SubtypeId))
                return entry.SubtypeId;
            return "Unknown";
        }
    }

    internal sealed class EnergyPowerIconControl : RectangleControl
    {
        readonly IAppHost _host;

        public EnergyPowerIconControl(IAppHost host) : base(default(RectangleF))
        {
            _host = host;
            Label = string.Empty;
            Amount = string.Empty;
            SpriteName = string.Empty;
            Color = Color.White;
        }

        public string Label { get; set; }
        public string Amount { get; set; }
        public string SpriteName { get; set; }
        public Color Color { get; set; }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var rect = GetViewBox();
            Color fg = _host.Surface.ScriptForegroundColor;
            float scale = _host.Config.Scale;
            float icon = Math.Min(rect.Height * 0.78f, Math.Max(1f, rect.Width - 2f * scale));
            var iconRect = new RectangleF(rect.Center.X - icon * 0.5f, rect.Center.Y - icon * 0.5f, icon, icon);

            if (!string.IsNullOrEmpty(SpriteName))
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = SpriteName, Position = iconRect.Center,
                    Size = new Vector2(iconRect.Width - 3f * scale, iconRect.Height - 3f * scale), Color = Color.White,
                    Alignment = TextAlignment.CENTER
                });
            }
            else
            {
                string glyph = GetInitial(Label);
                float ts = scale * 0.72f * _host.Surface.FontSize;
                Vector2 sz = FormatingHelper.GetSizeInPixel(glyph, this, ts, _host.Surface);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT, Data = glyph,
                    Position = new Vector2(iconRect.Center.X, iconRect.Center.Y - sz.Y * 0.5f), RotationOrScale = ts,
                    Color = Color, Alignment = TextAlignment.CENTER, FontId = TextFont
                });
            }

            if (!string.IsNullOrEmpty(Amount))
            {
                float miniTs = scale * 0.42f * _host.Surface.FontSize;
                Vector2 amountSz = FormatingHelper.GetSizeInPixel(Amount, this, miniTs, _host.Surface);
                var pos = new Vector2(iconRect.Right - amountSz.X * 0.5f - 1f * scale,
                    iconRect.Bottom - amountSz.Y - 1f * scale);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT, Data = Amount, Position = pos, RotationOrScale = miniTs, Color = fg,
                    Alignment = TextAlignment.CENTER, FontId = TextFont
                });
            }
        }

        static string GetInitial(string label)
        {
            if (string.IsNullOrEmpty(label))
                return "?";
            for (int i = 0; i < label.Length; i++)
            {
                if (!char.IsWhiteSpace(label[i]))
                    return char.ToUpperInvariant(label[i]).ToString();
            }

            return "?";
        }
    }
}
