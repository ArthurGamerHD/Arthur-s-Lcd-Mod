using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Gui.UserControls.Power;
using LcdMod.Client.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom
{
        sealed class FarmPlotControl : RectangleControl
        {
            const string FARM_PLOT_MASK_TEXTURE = "FarmPlotMask";
            static readonly Color GrowthBarColor = new Color(68, 210, 92);
            static readonly Color WaterBarColor = new Color(64, 156, 255);
            static readonly FillableTexture FarmPlotTexture = new FillableTexture("FarmPlot", 0f, 0f, 0f, 0f, 70f);

            
            public FarmPlotControl(RectangleF bounds, FarmApp.FarmEntry entry, InteractiveTooltip tooltip)
                : base(bounds, CursorType.Hand, entry, null, tooltip)
            {
                SetClass("ControlBase FarmPlotDetails");
                ClickSound = AudioHelper.HudClick;
            }

            public void Bind(FarmApp.FarmEntry entry, InteractiveTooltip tooltip)
            {
                SetDataContext(entry);
                SetCursor(CursorType.Hand);
                SetTooltip(tooltip);
                SetVisible(entry != null);
            }

            protected override void RenderDefault(List<MySprite> sprites)
            {
                var entry = DataContext as FarmApp.FarmEntry;
                if (entry == null || sprites == null)
                    return;

                DrawFarmSlotVisual(sprites, entry, Bounds, GetFarmColors());
            }

            void DrawFarmSlotVisual(List<MySprite> sprites, FarmApp.FarmEntry entry, RectangleF bounds, ColorConfigComponent colors)
            {
                float width = bounds.Width;
                float height = bounds.Height;
                float labelGap = Math.Max(1f, LayoutScale * 2f);
                float barHeight = Math.Max(4f, 4f * LayoutScale);
                float barGap = Math.Max(1f, 2f * LayoutScale);
                float barsHeight = barHeight * 2f + barGap;
                float iconBarGap = Math.Max(1f, 3f * LayoutScale);
                var label = entry.RemainingText;
                Vector2 labelRef = FormatingHelper.GetSizeInPixel(label, this, 1f, TextSurface);
                float labelScale = Math.Min((width * 0.82f) / Math.Max(1f, labelRef.X), (height * 0.22f) / Math.Max(1f, labelRef.Y)) *
                                   Math.Min(FontScale, 1f);
                float labelH = labelRef.Y * labelScale;
                float iconSize = Math.Max(0f, Math.Min(width, height - labelH - labelGap - barsHeight - iconBarGap));
                float centerX = bounds.X + width / 2f;
                float centerY = bounds.Y + iconSize / 2f;
                var center = new Vector2(centerX, centerY);

                var backgroundColor = ResolveColor(ThemeResources.BackgroundColor);
                var containerBackgroundColor = ResolveColor(ThemeResources.SecondaryContainerColor);
                var outlineColor = ResolveColor(ThemeResources.BorderColor);
                DrawFarmIcon(sprites, entry, center, iconSize, outlineColor, containerBackgroundColor);

                float barWidth = Math.Max(1f, iconSize * 0.62f);
                float barsTop = bounds.Y + iconSize + iconBarGap;
                var barTopLeft = new Vector2(centerX - barWidth / 2f, barsTop);
                DrawFarmLevelBars(sprites, entry, barTopLeft, new Vector2(barWidth, barHeight), barGap,
                    colors,
                    backgroundColor, containerBackgroundColor);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = label,
                    Position = new Vector2(centerX, barsTop + barsHeight + labelGap),
                    RotationOrScale = labelScale,
                    Color = GetStatusColor(entry),
                    Alignment = TextAlignment.CENTER,
                    FontId = TextFont
                });
            }

            static void DrawFarmIcon(
                List<MySprite> sprites,
                FarmApp.FarmEntry entry,
                Vector2 center,
                float iconSize,
                Color foreground,
                Color backgroundColor)
            {
                var frameSize = iconSize * 0.84f;
                var itemSize = iconSize * 0.58f;
                var itemCenter = FarmPlotTexture.GetInnerRect(center, frameSize).Center;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = FARM_PLOT_MASK_TEXTURE,
                    Position = center,
                    Size = new Vector2(frameSize),
                    Color = backgroundColor,
                    Alignment = TextAlignment.CENTER
                });

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = FarmPlotTexture.Name,
                    Position = center,
                    Size = new Vector2(frameSize),
                    Color = foreground,
                    Alignment = TextAlignment.CENTER
                });

                if (string.IsNullOrEmpty(entry.OutputSprite) || itemSize <= 0f)
                    return;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = entry.OutputSprite,
                    Position = itemCenter,
                    Size = new Vector2(itemSize),
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER
                });
            }

            void DrawFarmLevelBars(
                List<MySprite> sprites,
                FarmApp.FarmEntry entry,
                Vector2 topLeft,
                Vector2 size,
                float gap,
                ColorConfigComponent colors,
                Color backgroundColor,
                Color containerBackgroundColor)
            {
                var growthColor = GrowthBarColor.EnsureMinimalContrast(backgroundColor);
                var waterColor = GetWaterBarColor(colors, entry.WaterRatio, backgroundColor);

                BarPanel.CreateSprites(
                    sprites,
                    topLeft,
                    size,
                    growthColor,
                    containerBackgroundColor,
                    entry.Ratio,
                    cornerRadius: size.Y * .5f);

                BarPanel.CreateSprites(
                    sprites,
                    new Vector2(topLeft.X, topLeft.Y + size.Y + gap),
                    size,
                    waterColor,
                    containerBackgroundColor,
                    entry.WaterRatio,
                    cornerRadius: size.Y * .5f);
            }

            Color GetWaterBarColor(ColorConfigComponent colors, float waterRatio, Color backgroundColor)
            {
                Color color;
                if (waterRatio < .3f)
                    color = colors == null ? ResolveColor(ThemeResources.ErrorColor) : colors.ResolveErrorColor();
                else if (waterRatio < .6f)
                    // TODO: move warning/error bar colors into the theme.
                    color = colors == null ? new Color(224, 160, 16) : colors.ResolveWarningColor();
                else
                    color = WaterBarColor;

                return color.EnsureMinimalContrast(backgroundColor);
            }

            ColorConfigComponent GetFarmColors()
            {
                for (ControlTemplate node = this; node != null; node = node.Parent)
                {
                    var app = node.DataContext as FarmApp;
                    if (app != null)
                        return app.FarmColors;
                }

                return null;
            }

            Color GetStatusColor(FarmApp.FarmEntry entry)
            {
                var plot = entry.Plot;
                var logic = plot?.Logic;
                if (logic == null || !logic.IsPlantPlanted)
                    return ResolveColor(ThemeResources.OnSurfaceColor);
                if (!logic.IsAlive)
                    return ResolveColor(ThemeResources.ErrorColor);
                if (entry.Ratio >= 1f || logic.IsHarvestable)
                    return ResolveColor(ThemeResources.AccentColor);
                return ResolveColor(ThemeResources.OnSurfaceColor);
            }
        }
}
