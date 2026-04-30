using System;
using System.Collections.Generic;
using Generated;
using Graph.Apps.Abstract;
using Graph.Apps.Utility;
using Graph.System.ScreenAreas;
using Sandbox.ModAPI;
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
    }
}
