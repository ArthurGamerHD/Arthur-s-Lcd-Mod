using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Maps;
using LcdMod.Client.Apps.StarMap;
using LcdMod.Client.Apps.Views;
using LcdMod.Client.Helpers;
using Acrux.Cartography;
using Acrux.Cartography.Maps;
using LcdMod.Common.Config.Components;
using Acrux.Core.Input;
using Acrux.Render3D;
using Acrux.Rendering;
using Acrux.Styling;
using Adk.Mvvm;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps.ExternalSensors
{
    internal interface IExternalSensorsHost : IMapHost, IMarkerColors
    {
        StarMapConfigComponent Config { get; }
        IMyCubeBlock Block { get; }
        string Font { get; }
        float FontScale { get; }
        Color ForegroundColor { get; }
        Vector3D LinearVelocity { get; }
        // Looking out of the ship (a camera or the screen's reference): the flight instruments and the magnifying
        // wheel apply. Otherwise the camera follows the ship from outside.
        bool IsFirstPerson { get; }
        void SyncConfig();
        // Switches between third and first person.
        void ToggleView();
        // The camera block looked through in first person (zero for the screen's reference) and how far its view may
        // be nudged to each side, in degrees, as the game lets a camera pan.
        long ViewCameraId { get; }
        float PanLimitDegrees { get; }
        // The grid the player is locking on, how far the lock is (0 to 100) and whether it is complete.
        bool TryGetTargetLock(out IMyCubeGrid target, out float percent, out bool locked);
        // The other ships the sensors see, one grid each (the largest of a ship's subgrids).
        List<IMyCubeGrid> Contacts { get; }
        // Whether the HUD outlines and names this ship, given whether the pointer is over it.
        bool ShowsContact(IMyCubeGrid ship, bool underPointer);
        // The contact the game's lock key would lock now, while nothing is locked; null otherwise.
        IMyCubeGrid LockCandidate { get; }
        // Starts locking this contact, as the lock key would; false when the game would refuse it (no seat with
        // target locking, out of range, or outside the lock cone).
        bool RequestLock(IMyCubeGrid ship);
    }

    // What the ship's cameras see, drawn by the star map's 3D scene from a fixed camera: through one of them (first
    // person, with the flight instruments the Star Map's HUD had), or following the ship from outside (third person),
    // where only what lies inside some camera's view draws besides the ship itself.
    internal sealed class ExternalSensorsViewModel : CartographyMapViewModel, IMapSurfaceHover
    {
        const float DEFAULT_FOV_DEGREES = 70f;
        const float LADDER_STEP_RADIANS = .087266445f;
        const int MAGNIFICATION_VISIBLE_UPDATES = 30;
        const long ALTITUDE_SAMPLE_FRAMES = 60L;
        const int ALTITUDE_WARNING_UPDATES = 24;
        const float ORBIT_SENSITIVITY = 0.01f;
        const float ZOOM_STEP = 1.15f;
        // The third-person camera's distance, in the ship's radii.
        const float MINIMUM_DISTANCE_RADII = 1.2f;
        const float MAXIMUM_DISTANCE_RADII = 60f;
        const float DEFAULT_PITCH = 0.35f;
        const float DEFAULT_DISTANCE_RADII = 3f;
        // Degrees of camera pan per pixel dragged.
        const float PAN_SENSITIVITY = 0.15f;

        static readonly BackgroundBrush FirstPersonIcon = new ImageBrush("Camera") { Stretch = Stretch.Uniform };
        static readonly BackgroundBrush LockIcon = new ImageBrush("Lock") { Stretch = Stretch.Uniform };
        static readonly BackgroundBrush ThirdPersonIcon = new ImageBrush("RotationPlane") { Stretch = Stretch.Uniform };

        readonly IExternalSensorsHost _host;
        readonly MapSignalMarkers _signals;
        readonly MapSignalMarkers.Projector _project;
        IMyGravityProviderSystem _gravityProvider;
        long _runs;
        int _hudVersion;
        bool _firstPerson;
        float _knownFov = float.NaN;
        long _magnificationUntil = long.MinValue;
        bool _knownRenderSkybox = true;
        bool _knownRenderAtmosphere = true;
        bool _configApplied;
        int _lastAltitude;
        long _lastAltitudeFrame = long.MinValue;
        long _lastAltitudePlanet;
        int _verticalSpeed;
        long _altitudeWarningAt = long.MinValue;
        bool _isSceneLoading;
        string _sceneLoadingText = string.Empty;

        public ExternalSensorsViewModel(IExternalSensorsHost host) : base(host, MapMode.StarMap)
        {
            _host = host;
            _signals = new MapSignalMarkers(host);
            _project = Project;
            ViewCommand = new DelegateCommand(_ => _host.ToggleView());
            CameraLockCommand = new DelegateCommand(_ => ResetCamera());
            State.HasFixedStarCamera = true;
            State.PlanetsVisible = true;
            State.MissilesVisible = true;
            State.OrbitalLinesVisible = false;
        }

        // The third-person camera around the ship, in its own axes: turned from behind, raised, and how far; kept
        // in the screen's settings.
        public float OrbitYaw => _host.Config.SensorsYaw;
        public float OrbitPitch => _host.Config.SensorsPitch;
        public float OrbitDistanceRadii =>
            MathHelper.Clamp(_host.Config.SensorsDistance, MINIMUM_DISTANCE_RADII, MAXIMUM_DISTANCE_RADII);

        // The user turned or zoomed the third-person camera.
        public event Action OrbitChanged;

        // The button between third and first person, showing the current one.
        public ICommand ViewCommand { get; }
        public BackgroundBrush ViewIcon => _firstPerson ? FirstPersonIcon : ThirdPersonIcon;
        public bool IsFirstPersonView => _firstPerson;

        // The button that puts the camera back: centred through the camera, behind the ship from outside. Lit while
        // the camera is there.
        public ICommand CameraLockCommand { get; }
        public BackgroundBrush CameraLockIcon => LockIcon;
        public string CameraLockTip => LocHelper.GetLoc(MOD_PREFIX + "ExternalSensors_LockCamera");
        public bool IsCameraLocked => _firstPerson
            ? PanYawDegrees == 0f && PanPitchDegrees == 0f
            : OrbitYaw == 0f && Math.Abs(OrbitPitch - DEFAULT_PITCH) < 1e-4f &&
              Math.Abs(OrbitDistanceRadii - DEFAULT_DISTANCE_RADII) < 1e-4f;

        // The first-person nudge of the camera block's view, in degrees: up, and to the left.
        public float PanPitchDegrees { get; private set; }
        public float PanYawDegrees { get; private set; }
        long _panCameraId;
        public string ViewTip => LocHelper.GetLoc(MOD_PREFIX + "ExternalSensors_SwitchView");

        public StarMapHud Hud { get; } = new StarMapHud();
        public int HudVersion => _hudVersion;
        public StatusMessage Message { get; } = new StatusMessage();

        [PreviewWith(true)]
        public bool IsSceneLoading => _isSceneLoading;

        [PreviewWith("Loading 3D scene")]
        public string SceneLoadingText => _sceneLoadingText;

        // A short note on the screen (no cameras on the ship), or null.
        public string Notice { get; set; }

        public void Tick()
        {
            _runs++;
            if (_firstPerson != _host.IsFirstPerson)
            {
                _firstPerson = _host.IsFirstPerson;
                RaisePropertyChanged<BackgroundBrush>(nameof(ViewIcon));
                RaisePropertyChanged<bool>(nameof(IsFirstPersonView));
                RaisePropertyChanged<bool>(nameof(IsCameraLocked));
                MarkDirty();
            }
            ApplyConfig();
            // Another camera starts centred.
            if (_panCameraId != _host.ViewCameraId)
            {
                _panCameraId = _host.ViewCameraId;
                PanPitchDegrees = 0f;
                PanYawDegrees = 0f;
                RaisePropertyChanged<bool>(nameof(IsCameraLocked));
            }
            UpdateFixedCamera();
            Update();
            UpdateHud();
            UpdateMessage();
            bool loading = IsRendererLoading;
            RaiseAndSetIfChanged(ref _isSceneLoading, loading, nameof(IsSceneLoading));
            if (loading)
            {
                string text = LocHelper.GetLoc(MOD_PREFIX + "StarMap_Loading3DScene");
                RaiseAndSetIfChanged(ref _sceneLoadingText, text, nameof(SceneLoadingText));
            }
            Invalidate();
        }

        // Only these grids and voxel maps draw (planets always do); null draws every one.
        public void SetVisibleEntities(long[] entityIds)
        {
            State.VisibleEntityIds = entityIds;
        }

        void ApplyConfig()
        {
            StarMapConfigComponent config = _host.Config;
            if (config == null)
                return;
            bool changed = false;
            if (!_configApplied || config.RenderSkybox != _knownRenderSkybox)
            {
                State.RenderSkybox = config.RenderSkybox;
                _knownRenderSkybox = config.RenderSkybox;
                changed = true;
            }
            if (!_configApplied || config.RenderAtmosphere != _knownRenderAtmosphere)
            {
                State.AtmosphereVisible = config.RenderAtmosphere;
                _knownRenderAtmosphere = config.RenderAtmosphere;
                changed = true;
            }
            if (float.IsNaN(_knownFov) || Math.Abs(config.FoV - _knownFov) > .001f)
            {
                if (!float.IsNaN(_knownFov))
                    _magnificationUntil = _runs + MAGNIFICATION_VISIBLE_UPDATES;
                _knownFov = config.FoV;
                changed = true;
            }
            _configApplied = true;
            if (changed)
                MarkDirty();
        }

        protected override void RefreshCameraForCapture()
        {
            UpdateFixedCamera();
        }

        void UpdateFixedCamera()
        {
            State.HasFixedStarCamera = true;
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
                return !_firstPerson || config == null || config.FoV <= 0f ? DEFAULT_FOV_DEGREES : config.FoV;
            }
        }

        bool ScrollFov(int delta)
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
            ApplyConfig();
            ViewChanged();
            return true;
        }

        protected override bool HandleScroll(int delta)
        {
            if (_firstPerson)
                return ScrollFov(delta);
            StarMapConfigComponent config = _host.Config;
            if (config == null)
                return false;
            float next = MathHelper.Clamp(OrbitDistanceRadii * (delta > 0 ? 1f / ZOOM_STEP : ZOOM_STEP),
                MINIMUM_DISTANCE_RADII, MAXIMUM_DISTANCE_RADII);
            if (Math.Abs(next - OrbitDistanceRadii) <= .0001f)
                return false;
            config.SensorsDistance = next;
            OrbitMoved();
            return true;
        }

        void OrbitMoved()
        {
            _host.SyncConfig();
            ViewChanged();
            RaisePropertyChanged<bool>(nameof(IsCameraLocked));
            OrbitChanged?.Invoke();
        }

        // As the game pans a camera block: within its cone limit, each axis on its own.
        void Pan(Vector2 delta)
        {
            float limit = _host.PanLimitDegrees;
            if (limit <= 0f)
                return;
            // The view follows the pointer, as dragging the picture itself would.
            float pitch = MathHelper.Clamp(PanPitchDegrees + delta.Y * PAN_SENSITIVITY, -limit, limit);
            float yaw = MathHelper.Clamp(PanYawDegrees + delta.X * PAN_SENSITIVITY, -limit, limit);
            if (pitch == PanPitchDegrees && yaw == PanYawDegrees)
                return;
            PanPitchDegrees = pitch;
            PanYawDegrees = yaw;
            ViewChanged();
            RaisePropertyChanged<bool>(nameof(IsCameraLocked));
        }

        void ResetCamera()
        {
            Host.PlayClick();
            if (_firstPerson)
            {
                PanPitchDegrees = 0f;
                PanYawDegrees = 0f;
                ViewChanged();
                RaisePropertyChanged<bool>(nameof(IsCameraLocked));
                return;
            }
            StarMapConfigComponent config = _host.Config;
            config.SensorsYaw = 0f;
            config.SensorsPitch = DEFAULT_PITCH;
            config.SensorsDistance = DEFAULT_DISTANCE_RADII;
            OrbitMoved();
        }

        protected override bool HandleDrag(Vector2 delta)
        {
            if (_firstPerson)
            {
                Pan(delta);
                return true;
            }
            StarMapConfigComponent config = _host.Config;
            if (config == null)
                return true;
            config.SensorsYaw = MathHelper.WrapAngle(OrbitYaw - delta.X * ORBIT_SENSITIVITY);
            config.SensorsPitch = MathHelper.Clamp(OrbitPitch + delta.Y * ORBIT_SENSITIVITY, -1.5f, 1.5f);
            OrbitMoved();
            return true;
        }

        protected override bool CanOrbit => false;

        void UpdateHud()
        {
            StarMapHud hud = Hud;
            hud.Visible = true;
            hud.Ladder.Clear();
            hud.HasHorizon = hud.HasAltitude = hud.PullUp = hud.ShowSpeed = hud.BoreSight = hud.HasVelocity = false;
            hud.Magnification = _firstPerson && _runs <= _magnificationUntil
                ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "StarMap_MagnificationFormat"),
                    Math.Tan(MathHelper.ToRadians(DEFAULT_FOV_DEGREES) * .5) / Math.Tan(MathHelper.ToRadians(Math.Max(.1f, FovDegrees)) * .5))
                : null;
            hud.VerticalFovRadians = MathHelper.ToRadians(FovDegrees);
            hud.Color = _host.ForegroundColor;
            hud.ErrorColor = _host.ErrorColor;
            hud.Font = _host.Font;
            hud.FontScale = _host.FontScale;
            hud.EssentialOnly = false;
            MatrixD? reference = _host.ReferenceWorldMatrix;
            UpdateLock(hud, reference);
            UpdateContacts(hud, reference);
            if (_firstPerson && reference.HasValue && _host.Block != null && _host.Block.CubeGrid != null)
                BuildInstruments(hud, reference.Value);
            else
                ResetAltitude();
            RaiseAndSetIfChanged(ref _hudVersion, _hudVersion + 1, nameof(HudVersion));
        }

        Vector2? _hover;

        void IMapSurfaceHover.Hover(Vector2? pixel)
        {
            _hover = pixel;
        }

        // The ship's outline on the canvas holds the pointer (a small ship at least a few pixels across).
        bool IsUnderPointer(MatrixD world, Vector3D offset, double radius)
        {
            return _hover.HasValue && IsAt(_hover.Value, world, offset, radius);
        }

        bool IsAt(Vector2 pixel, MatrixD world, Vector3D offset, double radius)
        {
            if (!HasCanvas)
                return false;
            double depth = Vector3D.Dot(offset, world.Forward);
            Vector2 center;
            if (depth <= 1e-3d || !TryProjectStar(world.Translation + offset, out center))
                return false;
            double focal = Canvas.Height * .5d / Math.Tan(MathHelper.ToRadians(FovDegrees) * .5d);
            float half = (float)Math.Max(12d, focal * radius / depth);
            Vector2 d = pixel - center;
            return Math.Abs(d.X) <= half && Math.Abs(d.Y) <= half;
        }

        // A click on a ship's outline locks on it; the nearest wins where outlines overlap.
        protected override void OnClick(Vector2 pixel)
        {
            MatrixD? reference = _host.ReferenceWorldMatrix;
            if (!reference.HasValue)
                return;
            MatrixD world = reference.Value;
            List<IMyCubeGrid> contacts = _host.Contacts;
            IMyCubeGrid clicked = null;
            double nearest = double.MaxValue;
            for (int i = 0; i < contacts.Count; i++)
            {
                IMyCubeGrid grid = contacts[i];
                if (grid == null || grid.MarkedForClose)
                    continue;
                BoundingSphereD volume = grid.WorldVolume;
                Vector3D offset = volume.Center - world.Translation;
                double depth = Vector3D.Dot(offset, world.Forward);
                if (depth >= nearest || !IsAt(pixel, world, offset, volume.Radius))
                    continue;
                clicked = grid;
                nearest = depth;
            }
            if (clicked != null && _host.RequestLock(clicked))
                Host.PlayClick();
        }

        void UpdateContacts(StarMapHud hud, MatrixD? reference)
        {
            hud.Contacts.Clear();
            if (!reference.HasValue)
                return;
            MatrixD world = reference.Value;
            List<IMyCubeGrid> contacts = _host.Contacts;
            IMyCubeGrid candidate = _host.LockCandidate;
            for (int i = 0; i < contacts.Count; i++)
            {
                IMyCubeGrid grid = contacts[i];
                if (grid == null || grid.MarkedForClose)
                    continue;
                BoundingSphereD volume = grid.WorldVolume;
                Vector3D offset = volume.Center - world.Translation;
                bool isCandidate = grid == candidate;
                if (!isCandidate && !_host.ShowsContact(grid, IsUnderPointer(world, offset, volume.Radius)))
                    continue;
                hud.Contacts.Add(new HudContact
                {
                    Center = ToCamera(world, offset),
                    Radius = (float)volume.Radius,
                    Name = grid.CustomName,
                    Distance = FormatingHelper.DistanceToString((float)offset.Length()),
                    Color = RelationPalette.ForGrid(grid),
                    IsLockCandidate = isCandidate
                });
            }
        }

        void UpdateLock(StarMapHud hud, MatrixD? reference)
        {
            IMyCubeGrid target;
            float percent;
            bool locked;
            hud.HasLock = false;
            if (!reference.HasValue || !_host.TryGetTargetLock(out target, out percent, out locked))
                return;
            Vector3D direction = target.WorldVolume.Center - reference.Value.Translation;
            if (direction.Normalize() <= 1e-6)
                return;
            hud.HasLock = true;
            hud.LockDirection = ToCamera(reference.Value, direction);
            hud.LockProgress = locked ? 1f : MathHelper.Clamp(percent / 100f, 0f, .99f);
            hud.LockColor = locked ? _host.ErrorColor : _host.ForegroundColor;
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
            if (_firstPerson && _altitudeWarningAt != long.MinValue && _runs - _altitudeWarningAt <= ALTITUDE_WARNING_UPDATES)
                Message.Show(LocHelper.GetLoc("DisplayName_TSS_ArtificialHorizon_AltitudeWarning"), "Warning", _host.WarningColor);
            else if (!string.IsNullOrEmpty(Notice))
                Message.Show(Notice, "Warning", _host.WarningColor);
            else
                Message.Hide();
        }

        protected override void CollectMarkers(List<MapMarker> markers)
        {
            _signals.CollectRadio(markers, _host.Config, _host.Block, _project);
            _signals.CollectGps(markers, _host.Config, _project);
        }

        bool Project(Vector3D world, out Vector2 pixel)
        {
            return TryProjectStar(world, out pixel) && IsInsideCanvas(pixel, 32f);
        }
    }
}
