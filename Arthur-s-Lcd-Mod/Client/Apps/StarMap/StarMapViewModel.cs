using System;
using System.Collections.Generic;
using System.Globalization;
using Adk.Gui.Core.Input;
using LcdMod.Client.Apps.Maps;
using LcdMod.Client.Apps.Views;
using LcdMod.Client.Extensions;
using LcdMod.Client.Helpers;
using Cartography;
using Cartography.Maps;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Mvvm;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps.StarMap
{
    internal interface IStarMapHost : IMapHost
    {
        StarMapConfigComponent Config { get; }
        bool IsHud { get; }
        IMyCubeBlock Block { get; }
        string Title { get; }
        string Font { get; }
        float FontScale { get; }
        Color HeaderColor { get; }
        Color WarningColor { get; }
        Color ErrorColor { get; }
        Color ForegroundColor { get; }
        Vector3D LinearVelocity { get; }
        bool HasJumpDrives { get; }
        bool TryGetJumpPoint(PlanetSnapshot planet, double gravityRange, out Vector3D jumpPoint);
        void SyncConfig();
    }

    internal sealed class StarMapInfoLine : ObservableObject
    {
        string _text = string.Empty;

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

        /// <summary>The planet's centre on the map, normalized to the canvas.</summary>
        public Vector2 AnchorCenter { get; private set; }

        /// <summary>The planet's radius on the map, normalized to the canvas width.</summary>
        public float AnchorRadius { get; private set; }

        /// <summary>True when the user picked the planet, so clicking elsewhere dismisses it.</summary>
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

        public string Name => _name;
        public IReadOnlyList<StarMapInfoLine> Lines => _lines;
        public string PositionText => _positionText;
        public string JumpText => _jumpText;
        public bool CanJump => _canJump;
        public bool IsVisible => _isVisible;
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
    internal sealed class StarMapViewModel : CartographyMapViewModel
    {
        const float DEFAULT_FOV_DEGREES = 70f;
        const float LADDER_STEP_RADIANS = .087266445f;
        const int MAGNIFICATION_VISIBLE_UPDATES = 30;
        const int GPS_CREATED_STATUS_UPDATES = 18;
        const int RADIO_REFRESH_UPDATES = 6;
        const float CLUSTER_DISTANCE = 30f;
        const double JUMP_RUNS_PER_SECOND = 6d;
        const double JUMP_DISTANCE_PER_SECOND = 1000000d;
        const long ALTITUDE_SAMPLE_FRAMES = 60L;
        const int ALTITUDE_WARNING_UPDATES = 24;

        struct JumpThrottle
        {
            public long StartRun;
            public long DurationRuns;
            public long LastRequestRun;
        }

        readonly IStarMapHost _host;
        readonly List<IMyGps> _gpsEntries = new List<IMyGps>();
        readonly List<GpsMarkerProjection> _gpsProjections = new List<GpsMarkerProjection>();
        readonly List<GpsMarkerCluster> _gpsClusters = new List<GpsMarkerCluster>();
        readonly List<byte> _gpsConsumed = new List<byte>();
        readonly RadioSignalMarkerCollector _radioCollector = new RadioSignalMarkerCollector();
        readonly List<RadioSignalMarker> _radioSignals = new List<RadioSignalMarker>();
        readonly List<RadioSignalMarkerProjection> _radioProjections = new List<RadioSignalMarkerProjection>();
        readonly List<RadioSignalMarkerCluster> _radioClusters = new List<RadioSignalMarkerCluster>();
        readonly List<byte> _radioConsumed = new List<byte>();
        readonly Dictionary<long, JumpThrottle> _jumpThrottles = new Dictionary<long, JumpThrottle>();
        readonly List<string> _infoLines = new List<string>();
        IMyGravityProviderSystem _gravityProvider;
        long _runs;
        bool _isHud;
        int _hudVersion;
        long _selectedPlanetId;
        bool _selectionPinned;
        float _knownFov = float.NaN;
        long _magnificationUntil = long.MinValue;
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
        int _knownLayer;
        int _lastAltitude;
        long _lastAltitudeFrame = long.MinValue;
        long _lastAltitudePlanet;
        int _verticalSpeed;
        long _altitudeWarningAt = long.MinValue;

        public StarMapViewModel(IStarMapHost host) : base(host, MapMode.StarMap)
        {
            _host = host;
            SelectedPlanet = new StarMapPlanetInfo(CreatePositionGps, CreateJumpGps, () => _selectionPinned = false);
            SelectedPlanet.Opened += () => PlanetDetailsOpened?.Invoke();
        }

        public event Action StaticCameraOrbitChanged;
        public event Action PlanetDetailsOpened;

        public StarMapHud Hud { get; } = new StarMapHud();
        public int HudVersion => _hudVersion;
        public StarMapPlanetInfo SelectedPlanet { get; }
        public StatusMessage Message { get; } = new StatusMessage();
        public bool IsHud => _isHud;
        public bool IsPlanetarium => !_isHud;

        public void Tick()
        {
            _runs++;
            bool hud = _host.IsHud;
            if (RaiseAndSetIfChanged(ref _isHud, hud, nameof(IsHud)))
            {
                RaisePropertyChanged<bool>(nameof(IsPlanetarium));
                _selectionPinned = false;
                MarkDirty();
            }
            ApplyConfig(false);
            UpdateFixedCamera();
            Update();
            UpdateSelection();
            UpdateHud();
            UpdateMessage();
            if (_isHud)
                Invalidate();
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
                if (float.IsNaN(_knownFov) || Math.Abs(config.FoV - _knownFov) > .001f)
                {
                    if (!float.IsNaN(_knownFov))
                        _magnificationUntil = _runs + MAGNIFICATION_VISIBLE_UPDATES;
                    _knownFov = config.FoV;
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
            if (!_isHud)
                StaticCameraOrbitChanged?.Invoke();
        }

        void UpdateFixedCamera()
        {
            State.HasFixedStarCamera = _isHud;
            if (!_isHud)
                return;
            MatrixD? reference = _host.ReferenceWorldMatrix;
            if (reference.HasValue)
                State.FixedStarCamera = reference.Value;
            State.FixedStarCameraVerticalFovRadians = MathHelper.ToRadians(FovDegrees);
        }

        float FovDegrees
        {
            get
            {
                StarMapConfigComponent config = _host.Config;
                return config == null || config.FoV <= 0f ? DEFAULT_FOV_DEGREES : config.FoV;
            }
        }

        public bool ScrollFov(int delta)
        {
            StarMapConfigComponent config = _host.Config;
            if (config == null)
                return false;
            float magnification = Terminal.Controls.Generic.SliderFov.FovToMagnification(config.FoV);
            float next = Terminal.Controls.Generic.SliderFov.MagnificationToFov(magnification * (delta > 0 ? 1.1f : 1f / 1.1f));
            if (Math.Abs(next - config.FoV) <= .001f)
                return false;
            config.FoV = next;
            _host.SyncConfig();
            ApplyConfig(false);
            ViewChanged();
            return true;
        }

        protected override bool HandleScroll(int delta)
        {
            return _isHud ? ScrollFov(delta) : base.HandleScroll(delta);
        }

        protected override bool CanOrbit => !_isHud;

        void UpdateHud()
        {
            StarMapHud hud = Hud;
            hud.Visible = _isHud;
            hud.Ladder.Clear();
            hud.HasHorizon = hud.HasAltitude = hud.PullUp = hud.ShowSpeed = hud.BoreSight = hud.HasVelocity = false;
            hud.Magnification = _isHud && _runs <= _magnificationUntil
                ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "StarMap_MagnificationFormat"),
                    Math.Tan(MathHelper.ToRadians(DEFAULT_FOV_DEGREES) * .5) / Math.Tan(MathHelper.ToRadians(Math.Max(.1f, FovDegrees)) * .5))
                : null;
            hud.VerticalFovRadians = MathHelper.ToRadians(FovDegrees);
            hud.Color = _host.ForegroundColor;
            hud.ErrorColor = _host.ErrorColor;
            hud.Font = _host.Font;
            hud.FontScale = _host.FontScale;
            hud.EssentialOnly = SelectedPlanet.IsOpen;
            MatrixD? reference = _host.ReferenceWorldMatrix;
            if (_isHud && reference.HasValue && _host.Block != null && _host.Block.CubeGrid != null)
                BuildInstruments(hud, reference.Value);
            RaiseAndSetIfChanged(ref _hudVersion, _hudVersion + 1, nameof(HudVersion));
        }

        void BuildInstruments(StarMapHud hud, MatrixD world)
        {
            Vector3D velocity = _host.LinearVelocity;
            if (Vector3D.Dot(velocity, world.Forward) >= -.1d)
            {
                Vector3D direction = velocity;
                hud.HasVelocity = true;
                hud.VelocityDirection = velocity.LengthSquared() < 9d || direction.Normalize() <= 1e-6
                    ? new Vector3(0f, 0f, 1f)
                    : ToCamera(world, direction);
            }
            hud.ShowSpeed = true;
            hud.Speed = (int)velocity.Length();
            hud.BoreSight = true;

            Vector3D position = world.Translation;
            IMyNaturalGravityComponent gravityWell;
            if (!TryGetGravityWell(position, out gravityWell))
            {
                ResetAltitude();
                return;
            }
            Vector3D gravity = gravityWell.Position - position;
            if (gravity.Normalize() <= 1e-6)
                return;
            Vector3D horizonForward = Vector3D.Reject(world.Forward, gravity);
            if (horizonForward.Normalize() <= 1e-6)
                return;

            Vector3D gravityRoll = Vector3D.Normalize(Vector3D.Reject(gravity, world.Forward));
            if (double.IsNaN(gravityRoll.X))
                gravityRoll = -world.Up;
            double roll = -(Math.Acos(MathHelper.Clamp((float)Vector3D.Dot(gravityRoll, world.Left), -1f, 1f)) - Math.PI * .5d);
            if (Vector3D.Dot(gravity, world.Up) >= 0d)
                roll = Math.PI - roll;
            double pitch = Math.Acos(MathHelper.Clamp((float)Vector3D.Dot(gravity, world.Forward), -1f, 1f)) - Math.PI * .5d;
            hud.Roll = (float)roll;
            hud.HasHorizon = true;
            hud.HorizonDirection = ToCamera(world, horizonForward);

            int center = (int)Math.Round(pitch / LADDER_STEP_RADIANS);
            MatrixD level = MatrixD.CreateWorld(position, horizonForward, -gravity);
            for (int index = center - 5; index <= center + 5; index++)
            {
                if (index == 0)
                    continue;
                MatrixD step = MatrixD.CreateRotationX(index * LADDER_STEP_RADIANS) * level;
                hud.Ladder.Add(new HudLadderStep
                {
                    Direction = ToCamera(world, step.Forward),
                    Label = (index > 18 ? 180 - index * 5 : Math.Abs(index * 5)).ToString(FormatingHelper.Culture),
                    Negative = index < 0
                });
            }

            MyPlanet planet = gravityWell as MyPlanet;
            if (planet == null || planet.MarkedForClose)
            {
                ResetAltitude();
                return;
            }
            int altitude = Math.Max(0, (int)Math.Round(Vector3D.Distance(position, planet.GetClosestSurfacePointGlobal(position))));
            SampleAltitude(altitude, planet.EntityId);
            hud.HasAltitude = true;
            hud.Altitude = altitude;
            hud.VerticalSpeed = _verticalSpeed;
            double descent = Vector3D.Dot(velocity, gravity);
            hud.PullUp = descent > 0d && altitude <= Math.Max(50d, descent * 3d) && _runs % 10 > 2;
        }

        static Vector3 ToCamera(MatrixD world, Vector3D direction)
        {
            return new Vector3(
                (float)Vector3D.Dot(direction, world.Right),
                (float)Vector3D.Dot(direction, world.Up),
                (float)Vector3D.Dot(direction, world.Forward));
        }

        bool TryGetGravityWell(Vector3D position, out IMyNaturalGravityComponent well)
        {
            well = null;
            if (_gravityProvider == null)
                _gravityProvider = (IMyGravityProviderSystem)MyAPIGateway.Session.GetComponentByInterfaceType<IMyGravityProviderSystem>();
            if (_gravityProvider == null || !_gravityProvider.IsPositionInNaturalGravity(position))
                return false;
            _gravityProvider.GetStrongestNaturalGravityWell(position, out well);
            return well != null;
        }

        void SampleAltitude(int altitude, long planetId)
        {
            long frame = MyAPIGateway.Session.GameplayFrameCounter;
            float warningAltitude = 100f;
            var grid = _host.Block.CubeGrid as MyCubeGrid;
            if (grid != null)
                warningAltitude += grid.PositionComp.LocalAABB.Height;
            if (_lastAltitudeFrame != long.MinValue && _lastAltitude >= warningAltitude && altitude < warningAltitude)
                _altitudeWarningAt = _runs;

            if (_lastAltitudeFrame == long.MinValue || _lastAltitudePlanet != planetId || frame < _lastAltitudeFrame)
            {
                _lastAltitude = altitude;
                _lastAltitudeFrame = frame;
                _lastAltitudePlanet = planetId;
                _verticalSpeed = 0;
                return;
            }
            long elapsed = frame - _lastAltitudeFrame;
            if (elapsed < ALTITUDE_SAMPLE_FRAMES)
                return;
            _verticalSpeed = (int)Math.Round((altitude - _lastAltitude) * 60d / elapsed);
            _lastAltitude = altitude;
            _lastAltitudeFrame = frame;
        }

        void ResetAltitude()
        {
            _lastAltitudeFrame = long.MinValue;
            _verticalSpeed = 0;
        }

        void UpdateMessage()
        {
            if (Data.Planets.Count == 0)
                Message.Show(LocHelper.Empty, "Warning", _host.WarningColor);
            else if (_isHud && _altitudeWarningAt != long.MinValue && _runs - _altitudeWarningAt <= ALTITUDE_WARNING_UPDATES)
                Message.Show(LocHelper.GetLoc("DisplayName_TSS_ArtificialHorizon_AltitudeWarning"), "Warning", _host.WarningColor);
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
            if (!_isHud)
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
            if (planet == null && _isHud && HasCanvas)
            {
                long planetId;
                if (TryGetPlanetAt(new Vector2(Canvas.Width * .5f, Canvas.Height * .5f), out planetId))
                    planet = Data.FindPlanet(planetId);
            }
            if (planet == null && !_isHud)
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
            return string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + name + (_isHud ? "_Short" : string.Empty)), value);
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
            CollectRadio(markers);
            CollectGps(markers);
        }

        void CollectPlanetLabels(List<MapMarker> markers)
        {
            IReadOnlyList<StarMapLabel> labels = StarLabels;
            if (labels == null)
                return;
            for (int i = 0; i < labels.Count; i++)
            {
                StarMapLabel label = labels[i];
                if (_isHud && label.PlanetId != _selectedPlanetId)
                    continue;
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

        void CollectGps(List<MapMarker> markers)
        {
            StarMapConfigComponent config = _host.Config;
            var session = MyAPIGateway.Session;
            if (config == null)
                return;
            GpsDisplayWaypoint[] always = config.AlwaysDisplayedGpsWaypoints ?? Array.Empty<GpsDisplayWaypoint>();
            int[] legacy = config.AlwaysDisplayedGpsHashes ?? Array.Empty<int>();
            _gpsProjections.Clear();
            if ((config.DisplayMyGps || legacy.Length != 0) && session.GPS != null && session.Player != null)
            {
                _gpsEntries.Clear();
                session.GPS.GetGpsList(session.Player.IdentityId, _gpsEntries);
                for (int i = 0; i < _gpsEntries.Count; i++)
                    if (GpsMarkerLayout.ShouldRenderLiveGps(_gpsEntries[i], config.DisplayMyGps, always, legacy))
                        AddGps(GpsMarkerLayout.FromGps(_gpsEntries[i]));
            }
            for (int i = 0; i < always.Length; i++)
            {
                GpsMarker marker;
                if (GpsMarkerLayout.TryCreateMarker(always[i], out marker))
                    AddGps(marker);
            }
            GpsMarkerLayout.Cluster(_gpsProjections, CLUSTER_DISTANCE, _gpsClusters, _gpsConsumed);
            for (int i = 0; i < _gpsClusters.Count; i++)
            {
                GpsMarkerCluster cluster = _gpsClusters[i];
                bool isCluster = cluster.Count > 1;
                Color color = cluster.RepresentativeMarker.Color;
                color.A = byte.MaxValue;
                markers.Add(new MapMarker
                {
                    Kind = MapMarkerKind.Gps,
                    Position = cluster.ScreenPosition,
                    Icon = "WaypointIcon",
                    Color = color,
                    Size = isCluster ? 1.35f : 1.4f,
                    CountText = isCluster ? cluster.Count.ToString() : null,
                    Label = isCluster
                        ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "Gps_ClusterFormat"), cluster.Count)
                        : string.IsNullOrWhiteSpace(cluster.RepresentativeMarker.Name)
                            ? LocHelper.GetLoc(MOD_PREFIX + "Gps_Unnamed")
                            : cluster.RepresentativeMarker.Name
                });
            }
        }

        void AddGps(GpsMarker marker)
        {
            Vector2 pixel;
            if (TryProjectStar(marker.WorldPosition, out pixel) && IsInsideCanvas(pixel, 32f))
                _gpsProjections.Add(new GpsMarkerProjection { Marker = marker, ScreenPosition = pixel });
        }

        void CollectRadio(List<MapMarker> markers)
        {
            StarMapConfigComponent config = _host.Config;
            if (config == null || !config.IncludeRadioSignals)
                return;
            if (_runs % RADIO_REFRESH_UPDATES == 1 || _radioSignals.Count == 0)
                _radioCollector.Collect(_host.Block, _radioSignals);
            _radioProjections.Clear();
            for (int i = 0; i < _radioSignals.Count; i++)
            {
                Vector2 pixel;
                if (TryProjectStar(_radioSignals[i].WorldPosition, out pixel) && IsInsideCanvas(pixel, 32f))
                    _radioProjections.Add(new RadioSignalMarkerProjection { Marker = _radioSignals[i], ScreenPosition = pixel });
            }
            RadioSignalMarkerLayout.Cluster(_radioProjections, CLUSTER_DISTANCE, _radioClusters, _radioConsumed);
            for (int i = 0; i < _radioClusters.Count; i++)
            {
                RadioSignalMarkerCluster cluster = _radioClusters[i];
                RadioSignalMarker marker = cluster.RepresentativeMarker;
                bool isCluster = cluster.Count > 1;
                Color color = RadioColor(marker.Relationship);
                markers.Add(new MapMarker
                {
                    Kind = MapMarkerKind.Radio,
                    Position = cluster.ScreenPosition,
                    Icon = isCluster ? "CircleHollow" : RadioTexture(marker.Relationship),
                    Color = color,
                    Size = isCluster ? 1.35f : 1f,
                    CountText = isCluster ? cluster.Count.ToString() : null,
                    Label = isCluster
                        ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "RadioSignal_ClusterFormat"), cluster.Count)
                        : string.IsNullOrWhiteSpace(marker.Name) ? LocHelper.GetLoc(MOD_PREFIX + "RadioSignal_Unnamed") : marker.Name,
                    Name = marker.Name,
                    WorldPosition = marker.WorldPosition,
                    Clickable = !isCluster
                });
            }
        }

        Color RadioColor(MyRelationsBetweenPlayerAndBlock relationship)
        {
            Color color;
            switch (relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    color = _host.ErrorColor;
                    break;
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    color = _host.HeaderColor;
                    break;
                default:
                    color = _host.WarningColor;
                    break;
            }
            color.A = byte.MaxValue;
            return color;
        }

        static string RadioTexture(MyRelationsBetweenPlayerAndBlock relationship)
        {
            switch (relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return "Circle";
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return "SquareSimple";
                default:
                    return "Triangle";
            }
        }
    }
}
