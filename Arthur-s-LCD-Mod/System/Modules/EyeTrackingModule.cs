using System;
using System.Collections.Generic;
using System.Linq;
using Generated;
using Graph.Apps.Abstract;
using Graph.Apps.Utility;
using Graph.Helpers;
using Graph.System.Config.Models;
using Graph.System.ScreenAreas;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.GUI;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Input;
using VRageMath;


namespace Graph.System.Modules
{
    public class EyeTrackingModule : IModule<IEyeTracking>
    {
        const double MAX_TRACKING_DISTANCE_METERS = 20d;
        const double MAX_TRACKING_DISTANCE_SQ = MAX_TRACKING_DISTANCE_METERS * MAX_TRACKING_DISTANCE_METERS;

        readonly HashSet<IEyeTracking> _modules = new HashSet<IEyeTracking>();
        readonly List<IEyeTracking> _pendingModules = new List<IEyeTracking>();
        int _lastActiveNearbyCount;
        int _lastNearbyCount;
        InteractiveEntry _hoveredClickable;
        InteractiveEntry _pressedClickable;
        object _pressedClickableDataContext;
        bool _primaryWasPressed;
        bool _secondaryWasPressed;
        bool _useInputBlocked;

        public void Hook(IEyeTracking instance)
        {
            if (instance != null)
                _pendingModules.Add(instance);
        }
        

        public void Unhook(IEyeTracking instance)
        {
            if (instance == null)
                return;

            _modules.Remove(instance);
        }

        public int Count => _modules.Count;
        public int ActiveCount => _lastActiveNearbyCount;

        public void Update()
        {
            foreach (var module in _pendingModules) 
                _modules.Add(module);

            _pendingModules.Clear();
            
            var player = MyAPIGateway.Session?.LocalHumanPlayer;

            var entity = player?.Controller?.ControlledEntity?.Entity as IMyShipController as MyCubeBlock;


            if (MyAPIGateway.Gui.IsCursorVisible || (!MyAPIGateway.Input.IsAnyAltKeyPressed() &&
                                                     ((entity?.BlockDefinition as MyCockpitDefinition)
                                                         ?.EnableShipControl ?? false)))
            {
                if (_useInputBlocked)
                    LcdModSessionComponent.SetLocalPlayerUseInputBlocked(blocked: _useInputBlocked = false);

                return;
            }

            Vector3D cameraPos;
            Vector3D cameraForward;
            if (!TryGetCameraRay(out cameraPos, out cameraForward))
            {
                UpdateClickState(null, null, false, false);
                _lastActiveNearbyCount = 0;
                return;
            }

            var nearbyCount = 0;
            var resolvedCount = 0;
            InteractiveEntry hoveredClickable = null;
            IEyeTracking eyeTrackingEntity = null;
            IEyeTracking tooltipInputEntity = null;
            bool tooltipBlocksPrimary = false;
            bool tooltipBlocksSecondary = false;
            double hoveredDistanceSq = double.MaxValue;
            double tooltipDistanceSq = double.MaxValue;

            foreach (var screen in _modules)
            {
                var surfaceScript = screen as InteractiveSurfaceScript;
                if (surfaceScript == null || screen.Block == null)
                    continue;

                if (surfaceScript.RequiresAlt && !MyAPIGateway.Input.IsAnyAltKeyPressed())
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

                    var distanceSq = Vector3D.DistanceSquared(blockPos, cameraPos);

                    var interactiveSurface = screen as InteractiveSurfaceScript;
                    if (interactiveSurface != null && distanceSq < tooltipDistanceSq)
                    {
                        bool blocksPrimary = interactiveSurface.HasTooltipInputAtCursor(false);
                        bool blocksSecondary = interactiveSurface.HasTooltipInputAtCursor(true);
                        if (blocksPrimary || blocksSecondary)
                        {
                            tooltipDistanceSq = distanceSq;
                            tooltipInputEntity = screen;
                            tooltipBlocksPrimary = blocksPrimary;
                            tooltipBlocksSecondary = blocksSecondary;
                        }
                    }

                    InteractiveEntry clickable;
                    if (TryGetHoveredClickable(screen, out clickable))
                    {
                        if (distanceSq < hoveredDistanceSq)
                        {
                            hoveredDistanceSq = distanceSq;
                            hoveredClickable = clickable;
                            eyeTrackingEntity = screen;
                        }
                    }
                }
            }

            if (tooltipInputEntity != null && tooltipDistanceSq < hoveredDistanceSq)
                eyeTrackingEntity = tooltipInputEntity;

            UpdateClickState(hoveredClickable, eyeTrackingEntity, tooltipBlocksPrimary, tooltipBlocksSecondary);
            _lastNearbyCount = nearbyCount;
            _lastActiveNearbyCount = resolvedCount;
        }

