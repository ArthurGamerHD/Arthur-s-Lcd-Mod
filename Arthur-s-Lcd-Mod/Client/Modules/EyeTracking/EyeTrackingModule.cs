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
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Modules.EyeTracking
{
    public class EyeTrackingModule : IModule<IEyeTracking>
    {
        public static event Action<InteractiveSurfaceScript, ControlTemplate> OnControlPointerEnter;
        public static event Action<InteractiveSurfaceScript, ControlTemplate> OnControlPointerLeave;
        public static event Action<InteractiveSurfaceScript, ControlTemplate, bool> OnControlClick;

        const double MAX_TRACKING_DISTANCE_METERS = 20d;
        const double MAX_TRACKING_DISTANCE_SQ = MAX_TRACKING_DISTANCE_METERS * MAX_TRACKING_DISTANCE_METERS;
        const long ACTIVE_SURFACE_TIMEOUT_FRAMES = 30;
        const float MIN_DRAG_DELTA_PIXELS = 0.001f;
        const float PRESERVED_CLICK_DRAG_THRESHOLD_PIXELS = 3f;

        readonly HashSet<IEyeTracking> _modules = new HashSet<IEyeTracking>();
        readonly List<IEyeTracking> _pendingModules = new List<IEyeTracking>();

        int _lastActiveNearbyCount;
        ControlTemplate _pressedClickable;
        object _pressedClickableDataContext;
        InteractiveSurfaceScript _pointerSurface;
        ControlTemplate _pointerControl;
        ControlTemplate _draggingControl;
        IEyeTracking _draggingEntity;
        Vector2 _lastDragPosition;
        Vector2 _pendingDragDelta;
        bool _draggingSecondary;
        bool _draggingMoved;
        bool _draggingPreservesPrimaryClick;
        bool _suppressPrimaryReleaseClick;
        bool _suppressSecondaryReleaseClick;
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

            if (ReferenceEquals(_pointerSurface, instance))
                UpdatePointerTarget(null, null);

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
                ((entity?.BlockDefinition as MyCockpitDefinition)?.EnableShipControl ?? false)))
            {
                UpdatePointerTarget(null, null);
                UpdateClickState(null, null, null);
                _lastActiveNearbyCount = 0;
                return;
            }

            if (LocalPlayerBlockStateHelper.IsBlockPlacerActive())
            {
                UpdatePointerTarget(null, null);
                UpdateClickState(null, null, null);
                _lastActiveNearbyCount = 0;
                return;
            }

            Vector3D cameraPos;
            Vector3D cameraForward;

            if (!TryGetCameraRay(out cameraPos, out cameraForward))
            {
                UpdatePointerTarget(null, null);
                UpdateClickState(null, null, null);
                _lastActiveNearbyCount = 0;
                return;
            }

            var resolvedCount = 0;
            ControlTemplate hoveredClickable = null;
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

                    ControlTemplate clickable;

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

            var pointerSurface = lookingScreen as InteractiveSurfaceScript;
            ControlTemplate pointerControl = null;
            if (pointerSurface != null)
            {
                try
                {
                    pointerSurface.TryResolveHitAtCursor(out pointerControl);
                }
                catch (Exception e)
                {
                    pointerSurface.OnException(e);
                }
            }

            UpdatePointerTarget(pointerSurface, pointerControl);

            try
            {
                UpdateScrollState(lookingScreen);
                UpdateClickState(hoveredClickable, eyeTrackingEntity, lookingScreen);
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
            ControlTemplate hoveredClickable,
            IEyeTracking eyeTrackingEntity,
            IEyeTracking lookingScreen)
        {
            bool primaryPressed = MyAPIGateway.Input != null && HoldingClick;
            bool secondaryPressed = MyAPIGateway.Input != null && HoldingRightClick;
            bool primaryStarted = primaryPressed && !_primaryWasPressed;
            bool secondaryStarted = secondaryPressed && !_secondaryWasPressed;
            bool primaryReleased = !primaryPressed && _primaryWasPressed;
            bool secondaryReleased = !secondaryPressed && _secondaryWasPressed;
            bool hasInputTarget = hoveredClickable != null || eyeTrackingEntity != null || lookingScreen != null || _draggingControl != null;
            bool finishedDrag = false;

            if (primaryStarted)
            {
                if (!TryBeginDrag(lookingScreen ?? eyeTrackingEntity, false))
                {
                    if (hoveredClickable != null && hoveredClickable.ClickOnPress)
                    {
                        HandleClickOnPress(hoveredClickable, eyeTrackingEntity);
                        _suppressPrimaryReleaseClick = true;
                        SetPressedClickable(hoveredClickable);
                    }
                    else
                    {
                        SetPressedClickable(hoveredClickable);
                    }
                }
                else
                {
                    if (_draggingPreservesPrimaryClick)
                    {
                        SetPressedClickable(hoveredClickable);
                    }
                    else
                    {
                        _suppressPrimaryReleaseClick = true;
                        SetPressedClickable(null);
                    }
                }
            }

            if (secondaryStarted)
            {
                if (!TryBeginDrag(lookingScreen ?? eyeTrackingEntity, true))
                {
                    SetPressedClickable(hoveredClickable);
                }
                else
                {
                    _suppressSecondaryReleaseClick = true;
                    SetPressedClickable(null);
                }
            }

            bool activeDragPressed = _draggingSecondary ? secondaryPressed : primaryPressed;
            if (activeDragPressed && _draggingControl != null)
            {
                if (!ReferenceEquals(lookingScreen, _draggingEntity))
                {
                    bool suppressClick = !_draggingPreservesPrimaryClick || _draggingMoved;
                    EndActiveDrag();
                    finishedDrag = suppressClick;
                }
                else
                {
                    Vector2 currentPosition;

                    if (!TryGetHitTestCursorPosition(_draggingEntity, out currentPosition))
                    {
                        bool suppressClick = !_draggingPreservesPrimaryClick || _draggingMoved;
                        EndActiveDrag();
                        finishedDrag = suppressClick;
                    }
                    else
                    {
                        var delta = currentPosition - _lastDragPosition;
                        _lastDragPosition = currentPosition;

                        if (!IsValidVector(delta))
                        {
                            bool suppressClick = !_draggingPreservesPrimaryClick || _draggingMoved;
                            EndActiveDrag();
                            finishedDrag = suppressClick;
                        }
                        else
                        {
                            _pendingDragDelta += delta;
                            float dragThreshold = _draggingPreservesPrimaryClick && !_draggingMoved
                                ? PRESERVED_CLICK_DRAG_THRESHOLD_PIXELS
                                : MIN_DRAG_DELTA_PIXELS;
                            bool exceededThreshold =
                                Math.Abs(_pendingDragDelta.X) > dragThreshold ||
                                Math.Abs(_pendingDragDelta.Y) > dragThreshold;

                            if (exceededThreshold)
                            {
                                Vector2 dragDelta = _pendingDragDelta;
                                _pendingDragDelta = Vector2.Zero;

                                if (_draggingControl.Drag(_draggingEntity, dragDelta, _draggingSecondary))
                                {
                                    _draggingMoved = true;

                                    if (_draggingPreservesPrimaryClick)
                                    {
                                        _suppressPrimaryReleaseClick = true;
                                        SetPressedClickable(null);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            bool activeDragReleased = _draggingControl != null &&
                                      (_draggingSecondary ? secondaryReleased : primaryReleased);
            if (activeDragReleased)
            {
                bool suppressClick = !_draggingPreservesPrimaryClick || _draggingMoved;
                EndActiveDrag();
                finishedDrag = suppressClick;
            }

            if (primaryReleased || secondaryReleased)
            {
                bool suppressReleaseClick = finishedDrag ||
                                            primaryReleased && _suppressPrimaryReleaseClick ||
                                            secondaryReleased && _suppressSecondaryReleaseClick;
                var hoveredDataContext = hoveredClickable != null ? hoveredClickable.DataContext ?? hoveredClickable : null;

                try
                {
                    var interactiveSurface = eyeTrackingEntity as InteractiveSurfaceScript;
                    var rightClick = !_primaryWasPressed && _secondaryWasPressed;

                    ControlTemplate clickedControl = null;
                    var click = false;
                    if (!suppressReleaseClick &&
                        _pressedClickable != null &&
                        hoveredClickable != null &&
                        Equals(objA: _pressedClickableDataContext, objB: hoveredDataContext))
                    {
                        if (interactiveSurface != null)
                        {
                            click = interactiveSurface.TryClickAtCursor(
                                rightClick,
                                eyeTrackingEntity,
                                out clickedControl);
                        }
                        else
                        {
                            clickedControl = hoveredClickable;
                            click = rightClick
                                ? hoveredClickable.SecondaryClick(eyeTrackingEntity)
                                : hoveredClickable.Click(eyeTrackingEntity);
                        }
                    }

                    ControlTemplate tooltipParent;
                    if (!suppressReleaseClick && !click && TryHandleTooltipActivation(
                            eyeTrackingEntity,
                            rightClick: rightClick,
                            tooltipParent: out tooltipParent))
                    {
                        click = true;
                        clickedControl = tooltipParent;
                    }

                    if (click && interactiveSurface != null && clickedControl != null)
                        RaiseControlClick(interactiveSurface, clickedControl, rightClick);

                    if (eyeTrackingEntity != null && !suppressReleaseClick)
                    {
                        eyeTrackingEntity.PlaySounds(
                            hoveredClickable == null
                                ? AudioHelper.HudClick
                                : click
                                    ? hoveredClickable.ClickSound
                                    : hoveredClickable.ClickFailSound);
                    }
                }
                catch (Exception e)
                {
                    if (eyeTrackingEntity != null && !suppressReleaseClick)
                    {
                        eyeTrackingEntity.PlaySounds(
                            hoveredClickable != null
                                ? hoveredClickable.ClickFailSound
                                : AudioHelper.HudClick);
                    }

                    ErrorHandlerHelper.LogError(error: e, source: this);
                }

                SetPressedClickable(null);

                if (primaryReleased)
                    _suppressPrimaryReleaseClick = false;

                if (secondaryReleased)
                    _suppressSecondaryReleaseClick = false;
            }

            if (!hasInputTarget)
            {
                SetPressedClickable(null);

                if (_draggingControl != null)
                    EndActiveDrag();

                if (!primaryPressed)
                    _suppressPrimaryReleaseClick = false;

                if (!secondaryPressed)
                    _suppressSecondaryReleaseClick = false;
            }

            _primaryWasPressed = primaryPressed;
            _secondaryWasPressed = secondaryPressed;
        }

        void UpdatePointerTarget(InteractiveSurfaceScript surface, ControlTemplate control)
        {
            if (ReferenceEquals(_pointerSurface, surface) && ReferenceEquals(_pointerControl, control))
                return;

            if (_pointerSurface != null && _pointerControl != null)
                RaiseControlPointerEvent(OnControlPointerLeave, _pointerSurface, _pointerControl);

            _pointerSurface = surface;
            _pointerControl = control;

            if (_pointerSurface != null && _pointerControl != null)
                RaiseControlPointerEvent(OnControlPointerEnter, _pointerSurface, _pointerControl);
        }

        static void RaiseControlPointerEvent(
            Action<InteractiveSurfaceScript, ControlTemplate> handlers,
            InteractiveSurfaceScript surface,
            ControlTemplate control)
        {
            if (handlers == null || surface == null || control == null)
                return;

            foreach (var @delegate in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<InteractiveSurfaceScript, ControlTemplate>)@delegate)(surface, control);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, surface);
                }
            }
        }

        static void RaiseControlClick(
            InteractiveSurfaceScript surface,
            ControlTemplate control,
            bool secondary)
        {
            if (surface == null || control == null)
                return;

            var handlers = OnControlClick;
            if (handlers == null)
                return;

            foreach (var @delegate in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<InteractiveSurfaceScript, ControlTemplate, bool>)@delegate)(
                        surface,
                        control,
                        secondary);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, surface);
                }
            }
        }

        void SetPressedClickable(ControlTemplate control)
        {
            if (ReferenceEquals(_pressedClickable, control))
                return;

            if (_pressedClickable != null)
                _pressedClickable.SetPressed(false);

            _pressedClickable = control;
            _pressedClickableDataContext = control != null
                ? control.DataContext ?? control
                : null;

            if (_pressedClickable != null)
                _pressedClickable.SetPressed(true);
        }

        static void UpdateScrollState(IEyeTracking lookingScreen)
        {
            if (lookingScreen == null || MyAPIGateway.Input == null)
                return;

            var delta = MyAPIGateway.Input.DeltaMouseScrollWheelValue();

            if (delta != 0)
                lookingScreen.MouseScroll(delta);
        }

        bool TryBeginDrag(IEyeTracking screen, bool secondary)
        {
            if (_draggingControl != null)
                return false;

            ControlTemplate draggable;
            Vector2 position;

            if (!TryGetHoveredDraggable(screen, secondary, out draggable) ||
                !TryGetHitTestCursorPosition(screen, out position))
                return false;

            if (!draggable.BeginDrag(screen, secondary))
                return false;

            _draggingControl = draggable;
            _draggingEntity = screen;
            _lastDragPosition = position;
            _pendingDragDelta = Vector2.Zero;
            _draggingSecondary = secondary;
            _draggingMoved = false;
            _draggingPreservesPrimaryClick = !secondary && draggable.PreservePrimaryClickUntilDragged;
            return true;
        }

        void EndActiveDrag()
        {
            if (_draggingControl != null)
                _draggingControl.EndDrag(_draggingEntity);

            _draggingControl = null;
            _draggingEntity = null;
            _lastDragPosition = default(Vector2);
            _pendingDragDelta = Vector2.Zero;
            _draggingSecondary = false;
            _draggingMoved = false;
            _draggingPreservesPrimaryClick = false;
        }

        void HandleClickOnPress(ControlTemplate clickedControl, IEyeTracking eyeTrackingEntity)
        {
            if (clickedControl == null)
                return;

            bool click = false;

            try
            {
                Vector2 position;
                click = TryGetHitTestCursorPosition(eyeTrackingEntity, out position) &&
                        clickedControl.ClickAt(position, eyeTrackingEntity);

                var interactiveSurface = eyeTrackingEntity as InteractiveSurfaceScript;
                if (click && interactiveSurface != null)
                    RaiseControlClick(interactiveSurface, clickedControl, false);

                if (eyeTrackingEntity != null)
                {
                    eyeTrackingEntity.PlaySounds(
                        click
                            ? clickedControl.ClickSound
                            : clickedControl.ClickFailSound);
                }
            }
            catch (Exception e)
            {
                if (eyeTrackingEntity != null)
                    eyeTrackingEntity.PlaySounds(clickedControl.ClickFailSound);

                ErrorHandlerHelper.LogError(error: e, source: this);
            }
        }

        static bool TryHandleTooltipActivation(
            IEyeTracking eyeTrackingEntity,
            bool rightClick,
            out ControlTemplate tooltipParent)
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

        static bool TryGetHoveredClickable(IEyeTracking screen, bool? secondary, out ControlTemplate clickable)
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

            Vector2 position;
            if (!TryGetHitTestCursorPosition(screen, out position))
                return false;

            var list = entries as IList<Control>;

            if (list == null)
                return false;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var entry = list[i] as ControlTemplate;

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

        static bool TryGetHoveredDraggable(
            IEyeTracking screen,
            bool secondary,
            out ControlTemplate draggable)
        {
            draggable = null;

            if (screen == null)
                return false;

            var entries = screen.InteractiveEntries;

            if (entries == null || entries.Count == 0)
                return false;

            Vector2 position;
            if (!TryGetHitTestCursorPosition(screen, out position))
                return false;

            var list = entries as IList<Control>;

            if (list == null)
                return false;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var entry = list[i] as ControlTemplate;

                if (entry != null && entry.TryResolveDraggable(position, secondary, out draggable))
                    return true;
            }

            return false;
        }

        static bool TryGetHitTestCursorPosition(IEyeTracking screen, out Vector2 position)
        {
            position = default(Vector2);

            if (screen == null)
                return false;

            position = screen.CursorPosition + screen.HitTestOffset;
            return IsValidVector(position);
        }

        static bool IsValidVector(Vector2 value)
        {
            return !float.IsNaN(value.X) && !float.IsNaN(value.Y);
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
