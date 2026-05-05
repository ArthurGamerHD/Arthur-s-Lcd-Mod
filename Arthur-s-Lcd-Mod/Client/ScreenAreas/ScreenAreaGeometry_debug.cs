using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using VRageRender;

namespace LcdMod.Client.ScreenAreas
{
    public static partial class ScreenAreaGeometry
    {
#if DEBUG
        const double DEBUG_RAY_MAX_DISTANCE = 20d;
        static List<IMyHudNotification> _debugNotifications = new List<IMyHudNotification>();
        static int _drawTick;

        public static void DebugDraw()
        {
            if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated)
                return;

            if (_drawTick == 0)
                Cache.Clear();

            _drawTick++;
            if (_drawTick % 300 == 1)
                LogHelper.LogOnce("scan", "active screen scripts=" + SurfaceScriptBase.Instances.Count);

            Vector3D cameraPosition;
            Vector3D cameraForward;
            var hasCameraRay = TryGetCameraRay(out cameraPosition, out cameraForward);

            DebugHitInfo bestHit = null;
            foreach (var screen in SurfaceScriptBase.Instances)
                DebugDraw(screen, hasCameraRay, cameraPosition, cameraForward, ref bestHit);

            DrawDebugText(bestHit);
        }


        static void DebugDraw(
            SurfaceScriptBase screen,
            bool hasCameraRay,
            Vector3D cameraPosition,
            Vector3D cameraForward,
            ref DebugHitInfo bestHit)
        {
            if (screen == null || screen.Block == null || screen.Surface == null)
            {
                LogHelper.LogOnce("skip:null-screen", "skipping null screen/block/surface");
                return;
            }

            MinimalMwmScreenAreaGeometry geometry;
            if (!TryGetScreenAreaGeometry(screen, out geometry))
                return;

            var uv = Vector2.Zero;
            var rawUv = Vector2.Zero;
            var hitDistance = 0d;
            var intersects = hasCameraRay &&
                             TryGetScreenIntersection(
                                 screen.Block.Model,
                                 screen.Block.WorldMatrix,
                                 geometry,
                                 cameraPosition,
                                 cameraForward,
                                 DEBUG_RAY_MAX_DISTANCE,
                                 out uv,
                                 out rawUv,
                                 out hitDistance);
            var color = intersects ? new Vector4(0f, 1f, 0.15f, 1f) : new Vector4(1f, 0.05f, 0f, 1f);
            DebugDrawScreenGeometry(screen.Block.WorldMatrix, geometry, screen.Block.Model, ref color);
            DebugDrawMappedBounds(screen.Block.WorldMatrix, geometry, screen.Block.Model);

            if (intersects)
            {
                var surfacePoint = ToSurfacePoint(screen.Surface, uv);
                if (bestHit == null || hitDistance < bestHit.Distance)
                {
                    bestHit = new DebugHitInfo
                    {
                        Screen = screen,
                        Uv = uv,
                        RawUv = rawUv,
                        SurfacePoint = surfacePoint,
                        Distance = hitDistance
                    };
                }
            }

            LogHelper.LogOnce(
                "draw:" + screen.Block.EntityId + ":" + screen.RotationOrSurfaceIndex + ":" + geometry.Material,
                "drawing " + screen.Description() + ", material=" + geometry.Material +
                ", triangles=" + geometry.TriangleCount +
                ", areaM2=" + geometry.AreaM2 +
                ", worldPos=" + screen.Block.WorldMatrix.Translation);
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

        static void DrawDebugText(DebugHitInfo hit)
        {
            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
                return;

            if (_debugNotifications.Count == 0)
            {
                _debugNotifications.Add(utilities.CreateNotification("ScreenArea debug 0", 100, MyFontEnum.Green));
                _debugNotifications.Add(utilities.CreateNotification("ScreenArea debug 1", 100, MyFontEnum.Green));
                _debugNotifications.Add(utilities.CreateNotification("ScreenArea debug 2", 100, MyFontEnum.Green));
                _debugNotifications.Add(utilities.CreateNotification("ScreenArea debug 3", 100, MyFontEnum.Green));
                _debugNotifications.Add(utilities.CreateNotification("ScreenArea debug 4", 100, MyFontEnum.Green));
                _debugNotifications.Add(utilities.CreateNotification("ScreenArea debug 5", 100, MyFontEnum.Green));
            }

            foreach (var notification in _debugNotifications)
            {
                notification.Hide();
            }


            if (hit != null)
            {
                _debugNotifications[0].Text = "ScreenArea:";
                _debugNotifications[1].Text = hit.Screen.Description();
                _debugNotifications[2].Text =
                    "MeshUV " + hit.RawUv.X.ToString("0.000") + ", " + hit.RawUv.Y.ToString("0.000");
                _debugNotifications[3].Text =
                    "MappedUV " + hit.Uv.X.ToString("0.000") + ", " + hit.Uv.Y.ToString("0.000");
                _debugNotifications[4].Text = "Surface " +
                                              (hit.SurfacePoint.X.ToString("0.0") + ", " +
                                               hit.SurfacePoint.Y.ToString("0.0"));
                _debugNotifications[5].Text = hit.Distance.ToString("0.0") + "m";

                foreach (var notification in _debugNotifications)
                {
                    notification.AliveTime = 100;
                    notification.ResetAliveTime();
                    notification.Show();
                }
            }
        }

        static void DrawTriangleEdges(Vector3D a, Vector3D b, Vector3D c, ref Vector4 color, float thickness)
        {
            MySimpleObjectDraw.DrawLine(a, b, null, ref color, thickness,
                MyBillboard.BlendTypeEnum.AdditiveTop);
            MySimpleObjectDraw.DrawLine(b, c, null, ref color, thickness,
                MyBillboard.BlendTypeEnum.AdditiveTop);
            MySimpleObjectDraw.DrawLine(c, a, null, ref color, thickness,
                MyBillboard.BlendTypeEnum.AdditiveTop);
        }

        static void DebugDrawScreenGeometry(
            MatrixD worldMatrix,
            MinimalMwmScreenAreaGeometry geometry,
            IMyModel blockModel,
            ref Vector4 color)
        {
            if (geometry == null || geometry.Triangles == null)
                return;

            const float thickness = 0.1f;
            for (int i = 0; i < geometry.TriangleIndices.Count; i++)
            {
                var triangle = geometry.TriangleIndices[i];
                var t = blockModel.GetTriangle(triangle);

                Vector3 a, b, c;
                blockModel.GetVertex(t.I0, t.I1, t.I2, out a, out b, out c);

                DrawTriangleEdges(
                    Vector3.Transform(a, worldMatrix),
                    Vector3.Transform(b, worldMatrix),
                    Vector3.Transform(c, worldMatrix),
                    ref color, thickness);
            }


            color = new Vector4(1f, 0.15f, 0f, 1f);
            for (int i = 0; i < geometry.Triangles.Count; i++)
            {
                var triangle = geometry.Triangles[i];
                var a = Vector3D.Transform((Vector3D)triangle.A, worldMatrix);
                var b = Vector3D.Transform((Vector3D)triangle.B, worldMatrix);
                var c = Vector3D.Transform((Vector3D)triangle.C, worldMatrix);

                DrawTriangleEdges(a, b, c,
                    ref color, thickness);
            }
        }

        static void DebugDrawMappedBounds(MatrixD worldMatrix, MinimalMwmScreenAreaGeometry geometry,
            IMyModel blockModel)
        {
            if (geometry == null || geometry.TriangleIndices == null || blockModel == null)
                return;

            var edges = new Dictionary<EdgeKey, EdgeInfo>();
            for (int i = 0; i < geometry.TriangleIndices.Count; i++)
            {
                var triangle = blockModel.GetTriangle(geometry.TriangleIndices[i]);

                Vector3 a;
                Vector3 b;
                Vector3 c;
                blockModel.GetVertex(triangle.I0, triangle.I1, triangle.I2, out a, out b, out c);
                AddEdge(edges, triangle.I0, triangle.I1, a, b);
                AddEdge(edges, triangle.I1, triangle.I2, b, c);
                AddEdge(edges, triangle.I2, triangle.I0, c, a);
            }

            var color = new Vector4(1f, 0.45f, 0f, 1f);
            const float thickness = 0.06f;
            foreach (var pair in edges)
            {
                var edge = pair.Value;
                if (edge.Count != 1)
                    continue;

                var a = Vector3D.Transform(edge.A, worldMatrix);
                var b = Vector3D.Transform(edge.B, worldMatrix);
                MySimpleObjectDraw.DrawLine(a, b, null, ref color, thickness,
                    MyBillboard.BlendTypeEnum.AdditiveTop);
            }
        }

        class DebugHitInfo
        {
            public SurfaceScriptBase Screen;
            public Vector2 Uv;
            public Vector2 RawUv;
            public Vector2 SurfacePoint;
            public double Distance;
        }


        static void AddEdge(
            Dictionary<EdgeKey, EdgeInfo> edges,
            int aIndex,
            int bIndex,
            Vector3D a,
            Vector3D b)
        {
            var key = new EdgeKey(aIndex, bIndex);
            EdgeInfo edge;
            if (edges.TryGetValue(key, out edge))
            {
                edge.Count++;
                edges[key] = edge;
                return;
            }

            edges[key] = new EdgeInfo
            {
                A = a,
                B = b,
                Count = 1
            };
        }

        struct EdgeInfo
        {
            public Vector3D A;
            public Vector3D B;
            public int Count;
        }


        struct EdgeKey : IEquatable<EdgeKey>
        {
            readonly int _a;
            readonly int _b;

            public EdgeKey(int a, int b)
            {
                if (a <= b)
                {
                    _a = a;
                    _b = b;
                }
                else
                {
                    _a = b;
                    _b = a;
                }
            }

            public override bool Equals(object obj)
            {
                if (!(obj is EdgeKey))
                    return false;

                var other = (EdgeKey)obj;
                return _a == other._a && _b == other._b;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_a * 397) ^ _b;
                }
            }

            public bool Equals(EdgeKey other)
            {
                return _a == other._a && _b == other._b;
            }
        }

#endif
    }
}