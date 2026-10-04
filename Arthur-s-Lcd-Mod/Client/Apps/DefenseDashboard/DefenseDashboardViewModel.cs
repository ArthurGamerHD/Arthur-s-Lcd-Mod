using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Controls;
using LcdMod.Client.Extensions;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Defense;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.DefenseDashboard
{
    internal sealed class DefenseDashboardViewModel : ObservableObject
    {
        readonly DefenseDashboardApp _app;
        readonly List<ShieldStatusEntry> _shields = new List<ShieldStatusEntry>();
        readonly List<WeaponStatusEntry> _weapons = new List<WeaponStatusEntry>();
        readonly Dictionary<string, ShieldStatusEntry> _shieldByKey = new Dictionary<string, ShieldStatusEntry>(StringComparer.Ordinal);
        readonly Dictionary<string, WeaponStatusEntry> _weaponByKey = new Dictionary<string, WeaponStatusEntry>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _shieldKeys = new HashSet<string>(StringComparer.Ordinal);

        public DefenseDashboardViewModel(DefenseDashboardApp app)
        {
            _app = app;
        }

        public IReadOnlyList<ShieldStatusEntry> Shields => _shields;
        public IReadOnlyList<WeaponStatusEntry> Weapons => _weapons;
        public bool HasShields => _shields.Count > 0;
        public bool HasWeapons => _weapons.Count > 0;

        public void Refresh()
        {
            var palette = new DefensePalette(_app);
            SyncShields(palette);
            SyncWeapons(palette);
            RaisePropertyChanged<IReadOnlyList<ShieldStatusEntry>>(nameof(Shields));
            RaisePropertyChanged<IReadOnlyList<WeaponStatusEntry>>(nameof(Weapons));
            RaisePropertyChanged<bool>(nameof(HasShields));
            RaisePropertyChanged<bool>(nameof(HasWeapons));
        }

        void SyncShields(DefensePalette palette)
        {
            _shields.Clear();
            _shieldKeys.Clear();
            IReadOnlyList<ShieldInfo> source = _app.NativeShields;
            for (int i = 0; i < source.Count; i++)
            {
                ShieldInfo shield = source[i];
                if (shield == null) continue;
                string baseKey = string.IsNullOrEmpty(shield.ProviderName) ? "Shield" : shield.ProviderName;
                string key = baseKey;
                int suffix = 2;
                while (!_shieldKeys.Add(key))
                    key = baseKey + "#" + suffix++;
                ShieldStatusEntry entry;
                if (!_shieldByKey.TryGetValue(key, out entry))
                    _shieldByKey[key] = entry = new ShieldStatusEntry();
                entry.Update(shield, palette);
                _shields.Add(entry);
            }
        }

        void SyncWeapons(DefensePalette palette)
        {
            _weapons.Clear();
            IReadOnlyList<DefenseDashboardApp.WeaponAggregate> source = _app.NativeWeapons;
            for (int i = 0; i < source.Count; i++)
            {
                DefenseDashboardApp.WeaponAggregate weapon = source[i];
                WeaponStatusEntry entry;
                if (!_weaponByKey.TryGetValue(weapon.SubtypeId, out entry))
                    _weaponByKey[weapon.SubtypeId] = entry = new WeaponStatusEntry();
                entry.Update(weapon, palette);
                _weapons.Add(entry);
            }
        }
    }

    internal struct DefensePalette
    {
        public readonly Color Card;
        public readonly Color Shadow;
        public readonly Color Foreground;
        public readonly Color Muted;
        public readonly Color Accent;
        public readonly Color Success;
        public readonly Color Warning;
        public readonly Color Error;
        public readonly Color Track;
        public readonly float FontSize;
        public readonly string Font;

        public DefensePalette(DefenseDashboardApp app)
        {
            Card = app.NativeCardColor;
            Shadow = Card.MulValue(.2f);
            Foreground = app.NativeForegroundColor;
            Muted = app.NativeMutedColor;
            Accent = app.NativeAccentColor;
            Success = app.NativeSuccessColor;
            Warning = app.NativeWarningColor;
            Error = app.NativeErrorColor;
            Track = app.NativeTrackColor;
            FontSize = app.SurfaceFontSize;
            Font = app.NativeTextFont;
        }
    }

    internal abstract class DefenseCardEntry : ObservableObject
    {
        Color _cardColor;
        Color _shadowColor;
        Color _foreground;
        Color _trackColor;

        public Color CardColor => _cardColor;
        public Color ShadowColor => _shadowColor;
        public Color Foreground => _foreground;
        public Color TrackColor => _trackColor;

        protected void UpdateCard(DefensePalette palette)
        {
            Set(ref _cardColor, palette.Card, nameof(CardColor));
            Set(ref _shadowColor, palette.Shadow, nameof(ShadowColor));
            Set(ref _foreground, palette.Foreground, nameof(Foreground));
            Set(ref _trackColor, palette.Track, nameof(TrackColor));
        }

        protected void Set<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            RaisePropertyChanged<T>(propertyName);
        }
    }

    internal sealed class ShieldStatusEntry : DefenseCardEntry
    {
        string _title = string.Empty;
        string _chargeText = string.Empty;
        string _deltaText = string.Empty;
        string _chargeFont = "White";
        float _sweep;
        float _ghostSweep;
        bool _hasGhost;
        Color _statusColor;
        Color _ghostColor;
        Color _deltaColor;
        float _titleFontSize;
        float _chargeFontSize;
        float _deltaFontSize;

        public string Title => _title;
        public string ChargeText => _chargeText;
        public string DeltaText => _deltaText;
        public string ChargeFont => _chargeFont;
        public float Sweep => _sweep;
        public float GhostSweep => _ghostSweep;
        public bool HasGhost => _hasGhost;
        public Color StatusColor => _statusColor;
        public Color GhostColor => _ghostColor;
        public Color DeltaColor => _deltaColor;
        public float TitleFontSize => _titleFontSize;
        public float ChargeFontSize => _chargeFontSize;
        public float DeltaFontSize => _deltaFontSize;

        public void Update(ShieldInfo shield, DefensePalette palette)
        {
            UpdateCard(palette);
            float ratio = shield.HasCapacity && shield.MaximumPoints > 0f
                ? MathHelper.Clamp(shield.CurrentPoints / shield.MaximumPoints, 0f, 1f)
                : 0f;
            float rendered = shield.HasCapacity ? ratio : (shield.IsWorking ? 1f : 0f);
            Color status = ResolveStatusColor(shield, ratio, palette);
            bool ghost = shield.HasGhostCharge && shield.GhostChargeRatio > rendered + .0001f;

            Set(ref _title, string.IsNullOrEmpty(shield.ProviderName)
                ? DefenseDashboardLocalization.ShieldFallback
                : shield.ProviderName, nameof(Title));
            Set(ref _chargeText, FormatCharge(shield), nameof(ChargeText));
            Set(ref _deltaText, FormatDelta(shield), nameof(DeltaText));
            Set(ref _chargeFont, SupportsBoldFont(palette.Font) ? "White-Bold" : palette.Font, nameof(ChargeFont));
            Set(ref _sweep, rendered * 360f, nameof(Sweep));
            Set(ref _ghostSweep, ghost ? MathHelper.Clamp(shield.GhostChargeRatio, 0f, 1f) * 360f : 0f, nameof(GhostSweep));
            Set(ref _hasGhost, ghost, nameof(HasGhost));
            Set(ref _statusColor, status, nameof(StatusColor));
            Set(ref _ghostColor, status.MulSaturation(.25f).MulValue(.25f), nameof(GhostColor));
            Set(ref _deltaColor, ResolveDeltaColor(shield, palette), nameof(DeltaColor));
            Set(ref _titleFontSize, .5f * palette.FontSize, nameof(TitleFontSize));
            Set(ref _chargeFontSize, .78f * palette.FontSize, nameof(ChargeFontSize));
            Set(ref _deltaFontSize, .48f * palette.FontSize, nameof(DeltaFontSize));
        }

        static bool SupportsBoldFont(string font)
        {
            return string.Equals(font, "White", StringComparison.Ordinal) ||
                   string.Equals(font, "Debug", StringComparison.Ordinal);
        }

        static Color ResolveStatusColor(ShieldInfo shield, float ratio, DefensePalette palette)
        {
            if (!shield.IsWorking || shield.HasCapacity && ratio <= .15f)
                return palette.Error;
            if (shield.HasCapacity && ratio <= .35f)
                return palette.Warning;
            return palette.Accent;
        }

        static string FormatCharge(ShieldInfo shield)
        {
            string current = ShieldValueFormatter.Format(shield.CurrentPoints, shield.ValueUnit, shield.UseSiPrefixes);
            if (!shield.HasCapacity)
                return current;
            return current + " / " + ShieldValueFormatter.Format(shield.MaximumPoints, shield.ValueUnit, shield.UseSiPrefixes);
        }

        static string FormatDelta(ShieldInfo shield)
        {
            float ghostDelta;
            if (TryGetGhostDelta(shield, out ghostDelta))
                return ShieldValueFormatter.Format(ghostDelta, shield.ValueUnit, shield.UseSiPrefixes);

            if (!shield.IsWorking)
                return DefenseDashboardLocalization.Offline;

            if (shield.HasCapacity && shield.ChargeRatio >= .9999f)
                return DefenseDashboardLocalization.FullyCharged;

            if (shield.HasRechargeDelay && shield.TicksUntilRecharge > 0)
                return DefenseDashboardLocalization.RechargeInSeconds(Math.Max(1, (shield.TicksUntilRecharge + 59) / 60));

            if (shield.HasRecharge)
            {
                float delta = shield.RechargePointsPerSecond;
                return (delta > 0f ? "+" : string.Empty) +
                       ShieldValueFormatter.Format(delta, shield.ValueUnit, shield.UseSiPrefixes) + "/s";
            }

            return DefenseDashboardLocalization.RechargeUnavailable;
        }

        static Color ResolveDeltaColor(ShieldInfo shield, DefensePalette palette)
        {
            float ghostDelta;
            if (TryGetGhostDelta(shield, out ghostDelta) ||
                !shield.IsWorking || shield.HasRecharge && shield.RechargePointsPerSecond < 0f)
                return palette.Error;
            if (shield.HasCapacity && shield.ChargeRatio >= .9999f)
                return palette.Success;
            if (shield.HasRechargeDelay)
                return palette.Warning;
            if (shield.HasRecharge && shield.RechargePointsPerSecond > 0f)
                return palette.Success;
            return palette.Muted;
        }

        static bool TryGetGhostDelta(ShieldInfo shield, out float delta)
        {
            delta = 0f;
            if (!shield.HasGhostCharge || !shield.HasCapacity || shield.MaximumPoints <= 0f ||
                shield.GhostChargeRatio <= shield.ChargeRatio + .0001f)
                return false;

            delta = shield.CurrentPoints - shield.GhostChargeRatio * shield.MaximumPoints;
            return delta < 0f;
        }
    }

    internal sealed class WeaponStatusEntry : DefenseCardEntry
    {
        readonly List<WeaponStatusRow> _rows = new List<WeaponStatusRow>();
        readonly WeaponStatusRow[] _rowPool = { new WeaponStatusRow(), new WeaponStatusRow(), new WeaponStatusRow(), new WeaponStatusRow() };
        BarSegment[] _segments = new BarSegment[0];
        string _title = string.Empty;
        string _icon = "MissingIcon";
        float _total;
        float _titleFontSize;

        public string Title => _title;
        public string Icon => _icon;
        public IReadOnlyList<WeaponStatusRow> Rows => _rows;
        public IReadOnlyList<BarSegment> Segments => _segments;
        public float Total => _total;
        public float TitleFontSize => _titleFontSize;

        public void Update(DefenseDashboardApp.WeaponAggregate weapon, DefensePalette palette)
        {
            UpdateCard(palette);
            Set(ref _title, string.IsNullOrEmpty(weapon.DisplayName) ? weapon.SubtypeId : weapon.DisplayName, nameof(Title));
            Set(ref _icon, string.IsNullOrEmpty(weapon.SpriteName) ? "MissingIcon" : weapon.SpriteName, nameof(Icon));
            Set(ref _total, (float)Math.Max(0, weapon.Total), nameof(Total));
            Set(ref _titleFontSize, .56f * palette.FontSize, nameof(TitleFontSize));

            var segments = new[]
            {
                new BarSegment(weapon.Ready, palette.Success),
                new BarSegment(weapon.Shooting, palette.Accent),
                new BarSegment(weapon.Warning, palette.Warning),
                new BarSegment(weapon.Unavailable, palette.Error)
            };
            if (!SameSegments(segments))
            {
                _segments = segments;
                RaisePropertyChanged<IReadOnlyList<BarSegment>>(nameof(Segments));
            }

            int count = _rows.Count;
            _rows.Clear();
            float rowFontSize = .5f * palette.FontSize;
            AddRow(weapon.Ready, DefenseDashboardLocalization.Ready, palette.Success, palette.Foreground, rowFontSize);
            AddRow(weapon.Shooting, DefenseDashboardLocalization.Firing, palette.Accent, palette.Foreground, rowFontSize);
            AddRow(weapon.Warning, DefenseDashboardLocalization.NotReady, palette.Warning, palette.Foreground, rowFontSize);
            AddRow(weapon.Unavailable, DefenseDashboardLocalization.Disabled, palette.Error, palette.Foreground, rowFontSize);
            if (count != _rows.Count)
                RaisePropertyChanged<IReadOnlyList<WeaponStatusRow>>(nameof(Rows));
        }

        void AddRow(int count, string label, Color dot, Color foreground, float fontSize)
        {
            if (count <= 0)
                return;
            WeaponStatusRow row = _rowPool[_rows.Count];
            row.Update("(" + count + ") " + label, dot, foreground, fontSize);
            _rows.Add(row);
        }

        bool SameSegments(BarSegment[] segments)
        {
            if (_segments.Length != segments.Length)
                return false;
            for (int i = 0; i < segments.Length; i++)
                if (!_segments[i].Equals(segments[i]))
                    return false;
            return true;
        }
    }

    internal sealed class WeaponStatusRow : ObservableObject
    {
        string _text = string.Empty;
        Color _dotColor;
        Color _foreground;
        float _fontSize;

        public string Text => _text;
        public Color DotColor => _dotColor;
        public Color Foreground => _foreground;
        public float FontSize => _fontSize;

        public void Update(string text, Color dot, Color foreground, float fontSize)
        {
            RaiseAndSetIfChanged(ref _text, text, nameof(Text));
            RaiseAndSetIfChanged(ref _dotColor, dot, nameof(DotColor));
            RaiseAndSetIfChanged(ref _foreground, foreground, nameof(Foreground));
            RaiseAndSetIfChanged(ref _fontSize, fontSize, nameof(FontSize));
        }
    }
}
