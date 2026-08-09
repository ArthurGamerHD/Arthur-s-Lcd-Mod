using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Defense;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom
{
    internal abstract class DefenseStatusTile : RectangleControl
    {
        Color _cardColor;
        Color _warningColor;
        Color _errorColor;

        protected DefenseStatusTile() : base(default(RectangleF))
        {
        }

        protected Color Foreground => ResolveColor(ThemeResources.FontColor);
        protected Color Muted => ResolveColor(ThemeResources.MutedTextColor);
        protected Color Accent => ResolveColor(ThemeResources.AccentColor);
        protected Color Warning => _warningColor;
        protected Color Error => _errorColor;
        protected Color Success => ResolveColor(ThemeResources.SuccessColor);

        protected void SetCardColor(Color color)
        {
            if (_cardColor.Equals(color))
                return;

            _cardColor = color;
            MarkDirty();
        }

        protected void SetStatusColors(Color warning, Color error)
        {
            if (_warningColor.Equals(warning) && _errorColor.Equals(error))
                return;

            _warningColor = warning;
            _errorColor = error;
            MarkDirty();
        }

        protected void DrawCard(List<MySprite> sprites)
        {
            Color cardColor = _cardColor;
            Color shadowColor = cardColor.MulValue(0.2f);
            float shadowOffset = 2f * LayoutScale;
            var shadow = new RectangleF(Bounds.Position + shadowOffset, Bounds.Size);
            BorderRenderer.CreateSpritesFromRect(
                shadow,
                sprites,
                shadowColor,
                radiusScale: LayoutScale);
            BorderRenderer.CreateSpritesFromRect(
                Bounds,
                sprites,
                cardColor,
                radiusScale: LayoutScale);
        }

        protected void DrawBar(List<MySprite> sprites, RectangleF rect, float fraction, Color fill)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = rect.Center,
                Size = rect.Size,
                Color = ResolveColor(ThemeResources.SurfaceContainerHighestColor),
                Alignment = TextAlignment.CENTER
            });

            float fillWidth = rect.Width * MathHelper.Clamp(fraction, 0f, 1f);
            if (fillWidth <= 0.1f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(rect.X + fillWidth * 0.5f, rect.Center.Y),
                Size = new Vector2(fillWidth, rect.Height),
                Color = fill,
                Alignment = TextAlignment.CENTER
            });
        }

        protected void DrawIcon(List<MySprite> sprites, string spriteName, RectangleF rect)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = string.IsNullOrEmpty(spriteName) ? "MissingIcon" : spriteName,
                Position = rect.Center,
                Size = rect.Size,
                Color = Color.White,
                Alignment = TextAlignment.CENTER
            });
        }

        protected RectangleF GetLeftIconRect(RectangleF rect, float padding)
        {
            float availableHeight = Math.Max(1f, rect.Height - padding * 2f);
            float size = Math.Max(1f, Math.Min(availableHeight, rect.Width * 0.28f));
            return new RectangleF(rect.X + padding, rect.Center.Y - size * 0.5f, size, size);
        }

        protected RectangleF GetContentRect(RectangleF rect, RectangleF iconRect, float padding)
        {
            float gap = Math.Max(2f, 8f * LayoutScale);
            float x = iconRect.Right + gap;
            return new RectangleF(
                x,
                rect.Y + padding,
                Math.Max(1f, rect.Right - padding - x),
                Math.Max(1f, rect.Height - padding * 2f));
        }

        protected void DrawText(
            List<MySprite> sprites,
            string text,
            RectangleF rect,
            float requestedScale,
            Color color,
            TextAlignment alignment = TextAlignment.CENTER,
            string fontId = null)
        {
            if (string.IsNullOrEmpty(text) || rect.Width <= 0f || rect.Height <= 0f)
                return;

            string renderFont = string.IsNullOrEmpty(fontId) ? TextFont : fontId;
            float textScale = FitText(text, renderFont, requestedScale, rect.Width, rect.Height);
            Vector2 size = MeasureText(text, renderFont, textScale);
            float x = alignment == TextAlignment.LEFT
                ? rect.X
                : alignment == TextAlignment.RIGHT
                    ? rect.Right
                    : rect.Center.X;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(x, rect.Center.Y - size.Y * 0.5f),
                RotationOrScale = textScale,
                Color = color,
                Alignment = alignment,
                FontId = renderFont
            });
        }

        float FitText(
            string text,
            string fontId,
            float requestedScale,
            float availableWidth,
            float availableHeight)
        {
            float safeScale = Math.Max(0.01f, requestedScale);
            Vector2 size = MeasureText(text ?? string.Empty, fontId, safeScale);
            if (size.X <= 0f || size.Y <= 0f)
                return safeScale;

            float ratio = Math.Min(1f,
                Math.Min(Math.Max(0.01f, availableWidth) / size.X, Math.Max(0.01f, availableHeight) / size.Y));
            return Math.Max(0.01f, safeScale * ratio);
        }
    }

    /// <summary>A stable tile bound to one shield provider.</summary>
    internal sealed class ShieldStatus : DefenseStatusTile
    {
        public ShieldStatus(string providerKey, ShieldInfo viewModel)
        {
            ProviderKey = providerKey ?? string.Empty;
            SetDataContext(viewModel);
        }

        public string ProviderKey { get; private set; }

        public void SetViewModel(ShieldInfo viewModel)
        {
            SetDataContext(viewModel);
        }

        public void SetPresentationColors(Color cardColor, Color warningColor, Color errorColor)
        {
            SetCardColor(cardColor);
            SetStatusColors(warningColor, errorColor);
        }

        protected override bool ShouldInvalidateForDataContextProperty(string propertyName)
        {
            switch (propertyName)
            {
                case nameof(ShieldInfo.ProviderName):
                case nameof(ShieldInfo.ValueUnit):
                case nameof(ShieldInfo.UseSiPrefixes):
                case nameof(ShieldInfo.CurrentPoints):
                case nameof(ShieldInfo.MaximumPoints):
                case nameof(ShieldInfo.RechargePointsPerSecond):
                case nameof(ShieldInfo.EffectivenessRatio):
                case nameof(ShieldInfo.IsWorking):
                case nameof(ShieldInfo.HasCapacity):
                case nameof(ShieldInfo.HasRecharge):
                case nameof(ShieldInfo.TicksUntilRecharge):
                case nameof(ShieldInfo.HasRechargeDelay):
                case nameof(ShieldInfo.HasEffectiveness):
                case nameof(ShieldInfo.UsesLiveData):
                case nameof(ShieldInfo.GhostChargeRatio):
                case nameof(ShieldInfo.HasGhostCharge):
                    return true;
                default:
                    return false;
            }
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var shield = DataContext as ShieldInfo;
            if (shield == null)
                return;

            DrawCard(sprites);
            RectangleF rect = Bounds;
            float scale = Math.Max(0.1f, LayoutScale);
            float textScale = Math.Max(0.01f, scale * FontScale);
            float padding = Math.Min(8f * scale, Math.Min(rect.Width * 0.08f, rect.Height * 0.12f));
            var body = new RectangleF(
                rect.X + padding,
                rect.Y + padding,
                Math.Max(1f, rect.Width - padding * 2f),
                Math.Max(1f, rect.Height - padding * 2f));
            float outerRadius = Math.Max(1f, Math.Min(body.Height * 0.48f, body.Width * 0.18f));
            float innerRadius = outerRadius * 0.68f;
            var ringCenter = new Vector2(body.X + outerRadius, body.Center.Y);
            float gap = Math.Max(2f, 8f * scale);
            float contentX = ringCenter.X + outerRadius + gap;
            var content = new RectangleF(
                contentX,
                body.Y,
                Math.Max(1f, body.Right - contentX),
                body.Height);
            float ratio = GetChargeRatio(shield);
            Color statusColor = ResolveStatusColor(shield, ratio);
            Color trackColor = ResolveColor(ThemeResources.SurfaceContainerHighestColor);
            string providerName = string.IsNullOrEmpty(shield.ProviderName)
                ? DefenseDashboardLocalization.ShieldFallback
                : shield.ProviderName;
            float renderedRatio = shield.HasCapacity ? ratio : (shield.IsWorking ? 1f : 0f);
            bool drawGhost = shield.HasGhostCharge && shield.GhostChargeRatio > renderedRatio + 0.0001f;

            if (drawGhost)
            {
                Color ghostColor = statusColor.MulSaturation(0.25f).MulValue(0.25f);
                DonutPanel.DrawDonut(
                    sprites,
                    ringCenter,
                    innerRadius,
                    outerRadius,
                    shield.GhostChargeRatio,
                    ghostColor,
                    trackColor,
                    gapPixels: 2f * scale);
            }

            DonutPanel.DrawDonut(
                sprites,
                ringCenter,
                innerRadius,
                outerRadius,
                renderedRatio,
                statusColor,
                trackColor,
                gapPixels: 2f * scale,
                drawBackground: !drawGhost);

            float iconSize = innerRadius * 1.3f;
            DrawIcon(
                sprites,
                "ShieldIcon",
                new RectangleF(
                    ringCenter.X - iconSize * 0.5f,
                    ringCenter.Y - iconSize * 0.5f,
                    iconSize,
                    iconSize));

            string charge = FormatCharge(shield);
            string delta = FormatDelta(shield);
            Color deltaColor = ResolveDeltaColor(shield);
            float titleTextScale = 0.50f * textScale;
            float chargeTextScale = 0.78f * textScale;
            float deltaTextScale = 0.48f * textScale;
            string chargeFont = ResolveShieldDetailFont(true);
            float titleHeight = Math.Max(1f, MeasureText(providerName, TextFont, titleTextScale).Y);
            float chargeHeight = Math.Max(1f, MeasureText(charge, chargeFont, chargeTextScale).Y);
            float deltaHeight = Math.Max(1f, MeasureText(delta, TextFont, deltaTextScale).Y);
            float rowGap = Math.Max(1f, 2f * scale);
            float detailsHeight = titleHeight + chargeHeight + deltaHeight + rowGap * 2f;
            if (detailsHeight > content.Height)
            {
                float fit = content.Height / detailsHeight;
                titleHeight *= fit;
                chargeHeight *= fit;
                deltaHeight *= fit;
                rowGap *= fit;
                titleTextScale *= fit;
                chargeTextScale *= fit;
                deltaTextScale *= fit;
                detailsHeight = content.Height;
            }

            float detailY = content.Center.Y - detailsHeight * 0.5f;

            DrawShieldDetailRow(
                sprites,
                new RectangleF(content.X, detailY, content.Width, titleHeight),
                providerName,
                titleTextScale,
                Foreground);
            detailY += titleHeight + rowGap;
            DrawShieldDetailRow(
                sprites,
                new RectangleF(content.X, detailY, content.Width, chargeHeight),
                charge,
                chargeTextScale,
                Foreground,
                true);
            detailY += chargeHeight + rowGap;
            DrawShieldDetailRow(
                sprites,
                new RectangleF(content.X, detailY, content.Width, deltaHeight),
                delta,
                deltaTextScale,
                deltaColor);

        }

        void DrawShieldDetailRow(
            List<MySprite> sprites,
            RectangleF row,
            string text,
            float requestedTextScale,
            Color color,
            bool emphasized = false)
        {
            string fontId = ResolveShieldDetailFont(emphasized);
            DrawText(
                sprites,
                text,
                row,
                requestedTextScale,
                color,
                TextAlignment.LEFT,
                fontId);
        }

        string ResolveShieldDetailFont(bool emphasized)
        {
            return emphasized && SupportsBoldFont(TextFont) ? "White-Bold" : TextFont;
        }

        static bool SupportsBoldFont(string fontId)
        {
            return string.Equals(fontId, "White", StringComparison.Ordinal) ||
                   string.Equals(fontId, "Debug", StringComparison.Ordinal);
        }

        static float GetChargeRatio(ShieldInfo shield)
        {
            if (!shield.HasCapacity || shield.MaximumPoints <= 0f)
                return 0f;
            return MathHelper.Clamp(shield.CurrentPoints / shield.MaximumPoints, 0f, 1f);
        }

        Color ResolveStatusColor(ShieldInfo shield, float ratio)
        {
            if (!shield.IsWorking || shield.HasCapacity && ratio <= 0.15f)
                return Error;
            if (shield.HasCapacity && ratio <= 0.35f)
                return Warning;
            return Accent;
        }

        static string FormatCharge(ShieldInfo shield)
        {
            string current = ShieldValueFormatter.Format(
                shield.CurrentPoints, shield.ValueUnit, shield.UseSiPrefixes);
            if (!shield.HasCapacity)
                return current;
            return current + " / " + ShieldValueFormatter.Format(
                shield.MaximumPoints, shield.ValueUnit, shield.UseSiPrefixes);
        }

        static string FormatDelta(ShieldInfo shield)
        {
            float ghostDelta;
            if (TryGetGhostDelta(shield, out ghostDelta))
                return ShieldValueFormatter.Format(
                    ghostDelta, shield.ValueUnit, shield.UseSiPrefixes);

            if (!shield.IsWorking)
                return DefenseDashboardLocalization.Offline;

            if (shield.HasCapacity && shield.ChargeRatio >= 0.9999f)
                return DefenseDashboardLocalization.FullyCharged;

            if (shield.HasRechargeDelay && shield.TicksUntilRecharge > 0)
            {
                int seconds = Math.Max(1, (shield.TicksUntilRecharge + 59) / 60);
                return DefenseDashboardLocalization.RechargeInSeconds(seconds);
            }

            if (shield.HasRecharge)
            {
                float delta = shield.RechargePointsPerSecond;
                string sign = delta > 0f ? "+" : string.Empty;
                return sign + ShieldValueFormatter.Format(
                    delta, shield.ValueUnit, shield.UseSiPrefixes) + "/s";
            }

            return DefenseDashboardLocalization.RechargeUnavailable;
        }

        Color ResolveDeltaColor(ShieldInfo shield)
        {
            float ghostDelta;
            if (TryGetGhostDelta(shield, out ghostDelta) ||
                !shield.IsWorking || shield.HasRecharge && shield.RechargePointsPerSecond < 0f)
                return Error;
            if (shield.HasCapacity && shield.ChargeRatio >= 0.9999f)
                return Success;
            if (shield.HasRechargeDelay)
                return Warning;
            if (shield.HasRecharge && shield.RechargePointsPerSecond > 0f)
                return Success;
            return Muted;
        }

        static bool TryGetGhostDelta(ShieldInfo shield, out float delta)
        {
            delta = 0f;
            if (!shield.HasGhostCharge || !shield.HasCapacity || shield.MaximumPoints <= 0f ||
                shield.GhostChargeRatio <= shield.ChargeRatio + 0.0001f)
                return false;

            delta = shield.CurrentPoints - shield.GhostChargeRatio * shield.MaximumPoints;
            return delta < 0f;
        }
    }

    /// <summary>A stable tile bound to one weapon block subtype.</summary>
    internal sealed class WeaponStatus : DefenseStatusTile
    {
        string _displayName;
        string _spriteName = "MissingIcon";
        int _total;
        int _ready;
        int _shooting;
        int _warning;
        int _unavailable;

        public WeaponStatus(string subtypeId)
        {
            SubtypeId = subtypeId ?? string.Empty;
            _displayName = SubtypeId;
        }

        public string SubtypeId { get; private set; }

        public void Bind(
            string displayName,
            string spriteName,
            int total,
            int ready,
            int shooting,
            int warning,
            int unavailable,
            Color cardColor,
            Color warningColor,
            Color errorColor)
        {
            SetCardColor(cardColor);
            SetStatusColors(warningColor, errorColor);
            string nextName = string.IsNullOrEmpty(displayName) ? SubtypeId : displayName;
            string nextSprite = string.IsNullOrEmpty(spriteName) ? "MissingIcon" : spriteName;
            bool changed = !string.Equals(_displayName, nextName, StringComparison.Ordinal) ||
                           !string.Equals(_spriteName, nextSprite, StringComparison.Ordinal) ||
                           _total != total || _ready != ready || _shooting != shooting ||
                           _warning != warning || _unavailable != unavailable;
            _displayName = nextName;
            _spriteName = nextSprite;
            _total = Math.Max(0, total);
            _ready = Math.Max(0, ready);
            _shooting = Math.Max(0, shooting);
            _warning = Math.Max(0, warning);
            _unavailable = Math.Max(0, unavailable);
            if (changed)
                MarkDirty();
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            DrawCard(sprites);
            RectangleF rect = Bounds;
            float scale = Math.Max(0.1f, LayoutScale);
            float textScale = Math.Max(0.01f, scale * FontScale);
            float padding = Math.Min(8f * scale, Math.Min(rect.Width * 0.08f, rect.Height * 0.12f));
            float barHeight = Math.Min(Math.Max(3f, 8f * scale), Math.Max(3f, rect.Height * 0.10f));
            var barRect = new RectangleF(
                rect.X + padding,
                rect.Bottom - padding - barHeight,
                Math.Max(1f, rect.Width - padding * 2f),
                barHeight);
            var body = new RectangleF(
                rect.X + padding,
                rect.Y + padding,
                Math.Max(1f, rect.Width - padding * 2f),
                Math.Max(1f, barRect.Y - rect.Y - padding * 2f));
            float titleHeight = Math.Min(
                Math.Max(14f * scale, body.Height * 0.18f),
                body.Height * 0.28f);
            var titleRect = new RectangleF(body.X, body.Y, body.Width, titleHeight);
            DrawText(sprites, _displayName,
                titleRect,
                0.56f * textScale, Foreground);

            float titleGap = Math.Min(3f * scale, Math.Max(0f, body.Height - titleHeight));
            var statusBody = new RectangleF(
                body.X,
                titleRect.Bottom + titleGap,
                body.Width,
                Math.Max(1f, body.Bottom - titleRect.Bottom - titleGap));
            RectangleF iconRect = GetLeftIconRect(statusBody, 0f);
            RectangleF statusRect = GetContentRect(statusBody, iconRect, 0f);

            DrawIcon(sprites, _spriteName, iconRect);
            int rowCount = 0;
            if (_ready > 0) rowCount++;
            if (_shooting > 0) rowCount++;
            if (_warning > 0) rowCount++;
            if (_unavailable > 0) rowCount++;
            float row = (4f - rowCount) * 0.5f;
            if (_ready > 0)
            {
                DrawStatusRow(sprites, GetStatusRow(statusRect, row), _ready, DefenseDashboardLocalization.Ready, Success, textScale);
                row++;
            }
            if (_shooting > 0)
            {
                DrawStatusRow(sprites, GetStatusRow(statusRect, row), _shooting, DefenseDashboardLocalization.Firing, Accent, textScale);
                row++;
            }
            if (_warning > 0)
            {
                DrawStatusRow(sprites, GetStatusRow(statusRect, row), _warning, DefenseDashboardLocalization.NotReady, Warning, textScale);
                row++;
            }
            if (_unavailable > 0)
            {
                DrawStatusRow(sprites, GetStatusRow(statusRect, row), _unavailable, DefenseDashboardLocalization.Disabled, Error, textScale);
            }

            DrawStatusBar(sprites, barRect);
        }

        static RectangleF GetStatusRow(RectangleF rect, float index)
        {
            float rowHeight = rect.Height * 0.25f;
            return new RectangleF(rect.X, rect.Y + rowHeight * index, rect.Width, rowHeight);
        }

        void DrawStatusRow(
            List<MySprite> sprites,
            RectangleF rect,
            int count,
            string label,
            Color color,
            float textScale)
        {
            float dotSize = Math.Min(Math.Max(3f, 7f * LayoutScale), rect.Height * 0.48f);
            float gap = Math.Max(2f, 5f * LayoutScale);
            float dotX = rect.X + dotSize * 0.5f;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(dotX, rect.Center.Y),
                Size = new Vector2(dotSize),
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            float textX = rect.X + dotSize + gap;
            DrawText(
                sprites,
                "(" + count + ") " + label,
                new RectangleF(textX, rect.Y, Math.Max(1f, rect.Right - textX), rect.Height),
                0.50f * textScale,
                Foreground,
                TextAlignment.LEFT);
        }

        void DrawStatusBar(List<MySprite> sprites, RectangleF rect)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            BorderRenderer.CreateSpritesFromRect(
                rect,
                sprites,
                ResolveColor(ThemeResources.SurfaceContainerHighestColor),
                radiusScale: LayoutScale);

            if (_total <= 0)
                return;

            int sectorCount = 0;
            if (_ready > 0) sectorCount++;
            if (_shooting > 0) sectorCount++;
            if (_warning > 0) sectorCount++;
            if (_unavailable > 0) sectorCount++;
            if (sectorCount == 0)
                return;

            float gap = Math.Min(2f * LayoutScale, rect.Width / Math.Max(1f, sectorCount * 2f));
            float availableWidth = Math.Max(0f, rect.Width - gap * (sectorCount - 1));
            float x = rect.X;
            DrawStatusSegment(sprites, rect, _ready, Success, availableWidth, gap, ref x);
            DrawStatusSegment(sprites, rect, _shooting, Accent, availableWidth, gap, ref x);
            DrawStatusSegment(sprites, rect, _warning, Warning, availableWidth, gap, ref x);
            DrawStatusSegment(sprites, rect, _unavailable, Error, availableWidth, gap, ref x);
        }

        void DrawStatusSegment(
            List<MySprite> sprites,
            RectangleF rect,
            int count,
            Color color,
            float availableWidth,
            float gap,
            ref float x)
        {
            if (count <= 0)
                return;

            float width = availableWidth * count / _total;
            BorderRenderer.CreateSpritesFromRect(
                new RectangleF(x, rect.Y, width, rect.Height),
                sprites,
                color,
                radiusScale: LayoutScale);
            x += width + gap;
        }
    }
}
