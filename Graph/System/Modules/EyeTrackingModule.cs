using System;
using System.Collections.Generic;
using Generated;
using Graph.Apps.Utility;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;


namespace Graph.System.Modules
{
    public class EyeTrackingModule : IModule<IEyeTracking>
    {
        const double MAX_TRACKING_DISTANCE_METERS = 20d;
        const double MAX_TRACKING_DISTANCE_SQ = MAX_TRACKING_DISTANCE_METERS * MAX_TRACKING_DISTANCE_METERS;

        HashSet<IEyeTracking> modules = new HashSet<IEyeTracking>();
        readonly Dictionary<IEyeTracking, ScreenDummyCache> _dummyCache =
            new Dictionary<IEyeTracking, ScreenDummyCache>();
        int _lastActiveNearbyCount;
        int _lastNearbyCount;
        
        public void Hook(IEyeTracking instance)
        {
            modules.Add(instance);
            _dummyCache.Remove(instance);
        }

        public void Unhook(IEyeTracking instance)
        {
            modules.Remove(instance);
            _dummyCache.Remove(instance);
        }

        public int Count => modules.Count;
        public int ActiveCount => _lastActiveNearbyCount;

        public void Update()
        {
            Vector3D cameraPos;
            Vector3D cameraForward;
            if (!TryGetCameraRay(out cameraPos, out cameraForward))
            {
                _lastActiveNearbyCount = 0;
                return;
            }

            var nearbyCount = 0;
            var resolvedCount = 0;
            foreach (var screen in modules)
            {
                if (screen == null || screen.Block == null)
                    continue;

                var blockPos = screen.Block.WorldMatrix.Translation;
                if (Vector3D.DistanceSquared(blockPos, cameraPos) > MAX_TRACKING_DISTANCE_SQ)
                    continue;
                nearbyCount++;

                ScreenDummyCache dummy;
                if (!TryGetScreenDummy(screen, out dummy))
                    continue;
                resolvedCount++;

                MatrixD dummyWorldMatrix = MatrixD.Multiply((MatrixD)dummy.LocalMatrix, screen.Block.WorldMatrix);
                Vector2 lookAtCoordinates;
                if (TryProjectCameraCenter(
                        dummyWorldMatrix,
                        screen.Surface,
                        screen.RotationOrSurfaceIndex,
                        cameraPos,
                        cameraForward,
                        out lookAtCoordinates))
                {
                    screen.LookAt(lookAtCoordinates);
                }

            }
            _lastNearbyCount = nearbyCount;
            _lastActiveNearbyCount = resolvedCount;
        } 

