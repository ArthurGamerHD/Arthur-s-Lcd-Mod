using System;
using System.Collections.Generic;
using Generated;
using Graph.Apps.Abstract;
using Graph.Apps.Utility;
using Graph.Helpers;
using Graph.System.ScreenAreas;
using Sandbox.Game;
using Sandbox.Game.GUI;
using Sandbox.ModAPI;
using VRage.Input;
using VRageMath;


namespace Graph.System.Modules
{
    public class EyeTrackingModule : IModule<IEyeTracking>
    {
        const double MAX_TRACKING_DISTANCE_METERS = 20d;
        const double MAX_TRACKING_DISTANCE_SQ = MAX_TRACKING_DISTANCE_METERS * MAX_TRACKING_DISTANCE_METERS;

        HashSet<IEyeTracking> modules = new HashSet<IEyeTracking>();
        int _lastActiveNearbyCount;
        int _lastNearbyCount;
        InteractiveEntry _hoveredClickable;
        InteractiveEntry _pressedClickable;
        object _pressedClickableDataContext;
        bool _primaryWasPressed;
        bool _useInputBlocked;

        public void Hook(IEyeTracking instance)
        {
            if (instance != null)
                modules.Add(instance);
        }

        public void Unhook(IEyeTracking instance)
        {
            if (instance == null)
                return;

            modules.Remove(instance);
        }

        public int Count => modules.Count;
        public int ActiveCount => _lastActiveNearbyCount;

        public void Update()
        {
            if (!(MyAPIGateway.Session.LocalHumanPlayer.Character.ControllerInfo.IsLocallyHumanControlled() ||
                  MyAPIGateway.Input.IsAnyAltKeyPressed()))
            {
                if (_useInputBlocked)
                    LcdModSessionComponent.SetLocalPlayerUseInputBlocked(blocked: _useInputBlocked = false);

                return;
            }
               
            
            Vector3D cameraPos;
            Vector3D cameraForward;
            if (!TryGetCameraRay(out cameraPos, out cameraForward))
            {
                UpdateClickState(null, null);
                _lastActiveNearbyCount = 0;
                return;
            }

            var nearbyCount = 0;
            var resolvedCount = 0;
            InteractiveEntry hoveredClickable = null;
            IEyeTracking EyeTrackingEntity = null;
            double hoveredDistanceSq = double.MaxValue;
            foreach (var screen in modules)
            {
                var surfaceScript = screen as SurfaceScriptBase;
                if (surfaceScript == null || screen.Block == null)
                    continue;

                var blockPos = screen.Block.WorldMatrix.Translation;
                if (Vector3D.DistanceSquared(blockPos, cameraPos) > MAX_TRACKING_DISTANCE_SQ)
                    continue;
                nearbyCount++;

                Vector2 lookAtCoordinates;
                if (ScreenAreaGeometry.TryGetScreenPointIntersection(
                        surfaceScript,
                        cameraPos,
                        cameraForward,
                        out lookAtCoordinates))
                {
                    screen.LookAt(lookAtCoordinates);
                    resolvedCount++;

                    InteractiveEntry clickable;
                    if (TryGetHoveredClickable(screen, out clickable))
                    {
                        var distanceSq = Vector3D.DistanceSquared(blockPos, cameraPos);
                        if (distanceSq < hoveredDistanceSq)
                        {
                            hoveredDistanceSq = distanceSq;
                            hoveredClickable = clickable;
                            EyeTrackingEntity = screen;
                        }
                    }
                }
            }

            UpdateClickState(hoveredClickable, EyeTrackingEntity);
            _lastNearbyCount = nearbyCount;
            _lastActiveNearbyCount = resolvedCount;
        }

        public void PostUpdate()
        {
        }

        void UpdateClickState(InteractiveEntry hoveredClickable, IEyeTracking eyeTrackingEntity)
        {
            _hoveredClickable = hoveredClickable;
            bool shouldBlockUse = hoveredClickable != null;

            if (_useInputBlocked != shouldBlockUse)
            {
                LcdModSessionComponent.SetLocalPlayerUseInputBlocked(blocked: shouldBlockUse);
                _useInputBlocked = shouldBlockUse;
            }

            bool primaryPressed = shouldBlockUse && MyAPIGateway.Input != null && HoldingClick;

            if (primaryPressed && !_primaryWasPressed)
            {
                _pressedClickable = hoveredClickable;
                _pressedClickableDataContext = hoveredClickable.DataContext ?? hoveredClickable;
            }

            if (!primaryPressed && _primaryWasPressed)
            {
                var hoveredDataContext = hoveredClickable != null ? hoveredClickable.DataContext ?? hoveredClickable : null;
                if (_pressedClickable != null && hoveredClickable != null &&
                    Equals(objA: _pressedClickableDataContext, objB: hoveredDataContext))
                {
                    try
                    {
                        var click = hoveredClickable.Click(sender: eyeTrackingEntity);
                        eyeTrackingEntity.PlaySounds(click ? hoveredClickable.ClickSound : hoveredClickable.ClickFailSound);
                    }
                    catch (Exception e)
                    {
                        eyeTrackingEntity.PlaySounds(hoveredClickable.ClickFailSound);
                        ErrorHandlerHelper.LogError(error: e, source: this);
                    }
                }

                _pressedClickable = null;
                _pressedClickableDataContext = null;
            }

            if (!shouldBlockUse)
            {
                _pressedClickable = null;
                _pressedClickableDataContext = null;
            }

            _primaryWasPressed = primaryPressed;
        }

        public bool HoldingClick => MyAPIGateway.Input.IsLeftMousePressed() || MyAPIGateway.Input.IsJoystickButtonPressed(button: MyJoystickButtonsEnum.J06);

        static bool TryGetHoveredClickable(IEyeTracking screen, out InteractiveEntry clickable)
        {
            clickable = null;
            var entries = screen.InteractiveEntries;
            if (entries == null || entries.Count == 0)
                return false;

            var position = screen.CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y))
                return false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry == null || !entry.Hit(position))
                    continue;

                if (entry.OnClick == null)
                    continue;

                clickable = entry;
                return true;
            }

            return false;
        }

        static bool TryGetCameraRay(out Vector3D origin, out Vector3D direction)
        {
            origin = Vector3D.Zero;
            direction = Vector3D.Zero;

            var session = MyAPIGateway.Session;
            if (session == null)
                return false;

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