using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Helpers;
using LcdMod.Client.Utility;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Input;
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

        const long ACTIVE_SURFACE_TIMEOUT_FRAMES = 30;

        readonly HashSet<IEyeTracking> _modules = new HashSet<IEyeTracking>();
        readonly List<IEyeTracking> _pendingModules = new List<IEyeTracking>();

        int _lastActiveNearbyCount;
        InteractiveSurfaceScript _pointerSurface;

        // Acrux UI ships the eye tracking API: gaze and clicks for every mod's screens come from it (so screens from
        // different mods don't compete), and this module only forwards its events.
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
#endif
            foreach (var module in _pendingModules)
                _modules.Add(module);

            _pendingModules.Clear();

            if (!_eyeTrackingApiResolved)
            {
                _eyeTrackingApiResolved = true;
                try
                {
                    _eyeTrackingApi = AcruxClient.TryGet(EYE_TRACKING_API_REPLY_CHANNEL);
                }
                catch (Exception e)
                {
                    _eyeTrackingApi = null;
                    ErrorHandlerHelper.LogError(e, this);
                }
                if (_eyeTrackingApi == null)
                    LogHelper.Log(MyLogSeverity.Warning, "Acrux UI eye tracking API not found; LCD screens take no input.");
            }
            if (_eyeTrackingApi != null)
                UpdateApiRegistrations();
#if DEBUG
            Debugging.FrameProfiler.EndEyeTracking(profileStart);
#endif
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

        // A native text-entry or wrist pad screen owns the keyboard and mouse; only releases get through.
        static bool NativeScreenOwnsPointer => LcdTextEntryScreen.IsActive || WristpadInputScreen.IsActive;

        void OnApiLookAt(InteractiveSurfaceScript surface, Vector2 position)
        {
            if (NativeScreenOwnsPointer)
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
            if (pressed && NativeScreenOwnsPointer)
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
            if (!NativeScreenOwnsPointer)
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

        void UpdatePointerTarget(InteractiveSurfaceScript surface)
        {
            _pointerSurface = surface;
            CurrentPointerSurface = surface;
            if (surface != null)
                LastPointerSurface = surface;
        }

        public static bool HoldingClick => MyAPIGateway.Input.IsLeftMousePressed() ||
                                           MyAPIGateway.Input.IsJoystickButtonPressed(
                                               button: MyJoystickButtonsEnum.J06);
    }
}
