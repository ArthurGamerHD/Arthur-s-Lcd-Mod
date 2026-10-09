using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.UserControls.Power;
using LcdMod.Client.Helpers;
using Adk.Gui.Core.Input;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.PowerFilled
{
    internal sealed class PowerFilledViewModel : Views.CardGridViewModel
    {
        const float FOOTER_ROW_HEIGHT = 40f;

        readonly PowerFilledApp _app;
        readonly List<PowerDisplayEntry> _compactEntries = new List<PowerDisplayEntry>();
        readonly List<PowerDisplayEntry> _cardEntries = new List<PowerDisplayEntry>();
        readonly List<PowerFooterRow> _footerRows = new List<PowerFooterRow>();
        readonly Dictionary<long, PowerDisplayEntry> _byId = new Dictionary<long, PowerDisplayEntry>();
        readonly Dictionary<PowerCollector, PowerFooterRow> _rowsByCollector = new Dictionary<PowerCollector, PowerFooterRow>();
        bool _isCompact = true;
        bool _hasFooter;
        Color _footerBackground;
        int _entryCount;

        public PowerFilledViewModel(PowerFilledApp app)
        {
            _app = app;
        }

        public IReadOnlyList<PowerDisplayEntry> CompactEntries => _compactEntries;
        public IReadOnlyList<PowerDisplayEntry> CardEntries => _cardEntries;
        public IReadOnlyList<PowerFooterRow> FooterRows => _footerRows;
        public bool IsCompact => _isCompact;
        public bool IsCard => !_isCompact;
        public bool HasFooter => _hasFooter;
        public Color FooterBackground => _footerBackground;

        protected override float MinimumColumnWidth => _isCompact ? 100f : 220f;
        protected override float RowHeight => _isCompact ? 100f : 72f;
        protected override int CardCount => _entryCount;

        public void Refresh()
        {
            bool compact = _app.IsGridDisplay;
            if (RaiseAndSetIfChanged(ref _isCompact, compact, nameof(IsCompact)))
            {
                RaisePropertyChanged<bool>(nameof(IsCard));
                _compactEntries.Clear();
                _cardEntries.Clear();
            }

            List<PowerDisplayEntry> target = compact ? _compactEntries : _cardEntries;
            target.Clear();
            Color foreground = _app.NativeForeground;
            IReadOnlyList<PowerEntry> source = _app.NativeEntries;
            for (int i = 0; i < source.Count; i++)
            {
                PowerEntry power = source[i];
                PowerDisplayEntry entry;
                if (!_byId.TryGetValue(power.EntryId, out entry))
                    _byId[power.EntryId] = entry = new PowerDisplayEntry(power.EntryId, _app.NotifyEntryDetailsOpened);
                entry.Update(power, foreground);
                target.Add(entry);
            }
            _entryCount = target.Count;
            RaisePropertyChanged<IReadOnlyList<PowerDisplayEntry>>(compact ? nameof(CompactEntries) : nameof(CardEntries));
            RefreshFooter(foreground);
            UpdateCardGrid();
        }

        void RefreshFooter(Color foreground)
        {
            float fontSize = _app.SurfaceFontSize;
            float rowHeight = FOOTER_ROW_HEIGHT * fontSize;
            _footerRows.Clear();
            IReadOnlyList<PowerCollector> collectors = _app.NativeCollectors;
            for (int i = 0; i < collectors.Count; i++)
            {
                PowerCollector collector = collectors[i];
                if (collector == null || !collector.HasVisibleItems)
                    continue;
                PowerFooterRow row;
                if (!_rowsByCollector.TryGetValue(collector, out row))
                    _rowsByCollector[collector] = row = new PowerFooterRow();
                row.Update(collector, foreground, rowHeight, .75f * fontSize, 2f);
                _footerRows.Add(row);
            }

            RaiseAndSetIfChanged(ref _hasFooter, _footerRows.Count > 0, nameof(HasFooter));
            RaiseAndSetIfChanged(ref _footerBackground, new Color(_app.NativeBackground.MulValue(.8f), .5f),
                nameof(FooterBackground));
            RaisePropertyChanged<IReadOnlyList<PowerFooterRow>>(nameof(FooterRows));
        }
    }

    internal sealed class PowerDisplayEntry : ObservableObject
    {
        string _icon;
        string _label;
        float _percentage;
        string _percentText;
        string _fillClass;
        Color _fillColor;
        Color _foreground;
        bool _drawCenterIcon;
        float _centerIconRotation;

        readonly Action _detailsOpened;
        readonly List<PowerDetailModel> _details = new List<PowerDetailModel>();
        bool _hasDetails;

        public PowerDisplayEntry(long key, Action detailsOpened)
        {
            Key = key;
            _detailsOpened = detailsOpened;
        }

        public bool HasDetails => _hasDetails;
        public IReadOnlyList<PowerDetailModel> Details => _details;

        internal void NotifyDetailsOpened()
        {
            if (_detailsOpened != null)
                _detailsOpened();
        }
        public long Key { get; private set; }
        public string Icon => _icon;
        public string Label => _label;
        public float Percentage => _percentage;
        public string PercentText => _percentText;
        public string FillClass => _fillClass;
        public Color FillColor => _fillColor;
        public Color Foreground => _foreground;
        public bool DrawCenterIcon => _drawCenterIcon;
        public float CenterIconRotation => _centerIconRotation;

        public void Update(PowerEntry power, Color foreground)
        {
            Set(ref _icon, string.IsNullOrEmpty(power.Icon) ? "MissingIcon" : power.Icon, nameof(Icon));
            Set(ref _label, power.Entity == null ? string.Empty : power.Entity.CustomName ?? string.Empty, nameof(Label));
            RaiseAndSetIfChanged(ref _percentage, power.Ratio, nameof(Percentage));
            Set(ref _percentText, power.PercentText ?? string.Empty, nameof(PercentText));
            Set(ref _fillClass, power.FillClass, nameof(FillClass));
            RaiseAndSetIfChanged(ref _fillColor, power.FillColor, nameof(FillColor));
            RaiseAndSetIfChanged(ref _foreground, foreground, nameof(Foreground));
            RaiseAndSetIfChanged(ref _drawCenterIcon, power.DrawCenterIcon, nameof(DrawCenterIcon));
            RaiseAndSetIfChanged(ref _centerIconRotation, power.CenterIconRotation, nameof(CenterIconRotation));
            RaiseAndSetIfChanged(ref _hasDetails, power.HasDetails, nameof(HasDetails));
            UpdateDetails(power);
        }

        void UpdateDetails(PowerEntry power)
        {
            IList<PowerDetailLine> lines = _hasDetails ? power.GetDetails() : null;
            int count = lines == null ? 0 : lines.Count;
            bool resized = count != _details.Count;
            while (_details.Count < count)
                _details.Add(new PowerDetailModel());
            if (_details.Count > count)
                _details.RemoveRange(count, _details.Count - count);
            for (int i = 0; i < count; i++)
                _details[i].Update(lines[i]);
            if (resized)
                RaisePropertyChanged<IReadOnlyList<PowerDetailModel>>(nameof(Details));
        }

        void Set(ref string field, string value, string name)
        {
            if (string.Equals(field, value, StringComparison.Ordinal)) return;
            field = value;
            RaisePropertyChanged<string>(name);
        }
    }

    internal sealed class PowerFooterRow : ObservableObject
    {
        string _fillClass = string.Empty;
        float _ratio;
        string _chargeText = string.Empty;
        string _statusText = string.Empty;
        string _rightText = string.Empty;
        Color _statusColor;
        Color _rightColor;
        Color _foreground;
        bool _drawCenterIcon;
        float _height;
        float _iconSize;
        float _fontSize = 1f;
        Vector4 _textMargin;

        public string FillClass => _fillClass;
        public float Ratio => _ratio;
        public string ChargeText => _chargeText;
        public string StatusText => _statusText;
        public string RightText => _rightText;
        public Color StatusColor => _statusColor;
        public Color RightColor => _rightColor;
        public Color Foreground => _foreground;
        public bool DrawCenterIcon => _drawCenterIcon;
        public float Height => _height;
        public float IconSize => _iconSize;
        public float FontSize => _fontSize;
        public Vector4 TextMargin => _textMargin;

        public void Update(PowerCollector collector, Color foreground, float height, float fontSize, float textGap)
        {
            float ratio = (float)Math.Round(collector.AverageCharge * 100f, MidpointRounding.AwayFromZero) / 100f;
            RaiseAndSetIfChanged(ref _fillClass, collector.FillClass, nameof(FillClass));
            RaiseAndSetIfChanged(ref _ratio, ratio, nameof(Ratio));
            RaiseAndSetIfChanged(ref _chargeText, FormatingHelper.PercentageToString(ratio) + " " + collector.FooterPrefix,
                nameof(ChargeText));
            RaiseAndSetIfChanged(ref _statusText, collector.StatusText ?? string.Empty, nameof(StatusText));
            RaiseAndSetIfChanged(ref _rightText, collector.HasRightSideText ? collector.RightSideText : string.Empty,
                nameof(RightText));
            RaiseAndSetIfChanged(ref _statusColor, collector.StatusColor, nameof(StatusColor));
            RaiseAndSetIfChanged(ref _rightColor, collector.RightSideColor, nameof(RightColor));
            RaiseAndSetIfChanged(ref _foreground, foreground, nameof(Foreground));
            RaiseAndSetIfChanged(ref _drawCenterIcon, collector.DrawCenterIcon, nameof(DrawCenterIcon));
            RaiseAndSetIfChanged(ref _height, height, nameof(Height));
            RaiseAndSetIfChanged(ref _iconSize, height * .55f, nameof(IconSize));
            RaiseAndSetIfChanged(ref _fontSize, fontSize, nameof(FontSize));
            RaiseAndSetIfChanged(ref _textMargin, new Vector4(textGap, 0f, 0f, 0f), nameof(TextMargin));
        }
    }

    /// <summary>A details line as shown in the entry's flyout: text, or a button for lines with an action.</summary>
    internal sealed class PowerDetailModel : ObservableObject
    {
        PowerDetailLine _line;
        string _text = string.Empty;
        bool _isAction;
        bool _canClick;

        public PowerDetailModel()
        {
            Command = new DelegateCommand(_ =>
            {
                if (_line == null)
                    return;
                _line.Click();
                Update(_line);
            });
        }

        public string Text => _text;
        public bool IsAction => _isAction;
        public bool IsText => !_isAction;
        public bool CanClick => _canClick;
        public ICommand Command { get; }

        internal void Update(PowerDetailLine line)
        {
            _line = line;
            RaiseAndSetIfChanged(ref _text, line.Text, nameof(Text));
            if (RaiseAndSetIfChanged(ref _isAction, line.IsAction, nameof(IsAction)))
                RaisePropertyChanged<bool>(nameof(IsText));
            RaiseAndSetIfChanged(ref _canClick, line.CanClick, nameof(CanClick));
        }
    }
}