        public void PostUpdate()
        {
        }

        void UpdateClickState(
            InteractiveEntry hoveredClickable,
            IEyeTracking eyeTrackingEntity,
            bool tooltipBlocksPrimary,
            bool tooltipBlocksSecondary)
        {
            _hoveredClickable = hoveredClickable;

            bool primaryPressed = MyAPIGateway.Input != null && HoldingClick;
            bool secondaryPressed = MyAPIGateway.Input != null && HoldingRightClick;
            bool shouldBlockUse = hoveredClickable != null || tooltipBlocksPrimary || tooltipBlocksSecondary;

            if (_useInputBlocked != shouldBlockUse)
            {
                LcdModSessionComponent.SetLocalPlayerUseInputBlocked(blocked: shouldBlockUse);
                _useInputBlocked = shouldBlockUse;
            }

            if ((primaryPressed && !_primaryWasPressed) || (secondaryPressed && !_secondaryWasPressed))
            {
                _pressedClickable = hoveredClickable;
                _pressedClickableDataContext = hoveredClickable != null ? hoveredClickable.DataContext ?? hoveredClickable : null;
            }

            if ((!primaryPressed && _primaryWasPressed) || (!secondaryPressed && _secondaryWasPressed))
            {
                var hoveredDataContext = hoveredClickable != null ? hoveredClickable.DataContext ?? hoveredClickable : null;

                try
                {
                    var click = _pressedClickable != null && hoveredClickable != null &&
                                Equals(objA: _pressedClickableDataContext, objB: hoveredDataContext) &&
                                (_primaryWasPressed
                                    ? hoveredClickable.Click(eyeTrackingEntity)
                                    : hoveredClickable.SecondaryClick(eyeTrackingEntity))
                        ; // handle click first

                    InteractiveEntry tooltipParent;
                    click = click || TryHandleTooltipActivation(eyeTrackingEntity,
                        rightClick: !_primaryWasPressed && _secondaryWasPressed,
                        tooltipParent: out tooltipParent); // then handle tooltip if needed

                    if (eyeTrackingEntity != null)
                        eyeTrackingEntity.PlaySounds(
                            hoveredClickable == null
                                ? AudioHelper.HudClick
                                : click
                                    ? hoveredClickable.ClickSound
                                    : hoveredClickable.ClickFailSound);
                }
                catch (Exception e)
                {
                    if (eyeTrackingEntity != null)
                        eyeTrackingEntity.PlaySounds(hoveredClickable.ClickFailSound);
                    ErrorHandlerHelper.LogError(error: e, source: this);
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
            _secondaryWasPressed = secondaryPressed;
        }

        static bool TryHandleTooltipActivation(
            IEyeTracking eyeTrackingEntity,
            bool rightClick,
            out InteractiveEntry tooltipParent)
        {
            tooltipParent = null;

            var interactiveSurface = eyeTrackingEntity as InteractiveSurfaceScript;
            return interactiveSurface != null &&
                   interactiveSurface.TryHandleTooltipActivationClick(rightClick, out tooltipParent);
        }

        public static bool HoldingClick => MyAPIGateway.Input.IsLeftMousePressed() ||
                                    MyAPIGateway.Input.IsJoystickButtonPressed(button: MyJoystickButtonsEnum.J06);

        public static bool HoldingRightClick => MyAPIGateway.Input.IsRightMousePressed();

        static bool TryGetHoveredClickable(IEyeTracking screen, out InteractiveEntry clickable)
        {
            clickable = null;

            var entries = screen.InteractiveEntries;
            if (entries == null || !entries.Any())
                return false;

            var position = screen.CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y))
                return false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries.ElementAt(i);
                if (TryResolveHitClickable(screen, entry, position, out clickable))
                    return true;
            }

            return false;
        }

        static bool TryResolveHitClickable(
            IEyeTracking screen,
            InteractiveEntry entry,
            Vector2 position,
            out InteractiveEntry clickable)
        {
            clickable = null;

            if (entry == null || !entry.Visible)
                return false;

            bool selfHit = entry.Hit(position);

            var children = entry.Children;
            bool hasChild;

            var interactiveSurface = screen as InteractiveSurfaceScript;
            if (interactiveSurface != null)
                hasChild = interactiveSurface.IsInsideContainer(entry, position);
            else
                hasChild = selfHit && children != null && children.Count > 0;

            if (hasChild && children != null && children.Count > 0)
            {
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    if (TryResolveHitClickable(screen, children[i], position, out clickable))
                        return true;
                }
            }

            if (!selfHit)
                return false;

            if (!entry.CanClick)
                return false;

            clickable = entry;
            return true;
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