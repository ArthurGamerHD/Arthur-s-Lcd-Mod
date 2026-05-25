using System;
using System.Collections.Generic;
using Generated;
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
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Modules.EyeTracking
{
    public class EyeTrackingModule : IModule<IEyeTracking>
    {
        const double MAX_TRACKING_DISTANCE_METERS = 20d;
        const double MAX_TRACKING_DISTANCE_SQ = MAX_TRACKING_DISTANCE_METERS * MAX_TRACKING_DISTANCE_METERS;
        const long ACTIVE_SURFACE_TIMEOUT_FRAMES = 30;

        readonly HashSet<IEyeTracking> _modules = new HashSet<IEyeTracking>();
        readonly List<IEyeTracking> _pendingModules = new List<IEyeTracking>();
        int _lastActiveNearbyCount;
        ControlBase _pressedClickable;
        object _pressedClickableDataContext;
        bool _primaryWasPressed;
        bool _secondaryWasPressed;

        readonly IMyControl _moveCameraControl =
            MyAPIGateway.Input.GetGameControl(MyStringId.GetOrCompute("LOOKAROUND"));

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


            if (MyAPIGateway.Gui.IsCursorVisible || (!_moveCameraControl.IsPressed() &&
                                                     ((entity?.BlockDefinition as MyCockpitDefinition)
                                                         ?.EnableShipControl ?? false)))
                return;

            Vector3D cameraPos;
            Vector3D cameraForward;
            if (!TryGetCameraRay(out cameraPos, out cameraForward))
            {
                UpdateClickState(null, null);
                _lastActiveNearbyCount = 0;
                return;
            }

            var resolvedCount = 0;
            ControlBase hoveredClickable = null;
            IEyeTracking eyeTrackingEntity = null;
            IEyeTracking tooltipInputEntity = null;
            IEyeTracking lookingScreen = null;
            bool? activeClickButton = GetActiveClickButton();
            double hoveredDistanceSq = double.MaxValue;
            double tooltipDistanceSq = double.MaxValue;
            double lookingDistanceSq = double.MaxValue;

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
                if (ScreenAreaGeometry.TryGetScreenPointIntersection(
                        surfaceScript,
                        cameraPos,
                        cameraForward,
                        out lookAtCoordinates))
                {
                    screen.LookAt(lookAtCoordinates);
                    resolvedCount++;

                    var distanceSq = Vector3D.DistanceSquared(blockPos, cameraPos);
                    if (distanceSq < lookingDistanceSq)
                    {
                        lookingDistanceSq = distanceSq;
                        lookingScreen = screen;
                    }

                    var interactiveSurface = screen as InteractiveSurfaceScript;
                    if (distanceSq < tooltipDistanceSq)
                    {
                        try
                        {
                            bool blocksPrimary = interactiveSurface.HasTooltipInputAtCursor(false);
                            bool blocksSecondary = interactiveSurface.HasTooltipInputAtCursor(true);
                            if (blocksPrimary || blocksSecondary)
                            {
                                tooltipDistanceSq = distanceSq;
                                tooltipInputEntity = screen;
                            }
                        }
                        catch (Exception e)
                        {
                            interactiveSurface.OnException(e);
                        }
                    }

                    ControlBase clickable;
                    if (TryGetHoveredClickable(screen, activeClickButton, out clickable))
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

            try
            {
                UpdateScrollState(lookingScreen);
                UpdateClickState(hoveredClickable, eyeTrackingEntity);
            }
            catch (Exception e)
            {
                (lookingScreen as InteractiveSurfaceScript)?.OnException(e);
            }

            _lastActiveNearbyCount = resolvedCount;
        }

        static bool HasRecentlyRun(InteractiveSurfaceScript surfaceScript)
        {
            if (surfaceScript == null || MyAPIGateway.Session == null)
                return false;

            return surfaceScript.LastRunTick != long.MinValue &&
                   MyAPIGateway.Session.GameplayFrameCounter - surfaceScript.LastRunTick <=
                   ACTIVE_SURFACE_TIMEOUT_FRAMES;
        }

        public void PostUpdate()
        {
        }

        void UpdateClickState(
            ControlBase hoveredClickable,
            IEyeTracking eyeTrackingEntity)
        {
            bool primaryPressed = MyAPIGateway.Input != null && HoldingClick;
            bool secondaryPressed = MyAPIGateway.Input != null && HoldingRightClick;
            bool hasInputTarget = hoveredClickable != null || eyeTrackingEntity != null;

            if ((primaryPressed && !_primaryWasPressed) || (secondaryPressed && !_secondaryWasPressed))
            {
                _pressedClickable = hoveredClickable;
                _pressedClickableDataContext =
                    hoveredClickable != null ? hoveredClickable.DataContext ?? hoveredClickable : null;
            }

            if ((!primaryPressed && _primaryWasPressed) || (!secondaryPressed && _secondaryWasPressed))
            {
                var hoveredDataContext =
                    hoveredClickable != null ? hoveredClickable.DataContext ?? hoveredClickable : null;

                try
                {
                    var interactiveSurface = eyeTrackingEntity as InteractiveSurfaceScript;
                    var rightClick = !_primaryWasPressed && _secondaryWasPressed;
                    ControlBase clickedControl;
                    var click = _pressedClickable != null && hoveredClickable != null &&
                                Equals(objA: _pressedClickableDataContext, objB: hoveredDataContext) &&
                                (interactiveSurface != null
                                    ? interactiveSurface.TryClickAtCursor(rightClick, eyeTrackingEntity, out clickedControl)
                                    : rightClick
                                        ? hoveredClickable.SecondaryClick(eyeTrackingEntity)
                                        : hoveredClickable.Click(eyeTrackingEntity))
                        ; // handle click first

                    ControlBase tooltipParent;
                    click = click || TryHandleTooltipActivation(eyeTrackingEntity,
                        rightClick: rightClick,
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

            if (!hasInputTarget)
            {
                _pressedClickable = null;
                _pressedClickableDataContext = null;
            }

            _primaryWasPressed = primaryPressed;
            _secondaryWasPressed = secondaryPressed;
        }

        static void UpdateScrollState(IEyeTracking lookingScreen)
        {
            if (lookingScreen == null || MyAPIGateway.Input == null)
                return;

            var delta = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
            if (delta != 0)
                lookingScreen.MouseScroll(delta);
        }

        static bool TryHandleTooltipActivation(
            IEyeTracking eyeTrackingEntity,
            bool rightClick,
            out ControlBase tooltipParent)
        {
            tooltipParent = null;

            var interactiveSurface = eyeTrackingEntity as InteractiveSurfaceScript;
            return interactiveSurface != null &&
                   interactiveSurface.TryHandleTooltipActivationClick(rightClick, out tooltipParent);
        }

        public static bool HoldingClick => MyAPIGateway.Input.IsLeftMousePressed() ||
                                           MyAPIGateway.Input.IsJoystickButtonPressed(
                                               button: MyJoystickButtonsEnum.J06);

        public static bool HoldingRightClick => MyAPIGateway.Input.IsRightMousePressed();

        bool? GetActiveClickButton()
        {
            if (MyAPIGateway.Input == null)
                return null;

            if (HoldingClick || _primaryWasPressed)
                return false;

            if (HoldingRightClick || _secondaryWasPressed)
                return true;

            return null;
        }

        static bool TryGetHoveredClickable(IEyeTracking screen, bool? secondary, out ControlBase clickable)
        {
            clickable = null;

            var interactiveSurface = screen as InteractiveSurfaceScript;
            if (interactiveSurface != null)
            {
                return secondary.HasValue
                    ? interactiveSurface.TryResolveClickableAtCursor(secondary.Value, out clickable)
                    : interactiveSurface.TryResolveClickableAtCursor(out clickable);
            }

            var entries = screen.InteractiveEntries;
            if (entries == null || entries.Count == 0)
                return false;

            var position = screen.CursorPosition + screen.HitTestOffset;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y))
                return false;

            var list = entries as IList<ControlBase>;
            if (list == null)
                return false;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var entry = list[i];
                if (entry == null)
                    continue;

                bool resolved = secondary.HasValue
                    ? secondary.Value
                        ? entry.TryResolveSecondaryClickable(position, out clickable)
                        : entry.TryResolvePrimaryClickable(position, out clickable)
                    : entry.TryResolveClickable(position, out clickable);

                if (resolved)
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
