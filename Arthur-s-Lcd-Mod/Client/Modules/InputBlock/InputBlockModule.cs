using System.Collections.Generic;
using Generated;
using LcdMod.Client.Helpers;
using LcdMod.Client.ScreenAreas;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Utility;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.Modules.InputBlock
{
    public class InputBlockModule : IModule<IInputBlock>
    {
        const double MAX_TRACKING_DISTANCE_METERS = 20d;
        const double MAX_TRACKING_DISTANCE_SQ = MAX_TRACKING_DISTANCE_METERS * MAX_TRACKING_DISTANCE_METERS;
        const long ACTIVE_SURFACE_TIMEOUT_FRAMES = 30;

        public static Color OriginalHighlightColor = Color.Transparent;
        
        readonly HashSet<IInputBlock> _modules = new HashSet<IInputBlock>();
        readonly List<IInputBlock> _pendingModules = new List<IInputBlock>();
        int _lastActiveNearbyCount;
        bool _useInputBlocked;

        readonly IMyControl _moveCameraControl =
            MyAPIGateway.Input.GetGameControl(MyStringId.GetOrCompute("LOOKAROUND"));

        public void Hook(IInputBlock instance)
        {
            if (instance != null)
                _pendingModules.Add(instance);
            else
                LogHelper.Log(MyLogSeverity.Warning, $"{nameof(InputBlockModule)} tried to register an null instance");
        }

        public void Unhook(IInputBlock instance)
        {
            if (instance == null)
                return;

            _modules.Remove(instance);
            _pendingModules.Remove(instance);

            if (_modules.Count == 0 && _pendingModules.Count == 0)
                SetInputBlocked(false);
        }

        public int Count => _modules.Count;
        public int ActiveCount => _lastActiveNearbyCount;

        public void Update()
        {
            foreach (var module in _pendingModules)
                _modules.Add(module);

            _pendingModules.Clear();

            int activeCount;
            bool shouldBlock = ShouldBlockInput(out activeCount);
            _lastActiveNearbyCount = activeCount;
            SetInputBlocked(shouldBlock);
        }

        public void PostUpdate()
        {
        }

        bool ShouldBlockInput(out int activeCount)
        {
            activeCount = 0;

            var player = MyAPIGateway.Session?.LocalHumanPlayer;
            var entity = player?.Controller?.ControlledEntity?.Entity as MyCubeBlock;

            if (LocalPlayerBlockStateHelper.IsBlockPlacerActive())
                return false;

            if (MyAPIGateway.Gui.IsCursorVisible || (!_moveCameraControl.IsPressed() &&
                                                     ((entity?.BlockDefinition as MyCockpitDefinition)
                                                         ?.EnableShipControl ?? false)))
                return false;

            Vector3D cameraPos;
            Vector3D cameraForward;
            if (!TryGetCameraRay(out cameraPos, out cameraForward))
                return false;

            foreach (var screen in _modules)
            {
                var surfaceScript = screen as SurfaceScriptBase;
                if (surfaceScript == null || screen.Block == null)
                    continue;

                if (!HasRecentlyRun(screen))
                    continue;

                var blockPos = screen.Block.WorldMatrix.Translation;
                if (Vector3D.DistanceSquared(blockPos, cameraPos) > MAX_TRACKING_DISTANCE_SQ)
                    continue;

                Vector2 lookAtCoordinates;
                if (!ScreenAreaGeometry.TryGetScreenPointIntersection(
                        surfaceScript,
                        cameraPos,
                        cameraForward,
                        out lookAtCoordinates))
                    continue;

                activeCount++;
                return true;
            }

            return false;
        }

        void SetInputBlocked(bool blocked)
        {
            if (_useInputBlocked == blocked)
                return;

            if (MyAPIGateway.Session?.LocalHumanPlayer == null)
                return;

            LcdModClientComponent.SetLocalPlayerUseInputBlocked(blocked);
            _useInputBlocked = blocked;

            if (blocked)
            {
                if (MyDefinitionManager.Static.EnvironmentDefinition.ContourHighlightColor != Color.Transparent)
                    OriginalHighlightColor = MyDefinitionManager.Static.EnvironmentDefinition.ContourHighlightColor;
                
                MyDefinitionManager.Static.EnvironmentDefinition.ContourHighlightColor = Color.Transparent;
            }
            else
            {
                MyDefinitionManager.Static.EnvironmentDefinition.ContourHighlightColor = OriginalHighlightColor;
            }
        }

        static bool HasRecentlyRun(IInputBlock screen)
        {
            if (screen == null || MyAPIGateway.Session == null)
                return false;

            return screen.LastRunTick != long.MinValue &&
                   MyAPIGateway.Session.GameplayFrameCounter - screen.LastRunTick <=
                   ACTIVE_SURFACE_TIMEOUT_FRAMES;
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