        public void PostUpdate()
        {
            
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

        static bool TryProjectCameraCenter(
            MatrixD dummyWorldMatrix,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            int rotationOrSurfaceIndex,
            Vector3D rayOrigin,
            Vector3D rayDirection,
            out Vector2 lookAtCoordinates)
        {
            lookAtCoordinates = Vector2.Zero;
            if (surface == null)
                return false;

            var planeNormal = dummyWorldMatrix.Forward;
            var denom = Vector3D.Dot(rayDirection, planeNormal);
            if (Math.Abs(denom) < 1e-6)
                return false;

            var distance = Vector3D.Dot(dummyWorldMatrix.Translation - rayOrigin, planeNormal) / denom;
            if (distance <= 0d)
                return false;

            var hitPoint = rayOrigin + rayDirection * distance;
            var dummyWorldInverse = MatrixD.Invert(dummyWorldMatrix);
            var hitLocal = Vector3D.Transform(hitPoint, dummyWorldInverse);

            var localX = hitLocal.X;
            var localY = hitLocal.Y;
            RotateLocalQuarterTurns(ref localX, ref localY, rotationOrSurfaceIndex);

            const double localHalfExtent = 0.5d;
            if (localX < -localHalfExtent || localX > localHalfExtent ||
                localY < -localHalfExtent || localY > localHalfExtent)
                return false;

            var u = (localX + localHalfExtent) / (localHalfExtent * 2d);
            var v = (localHalfExtent - localY) / (localHalfExtent * 2d);
            if (u < 0d || u > 1d || v < 0d || v > 1d)
                return false;

            var offset = (surface.TextureSize - surface.SurfaceSize) * 0.5f;
            lookAtCoordinates = new Vector2(
                offset.X + (float)(u * surface.SurfaceSize.X),
                offset.Y + (float)(v * surface.SurfaceSize.Y));
            return true;
        }

        static void RotateLocalQuarterTurns(ref double x, ref double y, int quarterTurns)
        {
            var turns = ((quarterTurns % 4) + 4) % 4;
            if (turns == 0)
                return;

            var ox = x;
            var oy = y;

            // Rotate hit coordinates with the LCD quarter-turn so UV/bounds align with the rotated chart orientation.
            switch (turns)
            {
                case 1:
                    x = -oy;
                    y = ox;
                    break;
                case 2:
                    x = -ox;
                    y = -oy;
                    break;
                default:
                    x = oy;
                    y = -ox;
                    break;
            }
        }

        static Vector2 GetSurfaceCenterRaw(Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            var offset = (surface.TextureSize - surface.SurfaceSize) * 0.5f;
            return new Vector2(
                offset.X + surface.SurfaceSize.X * 0.5f,
                offset.Y + surface.SurfaceSize.Y * 0.5f);
        }

        static void DrawDummyWorldDebug(MatrixD dummyWorldMatrix, float localHalfExtent)
        {
            if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated)
                return;

            var right = (Vector3D)dummyWorldMatrix.Right;
            var up = (Vector3D)dummyWorldMatrix.Up;
            var forward = (Vector3D)dummyWorldMatrix.Forward;

            double rightScale = Math.Max(1e-4, right.Length());
            double upScale = Math.Max(1e-4, up.Length());
            double forwardScale = Math.Max(1e-4, forward.Length());

            right /= rightScale;
            up /= upScale;
            forward /= forwardScale;

            var drawMatrix = MatrixD.Identity;
            drawMatrix.Right = right;
            drawMatrix.Up = up;
            drawMatrix.Forward = forward;
            drawMatrix.Translation = dummyWorldMatrix.Translation;

            double halfX = Math.Max(0.02d, localHalfExtent * rightScale * 1.05d);
            double halfY = Math.Max(0.02d, localHalfExtent * upScale * 1.05d);
            double halfZ = Math.Max(0.01d, localHalfExtent * forwardScale * 1.05d);
            var box = new BoundingBoxD(
                new Vector3D(-halfX, -halfY, -halfZ),
                new Vector3D(halfX, halfY, halfZ));
            var solidColor = new Color(0, 220, 255, 36);
            MySimpleObjectDraw.DrawTransparentBox(
                ref drawMatrix,
                ref box,
                ref solidColor,
                MySimpleObjectRasterizer.Solid,
                1);

            var wireColor = new Color(0, 220, 255, 255);
            MySimpleObjectDraw.DrawTransparentBox(
                ref drawMatrix,
                ref box,
                ref wireColor,
                MySimpleObjectRasterizer.Wireframe,
                1,
                0.02f);
        }

        static void DrawMissingDummyDebug(VRage.Game.ModAPI.Ingame.IMyCubeBlock block)
        {
            if (block == null)
                return;
            if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated)
                return;

            var matrix = block.WorldMatrix;
            var half = Math.Max(0.25d, block.CubeGrid != null ? block.CubeGrid.GridSize * 0.6d : 0.6d);
            var localBox = new BoundingBoxD(
                new Vector3D(-half, -half, -half),
                new Vector3D(half, half, half));
            var color = new Color(255, 40, 40, 255);
            MySimpleObjectDraw.DrawTransparentBox(
                ref matrix,
                ref localBox,
                ref color,
                MySimpleObjectRasterizer.Wireframe,
                1,
                0.03f);
        }

        bool TryGetScreenDummy(IEyeTracking screen, out ScreenDummyCache cache)
        {
            cache = null;
            if (screen == null || screen.Block == null || screen.Surface == null)
                return false;

            var blockEntity = screen.Block as VRage.ModAPI.IMyEntity;
            if (blockEntity == null || blockEntity.Model == null)
                return false;

            int surfaceIndex;
            var hasSurfaceIndex = TryResolveSurfaceIndex(screen, out surfaceIndex);

            var model = blockEntity.Model;
            var modelId = model.UniqueId;
            ScreenDummyCache cached;
            if (_dummyCache.TryGetValue(screen, out cached) &&
                cached != null &&
                cached.BlockEntityId == blockEntity.EntityId &&
                cached.SurfaceIndex == surfaceIndex &&
                cached.ModelUniqueId == modelId)
            {
                cache = cached;
                return true;
            }

            var dummies = new Dictionary<string, IMyModelDummy>(StringComparer.OrdinalIgnoreCase);
            model.GetDummies(dummies);

            IMyModelDummy dummy;
            string dummyName;
            if (!TrySelectScreenDummy(dummies, surfaceIndex, hasSurfaceIndex, out dummyName, out dummy))
                return false;

            var resolved = new ScreenDummyCache
            {
                BlockEntityId = blockEntity.EntityId,
                SurfaceIndex = surfaceIndex,
                ModelUniqueId = modelId,
                DummyName = dummyName,
                LocalMatrix = dummy.Matrix
            };

            _dummyCache[screen] = resolved;
            cache = resolved;
            return true;
        }

