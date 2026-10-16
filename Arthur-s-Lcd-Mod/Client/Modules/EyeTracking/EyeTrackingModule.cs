using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Helpers;
using LcdMod.Client.ScreenAreas;
using LcdMod.Client.Utility;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using LcdEyeTracking.Api;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Modules.EyeTracking
{
    public class EyeTrackingModule : IModule<IEyeTracking>
    {
        public static InteractiveSurfaceScript CurrentPointerSurface { get; private set; }
        public static InteractiveSurfaceScript LastPointerSurface { get; private set; }

        const double MAX_TRACKING_DISTANCE_METERS = 20d;
        const double MAX_TRACKING_DISTANCE_SQ = MAX_TRACKING_DISTANCE_METERS * MAX_TRACKING_DISTANCE_METERS;
        const long ACTIVE_SURFACE_TIMEOUT_FRAMES = 30;

        readonly HashSet<IEyeTracking> _modules = new HashSet<IEyeTracking>();
        readonly List<IEyeTracking> _pendingModules = new List<IEyeTracking>();

        int _lastActiveNearbyCount;
        InteractiveSurfaceScript _pointerSurface;
        readonly PointerInputCapture<InteractiveSurfaceScript> _pointerButtons =
            new PointerInputCapture<InteractiveSurfaceScript>(surface => surface.ResetPointerButtonInput());

        readonly IMyControl _moveCameraControl =
            MyAPIGateway.Input.GetGameControl(MyStringId.GetOrCompute("LOOKAROUND"));

        // Optional LCD Eye Tracking API mod. When it is loaded, gaze and clicks for every
        // mod's screens come from it (so screens from different mods don't compete),
        // and this module only forwards its events. Without it, tracking runs here.
        const long EYE_TRACKING_API_REPLY_CHANNEL = (LcdMod.Common.Helpers.Constants.WORKSHOP_ID << 8) | 3L;
        static LcdEyeTrackingApi _eyeTrackingApi;
        bool _eyeTrackingApiResolved;

        sealed class ApiRegistration
        {
            public long Handle;
            public long Group;
            public PointerFlags Flags;
        }

        readonly Dictionary<IEyeTracking, ApiRegistration> _apiRegistrations =
            new Dictionary<IEyeTracking, ApiRegistration>();
        readonly Dictionary<InteractiveSurfaceScript, long> _apiCaptureGroups =
            new Dictionary<InteractiveSurfaceScript, long>();
        long _nextApiCaptureGroup;

        /// <summary>True when the LCD Eye Tracking API mod tracks the screens instead of this module.</summary>
        public static bool UsesEyeTrackingApi => _eyeTrackingApi != null;

        public void Hook(IEyeTracking instance)
        {
            if (instance != null)
                _pendingModules.Add(instance);
            else
                LogHelper.Log(MyLogSeverity.Warning, $"{nameof(EyeTrackingModule)} tried to register an null instance");
        }

        public void Unhook(IEyeTracking instance)
        {
            if (instance == null)
                return;

            if (ReferenceEquals(_pointerSurface, instance))
                UpdatePointerTarget(null);

            if (ReferenceEquals(_pointerButtons.Current, instance))
                UpdatePointerButtonTarget(null, false, false, false, false, false);

            _modules.Remove(instance);
            UnregisterFromApi(instance);
        }

        public int Count => _modules.Count;
        public int ActiveCount => _lastActiveNearbyCount;

        public static void ClearPointerHistory()
        {
            CurrentPointerSurface = null;
            LastPointerSurface = null;
        }

        public void Update()
        {
#if DEBUG
            long profileStart = Debugging.FrameProfiler.Start();
            _profiledTests = 0;
            _profiledLookedAt = 0;
            try
            {
                UpdateCore();
            }
            finally
            {
                Debugging.FrameProfiler.EndEyeTracking(profileStart, _profiledTests, _profiledLookedAt);
            }
        }

        int _profiledTests;
        int _profiledLookedAt;

        void UpdateCore()
        {
#endif
            foreach (var module in _pendingModules)
                _modules.Add(module);

            _pendingModules.Clear();

            if (!_eyeTrackingApiResolved)
            {
                _eyeTrackingApiResolved = true;
                try
                {
                    _eyeTrackingApi = LcdEyeTrackingApiClient.TryGet(EYE_TRACKING_API_REPLY_CHANNEL);
                }
                catch (Exception e)
                {
                    _eyeTrackingApi = null;
                    ErrorHandlerHelper.LogError(e, this);
                }
                if (_eyeTrackingApi != null)
                    LogHelper.LogInfo("LCD Eye Tracking API " + _eyeTrackingApi.GetApiVersion() + " found; screen input comes from it.");
            }
            if (_eyeTrackingApi != null)
            {
                UpdateApiRegistrations();
                return;
            }

            if (LcdTextEntryScreen.IsActive)
                return;

            var player = MyAPIGateway.Session.LocalHumanPlayer;
            var entity = player?.Controller?.ControlledEntity?.Entity as IMyShipController as MyCubeBlock;

            Vector3D cameraPos;
            Vector3D cameraForward;
            if (MyAPIGateway.Gui.IsCursorVisible ||
                !_moveCameraControl.IsPressed() &&
                ((entity?.BlockDefinition as MyCockpitDefinition)?.EnableShipControl ?? false) ||
                LocalPlayerBlockStateHelper.IsBlockPlacerActive() ||
                !TryGetCameraRay(out cameraPos, out cameraForward))
            {
                _pointerButtons.Select(null, false);
                UpdatePointerTarget(null);
                _lastActiveNearbyCount = 0;
                return;
            }

            var resolvedCount = 0;
            IEyeTracking lookingScreen = null;
            double lookingDistance = double.MaxValue;
            Vector2 lookingCoordinates = Vector2.Zero;

            foreach (var screen in _modules)
            {
                var surfaceScript = screen as InteractiveSurfaceScript;

                if (surfaceScript == null || screen.Block == null)
                    continue;

                if (!HasRecentlyRun(surfaceScript))
                    continue;

                if (surfaceScript.RequiresAlt && !_moveCameraControl.IsPressed())
                    continue;

                var blockPos = screen.Block.WorldMatrix.Translation;
                if (Vector3D.DistanceSquared(blockPos, cameraPos) > MAX_TRACKING_DISTANCE_SQ)
                    continue;

                Vector2 lookAtCoordinates;
                double hitDistance;
#if DEBUG
                _profiledTests++;
                long rayStart = Debugging.FrameProfiler.Start();
#endif
                bool hit = ScreenAreaGeometry.TryGetScreenPointIntersection(
                    surfaceScript,
                    cameraPos,
                    cameraForward,
                    out lookAtCoordinates,
                    out hitDistance);
#if DEBUG
                Debugging.FrameProfiler.AddRayTest(rayStart);
#endif

                if (hit)
                {
                    resolvedCount++;

                    if (hitDistance < lookingDistance)
                    {
                        lookingDistance = hitDistance;
                        lookingScreen = screen;
                        lookingCoordinates = lookAtCoordinates;
                    }
                }
            }

            // Only the nearest screen along the ray is being looked at; the ray passes through it to screens behind,
            // and every looked-at screen redraws each frame.
            if (lookingScreen != null)
            {
#if DEBUG
                long lookAtStart = Debugging.FrameProfiler.Start();
#endif
                lookingScreen.LookAt(lookingCoordinates);
#if DEBUG
                Debugging.FrameProfiler.AddLookAt(lookAtStart);
                _profiledLookedAt++;
#endif
            }

            var pointerSurface = lookingScreen as InteractiveSurfaceScript;
            UpdatePointerTarget(pointerSurface);

            try
            {
                UpdateScrollState(lookingScreen);
                UpdatePointerButtonTarget(
                    pointerSurface,
                    HoldingClick,
                    HoldingRightClick,
                    HoldingMiddleClick,
                    HoldingBackClick,
                    HoldingForwardClick);
            }
            catch (System.Exception e)
            {
                pointerSurface?.OnException(e);
            }

            _lastActiveNearbyCount = resolvedCount;
        }

        void UpdateApiRegistrations()
        {
            foreach (var screen in _modules)
            {
                var surfaceScript = screen as InteractiveSurfaceScript;
                var surface = surfaceScript == null ? null : surfaceScript.Surface as Sandbox.ModAPI.IMyTextSurface;
                if (surfaceScript == null || screen.Block == null || surface == null)
                    continue;

                // Proxies of one app share its input owner, so a held button may cross panels.
                long group = GetApiCaptureGroup(surfaceScript.PointerInputOwner ?? surfaceScript);
                ApiRegistration registration;
                if (!_apiRegistrations.TryGetValue(screen, out registration) || registration.Group != group)
                {
                    UnregisterFromApi(screen);
                    registration = new ApiRegistration
                    {
                        Group = group,
                        Handle = _eyeTrackingApi.Register(surfaceScript.Block, surface,
                            position => OnApiLookAt(surfaceScript, position),
                            () => OnApiLeave(surfaceScript),
                            (button, pressed) => OnApiButton(surfaceScript, button, pressed),
                            delta => OnApiScroll(surfaceScript, delta),
                            () => OnApiCaptureLost(surfaceScript),
                            group)
                    };
                    _apiRegistrations[screen] = registration;
                }
                if (registration.Handle == 0L)
                    continue;

                PointerFlags flags = PointerFlags.RequiresHeartbeat |
                                   (surfaceScript.RequiresAlt ? PointerFlags.RequiresAlt : PointerFlags.None);
                if (registration.Flags != flags)
                {
                    registration.Flags = flags;
                    _eyeTrackingApi.SetFlags(registration.Handle, flags);
                }
                if (HasRecentlyRun(surfaceScript))
                    _eyeTrackingApi.Heartbeat(registration.Handle);
            }
        }

        long GetApiCaptureGroup(InteractiveSurfaceScript owner)
        {
            long group;
            if (!_apiCaptureGroups.TryGetValue(owner, out group))
                _apiCaptureGroups[owner] = group = ++_nextApiCaptureGroup;
            return group;
        }

        void UnregisterFromApi(IEyeTracking screen)
        {
            ApiRegistration registration;
            if (!_apiRegistrations.TryGetValue(screen, out registration))
                return;
            _apiRegistrations.Remove(screen);
            var surfaceScript = screen as InteractiveSurfaceScript;
            if (surfaceScript != null)
                _apiCaptureGroups.Remove(surfaceScript);
            if (_eyeTrackingApi != null && registration.Handle != 0L)
                _eyeTrackingApi.Unregister(registration.Handle);
        }

        // A native text-entry screen owns the keyboard and mouse; only releases get through.
        void OnApiLookAt(InteractiveSurfaceScript surface, Vector2 position)
        {
            if (LcdTextEntryScreen.IsActive)
                return;
            RunApiEvent(surface, () =>
            {
                surface.LookAt(position);
                UpdatePointerTarget(surface);
                _lastActiveNearbyCount = 1;
            });
        }

        void OnApiLeave(InteractiveSurfaceScript surface)
        {
            if (ReferenceEquals(_pointerSurface, surface))
                UpdatePointerTarget(null);
            _lastActiveNearbyCount = 0;
        }

        void OnApiButton(InteractiveSurfaceScript surface, int button, bool pressed)
        {
            if (pressed && LcdTextEntryScreen.IsActive)
                return;
            RunApiEvent(surface, () =>
            {
                InteractiveSurfaceScript owner = surface.PointerInputOwner;
                if (owner != null && owner.HandlesPointerButtonInput)
                    owner.SetPointerButtonState((ControlClickButton)button, pressed);
            });
        }

        void OnApiScroll(InteractiveSurfaceScript surface, int delta)
        {
            if (!LcdTextEntryScreen.IsActive)
                RunApiEvent(surface, () => surface.MouseScroll(delta));
        }

        void OnApiCaptureLost(InteractiveSurfaceScript surface)
        {
            RunApiEvent(surface, () =>
            {
                InteractiveSurfaceScript owner = surface.PointerInputOwner;
                if (owner != null)
                    owner.ResetPointerButtonInput();
            });
        }

        static void RunApiEvent(InteractiveSurfaceScript surface, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                surface.OnException(e);
            }
        }

        static bool HasRecentlyRun(InteractiveSurfaceScript surfaceScript)
        {
            if (surfaceScript == null)
                return false;

            return surfaceScript.LastRunTick != long.MinValue &&
                   MyAPIGateway.Session.GameplayFrameCounter - surfaceScript.LastRunTick <=
                   ACTIVE_SURFACE_TIMEOUT_FRAMES;
        }

        public void PostUpdate()
        {
        }

        void UpdatePointerButtonTarget(
            InteractiveSurfaceScript surface,
            bool primaryPressed,
            bool secondaryPressed,
            bool middlePressed,
            bool backPressed,
            bool forwardPressed)
        {
            bool panelGap = surface == null;
            if (surface != null)
                surface = surface.PointerInputOwner;
            if (surface != null && !surface.HandlesPointerButtonInput)
                surface = null;

            bool held = primaryPressed || secondaryPressed || middlePressed || backPressed || forwardPressed;
            InteractiveSurfaceScript previous = _pointerButtons.Current;
            bool previousAlive = previous != null && previous.Block != null && !previous.Block.MarkedForClose;
            _pointerButtons.Select(surface, panelGap && held && previousAlive);

            InteractiveSurfaceScript target = _pointerButtons.Current;
            if (target == null)
                return;

            target.SetPointerButtonState(ControlClickButton.Primary, primaryPressed);
            target.SetPointerButtonState(ControlClickButton.Secondary, secondaryPressed);
            target.SetPointerButtonState(ControlClickButton.Middle, middlePressed);
            target.SetPointerButtonState(ControlClickButton.Back, backPressed);
            target.SetPointerButtonState(ControlClickButton.Forward, forwardPressed);
        }

        void UpdatePointerTarget(InteractiveSurfaceScript surface)
        {
            _pointerSurface = surface;
            CurrentPointerSurface = surface;
            if (surface != null)
                LastPointerSurface = surface;
        }

        static void UpdateScrollState(IEyeTracking lookingScreen)
        {
            if (lookingScreen == null)
                return;

            var delta = MyAPIGateway.Input.DeltaMouseScrollWheelValue();

            if (delta != 0)
                lookingScreen.MouseScroll(delta);
        }

        public static bool HoldingClick => MyAPIGateway.Input.IsLeftMousePressed() ||
                                           MyAPIGateway.Input.IsJoystickButtonPressed(
                                               button: MyJoystickButtonsEnum.J06);

        public static bool HoldingRightClick => MyAPIGateway.Input.IsRightMousePressed();

        public static bool HoldingMiddleClick => MyAPIGateway.Input.IsMiddleMousePressed();

        public static bool HoldingBackClick => MyAPIGateway.Input.IsXButton1MousePressed();

        public static bool HoldingForwardClick => MyAPIGateway.Input.IsXButton2MousePressed();

        static bool TryGetCameraRay(out Vector3D origin, out Vector3D direction)
        {
            origin = Vector3D.Zero;
            direction = Vector3D.Zero;

            var session = MyAPIGateway.Session;

            var camera = session.Camera;

            if (camera != null)
            {
                var matrix = camera.WorldMatrix;
                origin = matrix.Translation;
                direction = matrix.Forward;

                if (direction.LengthSquared() > 1e-8)
                {
                    direction.Normalize();
                    return true;
                }
            }

            var cameraEntity = session.CameraController?.Entity;

            if (cameraEntity == null)
                return false;

            var fallback = cameraEntity.WorldMatrix;
            origin = fallback.Translation;
            direction = fallback.Forward;

            if (direction.LengthSquared() <= 1e-8)
                return false;

            direction.Normalize();
            return true;
        }
    }
}
