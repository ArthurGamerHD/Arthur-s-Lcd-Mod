using System;
using System.Collections.Generic;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Maps;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Radar
{
    internal sealed class RadarViewModel : ObservableObject, IMapSurface
    {
        const float ORBIT_SENSITIVITY = .009f;

        readonly List<MapMarker> _billboards = new List<MapMarker>();
        Vector2I _canvas;
        int _billboardsVersion;
        string _debugText = string.Empty;
        Color _debugColor;
        bool _hasDebug;
        readonly List<RadarContactRow> _contacts = new List<RadarContactRow>();
        readonly List<RadarContactRow> _rowPool = new List<RadarContactRow>();
        int _version;
        bool _showFooter;
        Color _footerBackground;
        bool _showWarning;
        string _warningText = string.Empty;
        Color _warningColor;
        string _headerTitle = string.Empty;
        string _rangeText = string.Empty;
        Color _headerColor;
        Color _textColor;
        float _headerFontSize = .55f;
        float _warningFontSize = .46f;

        public RadarViewModel()
        {
            WarningIcon = new ImageBrush("Warning") { Stretch = Stretch.Uniform };
        }

        public RadarScene Scene { get; } = new RadarScene();
        public int Version => _version;
        public IMapSurface Surface => this;
        public IReadOnlyList<MapMarker> Billboards => _billboards;
        public int BillboardsVersion => _billboardsVersion;
        public string DebugText => _debugText;
        public Color DebugColor => _debugColor;
        public bool HasDebug => _hasDebug;
        public BackgroundBrush WarningIcon { get; }
        public IReadOnlyList<RadarContactRow> Contacts => _contacts;
        public bool HasContacts => _contacts.Count > 0;
        public bool ShowFooter => _showFooter;
        public Color FooterBackground => _footerBackground;
        public bool ShowWarning => _showWarning;
        public string WarningText => _warningText;
        public Color WarningColor => _warningColor;
        public string HeaderTitle => _headerTitle;
        public string RangeText => _rangeText;
        public Color HeaderColor => _headerColor;
        public Color TextColor => _textColor;
        public float HeaderFontSize => _headerFontSize;
        public float WarningFontSize => _warningFontSize;

        public void InvalidateScene()
        {
            _billboards.Clear();
            RadarRenderer.BuildBillboards(Scene, _canvas, _billboards);
            RaiseAndSetIfChanged(ref _billboardsVersion, _billboardsVersion + 1, nameof(BillboardsVersion));
            RaiseAndSetIfChanged(ref _version, _version + 1, nameof(Version));
        }

        public void SetDebug(string text, Color color)
        {
            RaiseAndSetIfChanged(ref _hasDebug, !string.IsNullOrEmpty(text), nameof(HasDebug));
            RaiseAndSetIfChanged(ref _debugText, text ?? string.Empty, nameof(DebugText));
            RaiseAndSetIfChanged(ref _debugColor, color, nameof(DebugColor));
        }

        void IMapSurface.SetCanvasSize(Vector2I size)
        {
            _canvas = size;
            InvalidateScene();
        }

        void IMapSurface.Paint(PixelCanvas canvas)
        {
            RadarRenderer.Render(canvas, Scene);
        }

        void IMapSurface.Drag(Vector2 delta)
        {
            if (Scene.Camera != null)
                Scene.Camera.Orbit(new Vector2(-delta.X, delta.Y) * ORBIT_SENSITIVITY);
        }

        void IMapSurface.MiddleDrag(Vector2 delta)
        {
            if (Scene.Camera == null || _canvas.X <= 0 || _canvas.Y <= 0)
                return;
            RadarCamera camera = RadarCamera.Create(Scene, _canvas);
            float metersPerPixel = camera.Distance / camera.Focal;
            Vector2 groundRight = Flatten(camera.Right);
            Vector2 groundUp = Flatten(camera.Up);
            float foreshortening = Math.Max(.2f, (float)Math.Sin(MathHelper.Clamp(Scene.Pitch, RadarCamera.MINIMUM_PITCH,
                RadarCamera.MAXIMUM_PITCH)));
            Scene.Camera.Pan((-delta.X * groundRight + delta.Y * groundUp / foreshortening) * metersPerPixel);
        }

        static Vector2 Flatten(Vector3 direction)
        {
            var flat = new Vector2(direction.X, direction.Y);
            return flat.LengthSquared() <= 1e-8f ? Vector2.Zero : Vector2.Normalize(flat);
        }

        void IMapSurface.MiddleClick(Vector2 pixel)
        {
        }

        void IMapSurface.Click(Vector2 pixel)
        {
        }

        void IMapSurface.DoubleClick(Vector2 pixel)
        {
            if (Scene.Camera != null)
                Scene.Camera.ResetView();
        }

        bool IMapSurface.Scroll(int delta)
        {
            return Scene.Camera != null && delta != 0 && Scene.Camera.Scroll(delta);
        }

        void IMapSurface.MarkerClicked(MapMarker marker)
        {
        }

        void IMapSurface.StreetView(Vector2 pixel)
        {
        }

        public void SetFontScale(float fontScale)
        {
            RaiseAndSetIfChanged(ref _headerFontSize, .55f * fontScale, nameof(HeaderFontSize));
            RaiseAndSetIfChanged(ref _warningFontSize, .46f * fontScale, nameof(WarningFontSize));
        }

        public void SetWarning(string text, Color color)
        {
            RaiseAndSetIfChanged(ref _showWarning, text != null, nameof(ShowWarning));
            RaiseAndSetIfChanged(ref _warningText, text ?? string.Empty, nameof(WarningText));
            RaiseAndSetIfChanged(ref _warningColor, color, nameof(WarningColor));
        }

        public void SetHeader(string title, string range, Color headerColor, Color textColor)
        {
            RaiseAndSetIfChanged(ref _headerTitle, title ?? string.Empty, nameof(HeaderTitle));
            RaiseAndSetIfChanged(ref _rangeText, range ?? string.Empty, nameof(RangeText));
            RaiseAndSetIfChanged(ref _headerColor, headerColor, nameof(HeaderColor));
            RaiseAndSetIfChanged(ref _textColor, textColor, nameof(TextColor));
        }

        public RadarContactRow ContactRow(int index)
        {
            while (_rowPool.Count <= index)
                _rowPool.Add(new RadarContactRow());
            return _rowPool[index];
        }

        public void SetContactCount(int count)
        {
            if (count == _contacts.Count)
                return;
            bool had = HasContacts;
            _contacts.Clear();
            for (int i = 0; i < count; i++)
                _contacts.Add(ContactRow(i));
            RaisePropertyChanged<IReadOnlyList<RadarContactRow>>(nameof(Contacts));
            if (had != HasContacts)
                RaisePropertyChanged<bool>(nameof(HasContacts));
        }

        public void SetFooter(bool visible, Color background)
        {
            RaiseAndSetIfChanged(ref _showFooter, visible, nameof(ShowFooter));
            RaiseAndSetIfChanged(ref _footerBackground, background, nameof(FooterBackground));
        }
    }

    internal sealed class RadarContactRow : ObservableObject
    {
        string _icon;
        BackgroundBrush _iconMask;
        Color _iconColor;
        string _text = string.Empty;
        float _fontSize = .5f;

        public BackgroundBrush IconMask => _iconMask;
        public Color IconColor => _iconColor;
        public string Text => _text;
        public float FontSize => _fontSize;

        public void Update(string icon, string text, Color iconColor, float fontSize)
        {
            if (_icon != icon)
            {
                _icon = icon;
                _iconMask = string.IsNullOrEmpty(icon) ? null : new ImageBrush(icon) { Stretch = Stretch.Uniform };
                RaisePropertyChanged<BackgroundBrush>(nameof(IconMask));
            }
            RaiseAndSetIfChanged(ref _iconColor, iconColor, nameof(IconColor));
            RaiseAndSetIfChanged(ref _text, text ?? string.Empty, nameof(Text));
            RaiseAndSetIfChanged(ref _fontSize, fontSize, nameof(FontSize));
        }
    }
}
