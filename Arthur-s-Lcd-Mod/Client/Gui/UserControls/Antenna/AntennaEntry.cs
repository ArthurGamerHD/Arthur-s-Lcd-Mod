using LcdMod.Common.Mvvm;
using LcdMod.Client.Extensions;
using VRageMath;

namespace LcdMod.Client.Gui.UserControls.Antenna
{
    internal sealed class AntennaEntry : ObservableObject
    {
        string _name;
        string _statusIcon;
        string _statusText;
        Color _statusColor;
        Color _statusForeground;
        bool _useLaserIconCompensation;
        bool _isFunctional;
        bool _drawAsLines;

        public long EntryId { get; private set; }
        public string Name { get { return _name; } }
        public string StatusIcon { get { return _statusIcon; } }
        public string StatusText { get { return _statusText; } }
        public Color StatusColor { get { return _statusColor; } }
        public Color StatusForeground { get { return _statusForeground; } }
        public bool UseLaserIconCompensation { get { return _useLaserIconCompensation; } }
        public bool IsFunctional { get { return _isFunctional; } }
        public bool DrawAsLines
        {
            get { return _drawAsLines; }
            set
            {
                if (_drawAsLines == value)
                    return;
                _drawAsLines = value;
                RaisePropertyChanged<bool>(nameof(DrawAsLines));
            }
        }

        const float LaserIconSourceSize = 190f;
        const float LaserIconTopPadding = 64f;
        const float LaserIconBottomPadding = 22f;
        const float IconSize = 110f;
        static readonly Color Transparent = new Color(0, 0, 0, 0);
        Color _cardBackground;
        Color _cardShadow;
        Color _textColor;
        Color _lineColor;
        Vector4 _lineThickness;
        Vector4 _iconMargin;

        public Color CardBackground { get { return _cardBackground; } }
        public Color CardShadow { get { return _cardShadow; } }
        public Color TextColor { get { return _textColor; } }
        public Color LineColor { get { return _lineColor; } }
        public Vector4 LineThickness { get { return _lineThickness; } }
        public Vector4 IconMargin { get { return _iconMargin; } }

        public void ApplyLayout(bool drawAsCard, bool listLines, bool gridLines, Color headerColor, Color errorColor,
            Color foreground)
        {
            Color background = drawAsCard ? (_isFunctional ? headerColor : errorColor) : Transparent;
            Vector3 hsv = background.ColorToHSV();
            hsv.Z *= 0.2f;
            Set(ref _cardBackground, background, nameof(CardBackground));
            Set(ref _cardShadow, drawAsCard ? hsv.HSVtoColor() : Transparent, nameof(CardShadow));
            Set(ref _textColor, drawAsCard ? foreground : _statusColor, nameof(TextColor));
            Set(ref _lineColor, gridLines ? new Color(headerColor.R, headerColor.G, headerColor.B) : foreground,
                nameof(LineColor));
            Set(ref _lineThickness, gridLines ? new Vector4(1f) : listLines ? new Vector4(0f, 1f, 0f, 1f) : Vector4.Zero,
                nameof(LineThickness));

            float offset = 0f;
            bool skipOffset = _statusIcon == "RotationPlane" || _statusIcon == "GridPower" || _statusIcon == "Search";
            if (_useLaserIconCompensation && !skipOffset)
                offset = -IconSize * ((LaserIconTopPadding - LaserIconBottomPadding) * 0.5f / LaserIconSourceSize);
            Set(ref _iconMargin, new Vector4(0f, offset, 0f, -offset), nameof(IconMargin));
        }

        public AntennaEntry(long entryId)
        {
            EntryId = entryId;
        }

        public void Update(
            string name,
            string statusIcon,
            string statusText,
            Color statusColor,
            bool isFunctional,
            bool useLaserIconCompensation)
        {
            Set(ref _name, name ?? string.Empty, nameof(Name));
            Set(ref _statusIcon, statusIcon ?? string.Empty, nameof(StatusIcon));
            Set(ref _statusText, statusText ?? string.Empty, nameof(StatusText));
            if (Set(ref _statusColor, statusColor, nameof(StatusColor)))
            {
                var foreground = statusColor.ContrastRatio(Color.Black) >=
                                 statusColor.ContrastRatio(Color.White)
                    ? Color.Black
                    : Color.White;
                Set(ref _statusForeground, foreground, nameof(StatusForeground));
            }
            Set(ref _isFunctional, isFunctional, nameof(IsFunctional));
            Set(ref _useLaserIconCompensation, useLaserIconCompensation,
                nameof(UseLaserIconCompensation));
        }

        bool Set<T>(ref T field, T value, string propertyName)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            RaisePropertyChanged<T>(propertyName);
            return true;
        }
    }
}
