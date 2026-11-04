using System;
using System.Collections.Generic;
using System.Globalization;
using Acrux.Core.Input;
using LcdMod.Client.Apps.Maps;
using LcdMod.Client.Apps.Views;
using LcdMod.Client.Extensions;
using LcdMod.Client.Helpers;
using Acrux.Cartography;
using Acrux.Cartography.Maps;
using LcdMod.Common.Config.Components;
using Adk.Mvvm;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps.StarMap
{
    internal interface IStarMapHost : IMapHost, IMarkerColors
    {
        StarMapConfigComponent Config { get; }
        IMyCubeBlock Block { get; }
        string Title { get; }
        Color ForegroundColor { get; }
        bool HasJumpDrives { get; }
        bool TryGetJumpPoint(PlanetSnapshot planet, double gravityRange, out Vector3D jumpPoint);
        void SyncConfig();
    }

    internal sealed class StarMapInfoLine : ObservableObject
    {
        string _text = string.Empty;

        [PreviewWith("Radius: 60.0 km", "Gravity: 1.00 g", "Atmosphere: breathable", "Distance: 1.24 Mm")]
        public string Text => _text;

        internal void Update(string text)
        {
            RaiseAndSetIfChanged(ref _text, text ?? string.Empty, nameof(Text));
        }
    }

    internal sealed class StarMapPlanetInfo : ObservableObject
    {
        readonly List<StarMapInfoLine> _lines = new List<StarMapInfoLine>();
        string _name = string.Empty;
        string _positionText = string.Empty;
        string _jumpText = string.Empty;
        readonly Action _dismiss;
        bool _canJump;
        bool _isVisible;
        bool _isOpen;

        public StarMapPlanetInfo(Action position, Action jump, Action dismiss)
        {
            PositionCommand = new DelegateCommand(_ => position());
            JumpCommand = new DelegateCommand(_ => jump());
            _dismiss = dismiss;
        }

        // The planet's centre on the map, normalized to the canvas.
        public Vector2 AnchorCenter { get; private set; }

        // The planet's radius on the map, normalized to the canvas width.
        public float AnchorRadius { get; private set; }

        // True when the user picked the planet, so clicking elsewhere dismisses it.
        public bool IsPinned { get; private set; }

        internal void Dismiss()
        {
            if (_dismiss != null)
                _dismiss();
        }

        internal void SetAnchor(Vector2 center, float radius, bool pinned)
        {
            AnchorCenter = center;
            AnchorRadius = radius;
            IsPinned = pinned;
        }

        [PreviewWith("Earthlike")]
        public string Name => _name;
        [PreviewWith(4)]
        public IReadOnlyList<StarMapInfoLine> Lines => _lines;
        [PreviewWith("Create GPS")]
        public string PositionText => _positionText;
        [PreviewWith("Jump here")]
        public string JumpText => _jumpText;
        [PreviewWith(true)]
        public bool CanJump => _canJump;
        [PreviewWith(true)]
        public bool IsVisible => _isVisible;
        [PreviewWith(true)]
        public bool IsOpen => _isOpen;
        public event Action Opened;

        internal void SetOpen(bool open)
        {
            if (RaiseAndSetIfChanged(ref _isOpen, open, nameof(IsOpen)) && open)
                Opened?.Invoke();
        }
        public ICommand PositionCommand { get; }
        public ICommand JumpCommand { get; }

        internal void Update(string name, List<string> lines, string position, string jump, bool canJump)
        {
            RaiseAndSetIfChanged(ref _name, name, nameof(Name));
            if (lines.Count != _lines.Count)
            {
                while (_lines.Count < lines.Count)
                    _lines.Add(new StarMapInfoLine());
                _lines.RemoveRange(lines.Count, _lines.Count - lines.Count);
                RaisePropertyChanged<IReadOnlyList<StarMapInfoLine>>(nameof(Lines));
            }
            for (int i = 0; i < lines.Count; i++)
                _lines[i].Update(lines[i]);
            RaiseAndSetIfChanged(ref _positionText, position, nameof(PositionText));
            RaiseAndSetIfChanged(ref _jumpText, jump, nameof(JumpText));
            RaiseAndSetIfChanged(ref _canJump, canJump, nameof(CanJump));
            RaiseAndSetIfChanged(ref _isVisible, true, nameof(IsVisible));
        }

        internal void Hide()
        {
            RaiseAndSetIfChanged(ref _isVisible, false, nameof(IsVisible));
        }
    }

    /// <summary>
    /// Star Map backported from the standalone Minimap: the orbit (Planetarium) view, or the same renderer driven from
    /// the reference block as a first-person HUD with an artificial horizon on top.
    /// </summary>
    [PreviewWith("Message.IsVisible", false)]
    [PreviewWith("IsStarMap", true)]
    [PreviewWith("IsPlanetary", false)]
    [PreviewWith("WaterVisible", true)]
    internal sealed class StarMapViewModel : CartographyMapViewModel
    {
        const int GPS_CREATED_STATUS_UPDATES = 18;
        const double JUMP_RUNS_PER_SECOND = 6d;
        const double JUMP_DISTANCE_PER_SECOND = 1000000d;

        struct JumpThrottle
        {
            public long StartRun;
            public long DurationRuns;
            public long LastRequestRun;
        }

        readonly IStarMapHost _host;
        readonly MapSignalMarkers _signals;
        readonly MapSignalMarkers.Projector _project;
        readonly Dictionary<long, JumpThrottle> _jumpThrottles = new Dictionary<long, JumpThrottle>();
        readonly List<string> _infoLines = new List<string>();
        long _runs;
        long _selectedPlanetId;
        bool _selectionPinned;
        string _gpsCreatedStatus;
        long _gpsCreatedStatusUntil;
        bool _applyingConfig;
        float[] _knownBasis;
        float _knownYaw;
        float _knownPitch;
        long _knownFocus;
        Vector3D _knownOffset;
        float _knownZoom = 1f;
        bool _knownLinesHidden;
        bool _knownRenderSkybox = true;
        bool _knownRenderAtmosphere = true;
        int _knownLayer;

        public StarMapViewModel(IStarMapHost host) : base(host, MapMode.StarMap)
        {
            _host = host;
            _signals = new MapSignalMarkers(host);
            _project = Project;
            SelectedPlanet = new StarMapPlanetInfo(CreatePositionGps, CreateJumpGps, () => _selectionPinned = false);
            SelectedPlanet.Opened += () => PlanetDetailsOpened?.Invoke();
        }

        public event Action StaticCameraOrbitChanged;
        public event Action PlanetDetailsOpened;

        public StarMapPlanetInfo SelectedPlanet { get; }
        public StatusMessage Message { get; } = new StatusMessage();
        // Changes every tick: the planet flyout follows its planet's label.
        public int InfoVersion => _infoVersion;
        int _infoVersion;

        public void Tick()
        {
            _runs++;
            ApplyConfig(false);
            Update();
            UpdateSelection();
            UpdateMessage();
            RaiseAndSetIfChanged(ref _infoVersion, _infoVersion + 1, nameof(InfoVersion));
        }

        public void ApplyConfig(bool force)
        {
            StarMapConfigComponent config = _host.Config;
            if (config == null)
                return;
            var offset = new Vector3D(config.StaticCameraTargetOffsetX, config.StaticCameraTargetOffsetY,
                config.StaticCameraTargetOffsetZ);
            bool changed = false;
            _applyingConfig = true;
            try
            {
                if (force || config.RenderSkybox != _knownRenderSkybox)
                {
                    State.RenderSkybox = config.RenderSkybox;
                    changed = true;
                }
                if (force || config.RenderAtmosphere != _knownRenderAtmosphere)
                {
                    State.AtmosphereVisible = config.RenderAtmosphere;
                    changed = true;
                }
                if (force || !SameBasis(config.StaticOrbitBasis, _knownBasis) ||
                    Math.Abs(config.StaticOrbitYawRadians - _knownYaw) > 1e-6f ||
                    Math.Abs(config.StaticOrbitPitchRadians - _knownPitch) > 1e-6f)
                {
                    if (!TrySetOrbitBasis(config.StaticOrbitBasis, true))
                    {
                        if (config.StaticOrbitYawRadians == 0f && config.StaticOrbitPitchRadians == 0f)
                            ResetStarOrbit();
                        else
                            SetStarOrbitAngles(config.StaticOrbitYawRadians, config.StaticOrbitPitchRadians);
                    }
                    changed = true;
                }
                if (force || config.StaticFocusPlanetId != _knownFocus || Vector3D.DistanceSquared(offset, _knownOffset) > 1e-6)
                {
                    if (config.StaticFocusPlanetId != 0L)
                        State.FocusedPlanetId = config.StaticFocusPlanetId;
                    State.StarTargetOffsetWorld = offset;
                    changed = true;
                }
                if (force || Math.Abs(config.StaticZoom - _knownZoom) > 1e-4f)
                {
                    State.Zoom = MathHelper.Clamp(config.StaticZoom > 0f ? config.StaticZoom : 1f, STAR_MINIMUM_ZOOM, STAR_MAXIMUM_ZOOM);
                    changed = true;
                }
                if (force || config.StaticOrbitLinesHidden != _knownLinesHidden || config.MapLayer != _knownLayer)
                {
                    State.OrbitalLinesVisible = !config.StaticOrbitLinesHidden;
                    State.Layer = config.MapLayer >= 0 && config.MapLayer <= (int)CartographyLayer.Biomes
                        ? (CartographyLayer)config.MapLayer
                        : CartographyLayer.Satellite;
                    changed = true;
                }
            }
            finally
            {
                _applyingConfig = false;
            }
            Capture(config);
            if (changed)
                MarkDirty();
        }

        void Capture(StarMapConfigComponent config)
        {
            _knownBasis = config.StaticOrbitBasis;
            _knownYaw = config.StaticOrbitYawRadians;
            _knownPitch = config.StaticOrbitPitchRadians;
            _knownFocus = config.StaticFocusPlanetId;
            _knownOffset = new Vector3D(config.StaticCameraTargetOffsetX, config.StaticCameraTargetOffsetY,
                config.StaticCameraTargetOffsetZ);
            _knownZoom = config.StaticZoom;
            _knownLinesHidden = config.StaticOrbitLinesHidden;
            _knownRenderSkybox = config.RenderSkybox;
            _knownRenderAtmosphere = config.RenderAtmosphere;
            _knownLayer = config.MapLayer;
        }

        protected override void OnViewChanged()
        {
            if (_applyingConfig)
                return;
            StarMapConfigComponent config = _host.Config;
            if (config == null)
                return;
            float[] basis = GetOrbitBasis(true);
            bool changed = !SameBasis(config.StaticOrbitBasis, basis) ||
                           config.StaticFocusPlanetId != State.FocusedPlanetId ||
                           Vector3D.DistanceSquared(_knownOffset, State.StarTargetOffsetWorld) > 1e-6 ||
                           Math.Abs(config.StaticZoom - State.Zoom) > 1e-4f ||
                           config.StaticOrbitLinesHidden == State.OrbitalLinesVisible ||
                           config.MapLayer != (int)State.Layer;
            config.StaticOrbitBasis = basis;
            config.StaticFocusPlanetId = State.FocusedPlanetId;
            config.StaticCameraTargetOffsetX = State.StarTargetOffsetWorld.X;
            config.StaticCameraTargetOffsetY = State.StarTargetOffsetWorld.Y;
            config.StaticCameraTargetOffsetZ = State.StarTargetOffsetWorld.Z;
            config.StaticZoom = State.Zoom;
            config.StaticOrbitLinesHidden = !State.OrbitalLinesVisible;
            config.MapLayer = (int)State.Layer;
            Capture(config);
            if (!changed)
                return;
            _host.SyncConfig();
            StaticCameraOrbitChanged?.Invoke();
        }

        void UpdateMessage()
        {
            if (Data.Planets.Count == 0)
                Message.Show(LocHelper.Empty, "Warning", _host.WarningColor);
            else
                Message.Hide();
        }

        protected override bool TryGetStatus(out string status, out Color color)
        {
            color = _host.ForegroundColor;
            status = _gpsCreatedStatus;
            return !string.IsNullOrEmpty(status) && _runs <= _gpsCreatedStatusUntil;
        }

        bool TryGetPlanetAt(Vector2 pixel, out long planetId)
        {
            planetId = 0L;
            IReadOnlyList<StarMapLabel> labels = StarLabels;
            if (labels == null || !HasCanvas)
                return false;
            float best = float.MaxValue;
            for (int i = 0; i < labels.Count; i++)
            {
                StarMapLabel label = labels[i];
                var center = new Vector2(label.NormalizedPosition.X * Canvas.Width, label.NormalizedPosition.Y * Canvas.Height);
                float radius = Math.Max(8f, label.NormalizedRadius * Canvas.Width);
                float distance = Vector2.DistanceSquared(pixel, center);
                if (distance > radius * radius || distance >= best)
                    continue;
                best = distance;
                planetId = label.PlanetId;
            }
            return planetId != 0L;
        }

        protected override void OnClick(Vector2 pixel)
        {
            long planetId;
            if (!TryGetPlanetAt(pixel, out planetId))
            {
                _selectionPinned = false;
                return;
            }
            _selectedPlanetId = planetId;
            _selectionPinned = true;
            Host.PlayClick();
            FocusPlanet(planetId);
            UpdateSelection();
        }

        protected override void OnMarkerClicked(MapMarker marker)
        {
            if (marker.Kind == MapMarkerKind.Radio)
                CreateGps(marker.Name, marker.WorldPosition, marker.Color, false);
        }

        void UpdateSelection()
        {
            PlanetSnapshot planet = null;
            if (_selectionPinned)
                planet = Data.FindPlanet(_selectedPlanetId);
            if (planet == null)
                planet = Data.FindPlanet(State.FocusedPlanetId);
            if (planet == null || planet.Planet == null || planet.Planet.MarkedForClose)
            {
                _selectedPlanetId = 0L;
                SelectedPlanet.Hide();
                return;
            }
            _selectedPlanetId = planet.EntityId;
            StarMapLabel label;
            if (!TryGetLabel(planet.EntityId, out label))
            {
                SelectedPlanet.Hide();
                return;
            }
            SelectedPlanet.SetAnchor(label.NormalizedPosition, label.NormalizedRadius, _selectionPinned);
            BuildInfo(planet);
        }

        bool TryGetLabel(long planetId, out StarMapLabel label)
        {
            IReadOnlyList<StarMapLabel> labels = StarLabels;
            if (labels != null)
                for (int i = 0; i < labels.Count; i++)
                    if (labels[i].PlanetId == planetId)
                    {
                        label = labels[i];
                        return true;
                    }
            label = default(StarMapLabel);
            return false;
        }

        void BuildInfo(PlanetSnapshot snapshot)
        {
            MyPlanet planet = snapshot.Planet;
            var generator = planet.Generator;
            var atmosphere = generator == null ? null : generator.Atmosphere;
            double surfaceGravity = planet.GetInitArguments.SurfaceGravity;
            double gravityRange = GravityRange(planet);
            _infoLines.Clear();
            _infoLines.Add(Property("Radius", FormatingHelper.DistanceToString(planet.AverageRadius)));
            _infoLines.Add(Property("Gravity", FormatingHelper.GravityToString((float)surfaceGravity)));
            _infoLines.Add(Property("Range", FormatingHelper.DistanceToString((float)gravityRange)));
            _infoLines.Add(Property("Atmosphere", FormatingHelper.PercentageToString(planet.HasAtmosphere && atmosphere != null ? atmosphere.Density : 0f)));
            _infoLines.Add(Property("O2", FormatingHelper.PercentageToString(planet.HasAtmosphere && atmosphere != null ? atmosphere.OxygenDensity : 0f)));
            _infoLines.Add(Property("Temperature", FormatingHelper.TemperatureToString(generator == null ? null : (MyTemperatureLevel?)generator.DefaultSurfaceTemperature)));
            _infoLines.Add(Property("Wind", FormatingHelper.WindToString(atmosphere == null ? 0f : atmosphere.MaxWindSpeed)));
            Data.UpdatePlayerPosition();
            _infoLines.Add(FormatingHelper.DistanceToString((float)Vector3D.Distance(Data.PlayerPosition, snapshot.Position)));

            Vector3D jumpPoint;
            string jump;
            bool canJump = TryJumpText(snapshot, gravityRange, out jump, out jumpPoint);
            SelectedPlanet.Update(PlanetName(snapshot), _infoLines,
                Property("Position", FormatingHelper.FormatBearing(Matrix.Identity, snapshot.Position)), jump, canJump);
        }

        bool TryJumpText(PlanetSnapshot planet, double gravityRange, out string text, out Vector3D jumpPoint)
        {
            jumpPoint = Vector3D.Zero;
            text = Property("Jump", LocHelper.GetLoc(MOD_PREFIX + "NotAvailable"));
            if (!_host.HasJumpDrives)
                return false;
            Data.UpdatePlayerPosition();
            int eta;
            if (IsJumpThrottled(planet.EntityId, Vector3D.Distance(Data.PlayerPosition, planet.Position), out eta))
            {
                text = Property("Jump", string.Format(FormatingHelper.Culture,
                    LocHelper.GetLoc(MOD_PREFIX + "StarMap_JumpCalculatingFormat"), eta));
                return false;
            }
            if (!_host.TryGetJumpPoint(planet, gravityRange, out jumpPoint))
                return false;
            MatrixD? reference = _host.ReferenceWorldMatrix;
            text = Property("Jump", FormatingHelper.FormatBearing(reference ?? MatrixD.Identity, jumpPoint));
            return true;
        }

        bool IsJumpThrottled(long planetId, double distance, out int eta)
        {
            JumpThrottle state;
            bool known = _jumpThrottles.TryGetValue(planetId, out state);
            if (!known || _runs - state.LastRequestRun > JUMP_RUNS_PER_SECOND)
            {
                double seconds = Math.Max(1d, distance / JUMP_DISTANCE_PER_SECOND);
                state = new JumpThrottle
                {
                    StartRun = _runs,
                    DurationRuns = (long)Math.Ceiling(seconds * JUMP_RUNS_PER_SECOND),
                    LastRequestRun = _runs
                };
                _jumpThrottles[planetId] = state;
                eta = (int)Math.Ceiling(state.DurationRuns / JUMP_RUNS_PER_SECOND);
                return true;
            }
            state.LastRequestRun = _runs;
            _jumpThrottles[planetId] = state;
            long remaining = state.DurationRuns - (_runs - state.StartRun);
            eta = Math.Max(1, (int)Math.Ceiling(remaining / JUMP_RUNS_PER_SECOND));
            return remaining > 0;
        }

        void CreatePositionGps()
        {
            PlanetSnapshot planet = Data.FindPlanet(_selectedPlanetId);
            if (planet != null)
                CreateGps(PlanetName(planet), planet.Position, _host.HeaderColor, true);
        }

        void CreateJumpGps()
        {
            PlanetSnapshot planet = Data.FindPlanet(_selectedPlanetId);
            Vector3D jumpPoint;
            if (planet == null || planet.Planet == null || !_host.TryGetJumpPoint(planet, GravityRange(planet.Planet), out jumpPoint))
                return;
            CreateGps(string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "StarMap_JumpPointNameFormat"),
                PlanetName(planet)), jumpPoint, _host.HeaderColor, true);
        }

        void CreateGps(string name, Vector3D position, Color color, bool sendToChat)
        {
            var gpsCollection = MyAPIGateway.Session.GPS;
            if (gpsCollection == null)
                return;
            string localName = string.IsNullOrWhiteSpace(name) ? LocHelper.GetLoc(MOD_PREFIX + "Gps_Unknown_Name") : name;
            IMyGps gps = gpsCollection.Create(localName, string.Empty, position, false, true);
            if (gps == null)
                return;
            gps.GPSColor = color;
            gpsCollection.AddLocalGps(gps);
            _gpsCreatedStatus = string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "Gps_CreatedFormat"), localName);
            _gpsCreatedStatusUntil = _runs + GPS_CREATED_STATUS_UPDATES;
            Host.PlayGps();
            if (sendToChat)
                MyAPIGateway.Utilities.ShowMessage(_host.Title, string.Format(CultureInfo.InvariantCulture,
                    "GPS:{0}:{1:0.###}:{2:0.###}:{3:0.###}:{4}:", localName.Replace(":", "_"), position.X, position.Y,
                    position.Z, color.ToAHex()));
            ViewChanged();
        }

        static double GravityRange(MyPlanet planet)
        {
            double surfaceGravity = planet.GetInitArguments.SurfaceGravity;
            double falloff = planet.GetInitArguments.GravityFalloff;
            if (planet.MaximumRadius <= 0d || surfaceGravity <= 0d || falloff <= 0d)
                return 0d;
            return Math.Max(0d, planet.MaximumRadius * Math.Pow(surfaceGravity / .05d, 1d / falloff) - planet.AverageRadius);
        }

        string Property(string name, object value)
        {
            return string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + name), value);
        }

        static string PlanetName(PlanetSnapshot planet)
        {
            string name;
            if (PlanetHelper.PlanetNamesById.TryGetValue(planet.EntityId, out name) && !string.IsNullOrWhiteSpace(name))
                return name;
            return string.IsNullOrWhiteSpace(planet.Name) ? LocHelper.GetLoc(MOD_PREFIX + "ClockDashboard_UnknownPlanet") : planet.Name;
        }

        protected override void CollectMarkers(List<MapMarker> markers)
        {
            CollectPlanetLabels(markers);
            _signals.CollectRadio(markers, _host.Config, _host.Block, _project);
            _signals.CollectGps(markers, _host.Config, _project);
        }

        bool Project(Vector3D world, out Vector2 pixel)
        {
            return TryProjectStar(world, out pixel) && IsInsideCanvas(pixel, 32f);
        }

        void CollectPlanetLabels(List<MapMarker> markers)
        {
            IReadOnlyList<StarMapLabel> labels = StarLabels;
            if (labels == null)
                return;
            for (int i = 0; i < labels.Count; i++)
            {
                StarMapLabel label = labels[i];
                PlanetSnapshot planet = Data.FindPlanet(label.PlanetId);
                if (planet == null)
                    continue;
                float radius = label.NormalizedRadius * Canvas.Width;
                var center = new Vector2(label.NormalizedPosition.X * Canvas.Width, label.NormalizedPosition.Y * Canvas.Height);
                markers.Add(new MapMarker
                {
                    Position = center - new Vector2(0f, radius + 12f),
                    Label = PlanetName(planet),
                    Color = _host.ForegroundColor,
                    CenterLabel = true
                });
            }
        }
    }
}