        static bool TryResolveSurfaceIndex(IEyeTracking screen, out int index)
        {
            index = -1;
            var providerModApi = screen.Block as IMyTextSurfaceProvider;
            if (providerModApi != null)
            {
                for (int i = 0; i < providerModApi.SurfaceCount; i++)
                {
                    var providerSurface = providerModApi.GetSurface(i);
                    if (ReferenceEquals(providerSurface, screen.Surface) ||
                        (providerSurface != null && providerSurface.Equals(screen.Surface)))
                    {
                        index = i;
                        return true;
                    }
                }

                if (providerModApi.SurfaceCount == 1)
                {
                    index = 0;
                    return true;
                }
            }

            var providerIngame = screen.Block as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
            if (providerIngame == null)
                return false;

            for (int i = 0; i < providerIngame.SurfaceCount; i++)
            {
                var providerSurface = providerIngame.GetSurface(i);
                if (ReferenceEquals(providerSurface, screen.Surface) ||
                    (providerSurface != null && providerSurface.Equals(screen.Surface)))
                {
                    index = i;
                    return true;
                }
            }

            if (providerIngame.SurfaceCount == 1)
            {
                index = 0;
                return true;
            }

            return false;
        }

        static bool TrySelectScreenDummy(
            Dictionary<string, IMyModelDummy> dummies,
            int surfaceIndex,
            bool hasSurfaceIndex,
            out string selectedName,
            out IMyModelDummy selectedDummy)
        {
            selectedName = null;
            selectedDummy = null;
            if (dummies == null || dummies.Count == 0)
                return false;

            if (hasSurfaceIndex)
            {
                var exactCandidates = new[]
                {
                    "screen_" + surfaceIndex,
                    "screen_" + (surfaceIndex + 1),
                    "screenarea_" + surfaceIndex,
                    "screenarea_" + (surfaceIndex + 1),
                    "screen_area_" + surfaceIndex,
                    "screen_area_" + (surfaceIndex + 1),
                    "lcd_" + surfaceIndex,
                    "lcd_" + (surfaceIndex + 1),
                    "surface_" + surfaceIndex,
                    "surface_" + (surfaceIndex + 1),
                    "screenarea" + surfaceIndex,
                    "screenarea" + (surfaceIndex + 1),
                    "screenarea" + (surfaceIndex * 90),
                    "detector_textpanel",
                    "detector_lcd",
                    "detector_screen",
                    "detector_surface"
                };

                for (int i = 0; i < exactCandidates.Length; i++)
                {
                    IMyModelDummy exact;
                    if (!dummies.TryGetValue(exactCandidates[i], out exact))
                        continue;

                    selectedName = exactCandidates[i];
                    selectedDummy = exact;
                    return true;
                }
            }

            var fallbackNames = new List<string>();
            foreach (var pair in dummies)
            {
                var key = pair.Key;
                if (string.IsNullOrEmpty(key))
                    continue;

                if (key.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    key.IndexOf("lcd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    key.IndexOf("surface", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    key.IndexOf("detector", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    key.IndexOf("panel", StringComparison.OrdinalIgnoreCase) >= 0)
                    fallbackNames.Add(key);
            }

            if (fallbackNames.Count == 0)
            {
                if (dummies.Count == 1)
                {
                    foreach (var pair in dummies)
                    {
                        selectedName = pair.Key;
                        selectedDummy = pair.Value;
                        return selectedDummy != null;
                    }
                }

                return false;
            }

            fallbackNames.Sort(StringComparer.OrdinalIgnoreCase);
            var selectedIndex = hasSurfaceIndex && surfaceIndex < fallbackNames.Count ? surfaceIndex : 0;
            selectedName = fallbackNames[selectedIndex];
            selectedDummy = dummies[selectedName];
            return true;
        }

        class ScreenDummyCache
        {
            public long BlockEntityId;
            public int SurfaceIndex;
            public int ModelUniqueId;
            public string DummyName;
            public Matrix LocalMatrix;
        }
    }
}
