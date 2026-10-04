using System;
using Adk.Gui.Styling;
using LcdMod.Client.Extensions;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.DockingAlignment
{
    // One model for the lifetime of the screen. Layout notifications are separate
    // from telemetry, which only invalidates bindings whose values changed.
    internal sealed class DockingAlignmentViewModel : ObservableObject
    {
        Vector2 _viewport;
        Vector3D _position;

        public Vector3D Position
        {
            get { return _position; }
            private set { RaiseAndSetIfChanged(ref _position, value, nameof(Position)); }
        }

        Vector3D _rotation;

        public Vector3D Rotation
        {
            get { return _rotation; }
            private set { RaiseAndSetIfChanged(ref _rotation, value, nameof(Rotation)); }
        }

        Vector3D _velocity;

        public Vector3D Velocity
        {
            get { return _velocity; }
            private set { RaiseAndSetIfChanged(ref _velocity, value, nameof(Velocity)); }
        }

        Color _statusColor;

        public Color StatusColor
        {
            get { return _statusColor; }
            private set { RaiseAndSetIfChanged(ref _statusColor, value, nameof(StatusColor)); }
        }

        bool _hasTarget;

        public bool HasTarget
        {
            get { return _hasTarget; }
            private set { RaiseAndSetIfChanged(ref _hasTarget, value, nameof(HasTarget)); }
        }

        string _warning;

        public string Warning
        {
            get { return _warning; }
        }

        string _referenceName;

        public string ReferenceName
        {
            get { return _referenceName; }
            private set { RaiseAndSetIfChanged(ref _referenceName, value, nameof(ReferenceName)); }
        }

        string _targetName;

        public string TargetName
        {
            get { return _targetName; }
            private set { RaiseAndSetIfChanged(ref _targetName, value, nameof(TargetName)); }
        }

        double _pitch;

        public double Pitch
        {
            get { return _pitch; }
            private set { RaiseAndSetIfChanged(ref _pitch, value, nameof(Pitch)); }
        }

        double _yaw;

        public double Yaw
        {
            get { return _yaw; }
            private set { RaiseAndSetIfChanged(ref _yaw, value, nameof(Yaw)); }
        }

        double _roll;

        public double Roll
        {
            get { return _roll; }
            private set { RaiseAndSetIfChanged(ref _roll, value, nameof(Roll)); }
        }

        double _leftRight;

        public double LeftRight
        {
            get { return _leftRight; }
            private set { RaiseAndSetIfChanged(ref _leftRight, value, nameof(LeftRight)); }
        }

        double _forwardBack;

        public double ForwardBack
        {
            get { return _forwardBack; }
            private set { RaiseAndSetIfChanged(ref _forwardBack, value, nameof(ForwardBack)); }
        }

        double _upDown;

        public double UpDown
        {
            get { return _upDown; }
            private set { RaiseAndSetIfChanged(ref _upDown, value, nameof(UpDown)); }
        }

        double _distance;

        public double Distance
        {
            get { return _distance; }
            private set { RaiseAndSetIfChanged(ref _distance, value, nameof(Distance)); }
        }

        double _axisDistance;

        public double AxisDistance
        {
            get { return _axisDistance; }
            private set { RaiseAndSetIfChanged(ref _axisDistance, value, nameof(AxisDistance)); }
        }

        double _distanceRate;

        public double DistanceRate
        {
            get { return _distanceRate; }
            private set { RaiseAndSetIfChanged(ref _distanceRate, value, nameof(DistanceRate)); }
        }

        double _closingVelocity;

        public double ClosingVelocity
        {
            get { return _closingVelocity; }
            private set { RaiseAndSetIfChanged(ref _closingVelocity, value, nameof(ClosingVelocity)); }
        }

        double _footerRoll;

        public double FooterRoll
        {
            get { return _footerRoll; }
            private set { RaiseAndSetIfChanged(ref _footerRoll, value, nameof(FooterRoll)); }
        }

        public float FontScale { get; private set; } = 1f;
        public float RenderScale { get; private set; } = 1f;
        public string FontFamily { get; private set; } = "White";
        public Color Foreground { get; private set; } = Color.White;
        public Color Accent { get; private set; } = Color.White;
        public Color ErrorColor { get; private set; } = Color.Red;
        public BackgroundBrush FooterBackground { get; private set; }
        public BackgroundBrush WarningBrush { get; private set; }
        public BackgroundBrush WarningShadowBrush { get; private set; }
        public Color WarningColor { get; private set; }
        public Color WarningShadowColor { get; private set; }
        public string PitchLabel { get; private set; }
        public string YawLabel { get; private set; }
        public string RollLabel { get; private set; }

        public float FooterHeight => 58f * FontScale;

        // The legacy size tier was selected in physical pixels; keep that tier
        // while publishing logical font sizes for the window's single DPI pass.
        public float InstrumentFont => MathHelper.Clamp(Math.Min(_viewport.X,
            Math.Max(0f, _viewport.Y - FooterHeight)) * RenderScale / 560f, .45f, .95f) * FontScale;

        public float LabelFont => InstrumentFont * .65f;
        public float NameFont => InstrumentFont * .75f;
        public float FooterFont => Math.Max(.36f / RenderScale, .52f) * FontScale;
        public bool HasWarning => !string.IsNullOrEmpty(_warning);

        public void SetViewport(Vector2 viewport)
        {
            if (_viewport == viewport) return;
            _viewport = viewport;
            RaisePropertyChanged<object>(string.Empty);
        }

        public void RefreshLayout(float fontScale, float renderScale, string font, Color foreground,
            Color accent, Color background, Color warning, Color error, string pitch, string yaw, string roll)
        {
            FontScale = Math.Max(.01f, fontScale);
            RenderScale = renderScale > 0f ? renderScale : 1f;
            FontFamily = font;
            Foreground = foreground;
            Accent = accent;
            ErrorColor = error;
            WarningColor = warning;
            WarningBrush = new SolidColorBrush(warning);
            WarningShadowColor = warning.MulValue(.2f);
            WarningShadowBrush = new SolidColorBrush(WarningShadowColor);
            FooterBackground = new SolidColorBrush(new Color(background.MulValue(.8f), .5f));
            PitchLabel = pitch;
            YawLabel = yaw;
            RollLabel = roll;
            RaisePropertyChanged<object>(string.Empty);
        }

        public void ShowWarning(string message, bool hideTarget)
        {
            if (hideTarget) RaiseAndSetIfChanged(ref _hasTarget, false, nameof(HasTarget));
            if (RaiseAndSetIfChanged(ref _warning, message, nameof(Warning)))
                RaisePropertyChanged<bool>(nameof(HasWarning));
        }

        public void Update(Vector3D position, Vector3D rotation, Vector3D velocity, Color statusColor,
            double axisDistance, double distanceRate, double closingVelocity, string referenceName, string targetName)
        {
            Position = position;
            Rotation = rotation;
            Velocity = velocity;
            StatusColor = statusColor;
            HasTarget = true;
            ReferenceName = referenceName;
            TargetName = targetName;
            Pitch = rotation.X;
            Yaw = rotation.Y;
            Roll = rotation.Z;
            LeftRight = -position.X;
            ForwardBack = position.Z;
            UpDown = -position.Y;
            Distance = position.Length();
            AxisDistance = axisDistance;
            DistanceRate = distanceRate;
            ClosingVelocity = closingVelocity;
            FooterRoll = rotation.Z;
        }
    }
}
