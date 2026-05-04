using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Graph.Apps.Abstract;
using Graph.Extensions;
using Graph.Helpers;
using Graph.System.Modules;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using VRageRender;


namespace Graph.System.ScreenAreas
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
                AddEdge(edges, triangle.I0, triangle.I1, (Vector3D)a, (Vector3D)b);
                AddEdge(edges, triangle.I1, triangle.I2, (Vector3D)b, (Vector3D)c);
                AddEdge(edges, triangle.I2, triangle.I0, (Vector3D)c, (Vector3D)a);
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

        static bool TryGetUvAlignedBounds(
            MinimalMwmScreenAreaGeometry geometry,
            IMyModel blockModel,
            out Vector3D origin,
            out Vector3D axisU,
            out Vector3D axisV)
        {
            origin = Vector3D.Zero;
            axisU = Vector3D.Zero;
            axisV = Vector3D.Zero;
            if (geometry == null || geometry.TriangleIndices == null || geometry.UvByVertexIndex == null ||
                blockModel == null)
                return false;

            var samples = new List<UvPositionSample>();
            for (int i = 0; i < geometry.TriangleIndices.Count; i++)
            {
                var triangle = blockModel.GetTriangle(geometry.TriangleIndices[i]);
                AddUvPositionSample(blockModel, triangle.I0, geometry, samples);
                AddUvPositionSample(blockModel, triangle.I1, geometry, samples);
                AddUvPositionSample(blockModel, triangle.I2, geometry, samples);
            }

            if (samples.Count < 3)
                return false;

            Vector2 minUv;
            Vector2 maxUv;
            Vector3D avgPosition;
            if (!GetUvPositionAverages(samples, out minUv, out maxUv, out avgPosition))
                return false;

            var avgUv = (minUv + maxUv) * 0.5f;
            var uRange = maxUv.X - minUv.X;
            var vRange = maxUv.Y - minUv.Y;
            if (Math.Abs(uRange) <= 1e-6f || Math.Abs(vRange) <= 1e-6f)
                return false;

            var uDirection = Vector3D.Zero;
            var vDirection = Vector3D.Zero;
            for (int i = 0; i < samples.Count; i++)
            {
                var centeredPosition = samples[i].Position - avgPosition;
                var centeredUv = samples[i].Uv - avgUv;
                uDirection += centeredPosition * centeredUv.X;
                vDirection += centeredPosition * centeredUv.Y;
            }

            if (uDirection.LengthSquared() <= 1e-8 || vDirection.LengthSquared() <= 1e-8)
                return false;

            uDirection.Normalize();
            vDirection = Reject(vDirection, uDirection);
            if (vDirection.LengthSquared() <= 1e-8)
                return false;
            vDirection.Normalize();

            double minU;
            double maxU;
            double minV;
            double maxV;
            GetProjectedRanges(samples, uDirection, vDirection, out minU, out maxU, out minV, out maxV);

            origin = avgPosition + uDirection * minU + vDirection * minV;
            axisU = uDirection * (maxU - minU);
            axisV = vDirection * (maxV - minV);
            return true;
        }

        static bool TryGetRuntimeScreenBounds(
            MinimalMwmScreenAreaGeometry geometry,
            IMyModel blockModel,
            out Vector3D min,
            out Vector3D max)
        {
            min = Vector3D.Zero;
            max = Vector3D.Zero;
            if (geometry == null || geometry.TriangleIndices == null || blockModel == null)
                return false;

            var hasBounds = false;
            for (int i = 0; i < geometry.TriangleIndices.Count; i++)
            {
                var triangle = blockModel.GetTriangle(geometry.TriangleIndices[i]);

                Vector3 a;
                Vector3 b;
                Vector3 c;
                blockModel.GetVertex(triangle.I0, triangle.I1, triangle.I2, out a, out b, out c);
                IncludeBounds((Vector3D)a, ref min, ref max, ref hasBounds);
                IncludeBounds((Vector3D)b, ref min, ref max, ref hasBounds);
                IncludeBounds((Vector3D)c, ref min, ref max, ref hasBounds);
            }

            return hasBounds;
        }

        static void AddUvPositionSample(
            IMyModel blockModel,
            int vertexIndex,
            MinimalMwmScreenAreaGeometry geometry,
            List<UvPositionSample> samples)
        {
            Vector2 rawUv;
            if (!geometry.UvByVertexIndex.TryGetValue(vertexIndex, out rawUv))
                return;

            Vector3 position;
            Vector3 ignored1;
            Vector3 ignored2;
            blockModel.GetVertex(vertexIndex, vertexIndex, vertexIndex, out position, out ignored1, out ignored2);
            samples.Add(new UvPositionSample
            {
                Position = (Vector3D)position,
                Uv = ToScreenUv(rawUv)
            });
        }

        static bool GetUvPositionAverages(
            List<UvPositionSample> samples,
            out Vector2 minUv,
            out Vector2 maxUv,
            out Vector3D avgPosition)
        {
            minUv = Vector2.Zero;
            maxUv = Vector2.Zero;
            avgPosition = Vector3D.Zero;
            if (samples == null || samples.Count == 0)
                return false;

            minUv = samples[0].Uv;
            maxUv = samples[0].Uv;
            for (int i = 0; i < samples.Count; i++)
            {
                minUv = Vector2.Min(minUv, samples[i].Uv);
                maxUv = Vector2.Max(maxUv, samples[i].Uv);
                avgPosition += samples[i].Position;
            }

            avgPosition /= samples.Count;
            return true;
        }

        static Vector3D Reject(Vector3D value, Vector3D normal)
        {
            return value - normal * Vector3D.Dot(value, normal);
        }

        static void GetProjectedRanges(
            List<UvPositionSample> samples,
            Vector3D uDirection,
            Vector3D vDirection,
            out double minU,
            out double maxU,
            out double minV,
            out double maxV)
        {
            minU = 0d;
            maxU = 0d;
            minV = 0d;
            maxV = 0d;
            if (samples == null || samples.Count == 0)
                return;

            var center = Vector3D.Zero;
            for (int i = 0; i < samples.Count; i++)
                center += samples[i].Position;
            center /= samples.Count;

            for (int i = 0; i < samples.Count; i++)
            {
                var relative = samples[i].Position - center;
                var u = Vector3D.Dot(relative, uDirection);
                var v = Vector3D.Dot(relative, vDirection);
                if (i == 0)
                {
                    minU = u;
                    maxU = u;
                    minV = v;
                    maxV = v;
                }
                else
                {
                    minU = Math.Min(minU, u);
                    maxU = Math.Max(maxU, u);
                    minV = Math.Min(minV, v);
                    maxV = Math.Max(maxV, v);
                }
            }
        }

        static void IncludeBounds(Vector3D value, ref Vector3D min, ref Vector3D max, ref bool hasBounds)
        {
            if (!hasBounds)
            {
                min = value;
                max = value;
                hasBounds = true;
                return;
            }

            min = Vector3D.Min(min, value);
            max = Vector3D.Max(max, value);
        }

        static void DrawQuadEdges(Vector3D p0, Vector3D p1, Vector3D p2, Vector3D p3, ref Vector4 color,
            float thickness)
        {
            MySimpleObjectDraw.DrawLine(p0, p1, null, ref color, thickness,
                MyBillboard.BlendTypeEnum.AdditiveTop);
            MySimpleObjectDraw.DrawLine(p1, p2, null, ref color, thickness,
                MyBillboard.BlendTypeEnum.AdditiveTop);
            MySimpleObjectDraw.DrawLine(p2, p3, null, ref color, thickness,
                MyBillboard.BlendTypeEnum.AdditiveTop);
            MySimpleObjectDraw.DrawLine(p3, p0, null, ref color, thickness,
                MyBillboard.BlendTypeEnum.AdditiveTop);
        }

        static void SelectLargestAxes(Vector3D ranges, out int first, out int second)
        {
            first = -1;
            second = -1;
            for (int axis = 0; axis < 3; axis++)
            {
                var range = GetAxis(ranges, axis);
                if (range <= 1e-6)
                    continue;

                if (first < 0 || range > GetAxis(ranges, first))
                {
                    second = first;
                    first = axis;
                }
                else if (second < 0 || range > GetAxis(ranges, second))
                {
                    second = axis;
                }
            }
        }

        static double GetAxis(Vector3D value, int axis)
        {
            if (axis == 0)
                return value.X;
            if (axis == 1)
                return value.Y;
            return value.Z;
        }

        static void SetAxis(ref Vector3D value, int axis, double axisValue)
        {
            if (axis == 0)
            {
                value.X = axisValue;
                return;
            }

            if (axis == 1)
            {
                value.Y = axisValue;
                return;
            }

            value.Z = axisValue;
        }

        struct EdgeInfo
        {
            public Vector3D A;
            public Vector3D B;
            public int Count;
        }

        struct UvPositionSample
        {
            public Vector3D Position;
            public Vector2 Uv;
        }


        struct EdgeKey
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
        }

        static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        class GeometryProjection
        {
            readonly List<Vector3D> _positions = new List<Vector3D>();
            readonly List<Vector2> _uvs = new List<Vector2>();
            bool _hasBounds;
            Vector3D _min;
            Vector3D _max;

            public void AddSample(Vector3D position, Vector2 uv)
            {
                _positions.Add(position);
                _uvs.Add(uv);

                if (!_hasBounds)
                {
                    _min = position;
                    _max = position;
                    _hasBounds = true;
                    return;
                }

                _min = Vector3D.Min(_min, position);
                _max = Vector3D.Max(_max, position);
            }

            public bool TryProject(Vector3D position, out Vector2 uv)
            {
                uv = Vector2.Zero;
                if (!_hasBounds || _positions.Count == 0)
                    return false;

                var ranges = _max - _min;
                var axisX = SelectAxisForUv(0, -1);
                var axisY = SelectAxisForUv(1, axisX);
                if (axisX < 0 || axisY < 0)
                    SelectLargestAxes(ranges, out axisX, out axisY);
                if (axisX < 0 || axisY < 0)
                    return false;

                var x = NormalizeAxis(position, axisX);
                var y = NormalizeAxis(position, axisY);

                if (GetCovariance(axisX, 0) < 0f)
                    x = 1f - x;
                if (GetCovariance(axisY, 1) < 0f)
                    y = 1f - y;

                uv = new Vector2(x, y);
                return true;
            }

            int SelectAxisForUv(int uvComponent, int excludedAxis)
            {
                var bestAxis = -1;
                var bestScore = 0f;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (axis == excludedAxis || GetRange(axis) <= 1e-6)
                        continue;

                    var score = Math.Abs(GetCovariance(axis, uvComponent));
                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    bestAxis = axis;
                }

                return bestAxis;
            }

            float GetCovariance(int axis, int uvComponent)
            {
                if (_positions.Count == 0)
                    return 0f;

                var sumAxis = 0f;
                var sumUv = 0f;
                for (int i = 0; i < _positions.Count; i++)
                {
                    sumAxis += NormalizeAxis(_positions[i], axis);
                    sumUv += GetUvComponent(_uvs[i], uvComponent);
                }

                var avgAxis = sumAxis / _positions.Count;
                var avgUv = sumUv / _positions.Count;
                var covariance = 0f;
                for (int i = 0; i < _positions.Count; i++)
                    covariance += (NormalizeAxis(_positions[i], axis) - avgAxis) *
                                  (GetUvComponent(_uvs[i], uvComponent) - avgUv);

                return covariance;
            }

            float NormalizeAxis(Vector3D position, int axis)
            {
                var range = GetRange(axis);
                if (range <= 1e-6)
                    return 0f;

                return (float)((GetAxis(position, axis) - GetAxis(_min, axis)) / range);
            }

            double GetRange(int axis)
            {
                return GetAxis(_max, axis) - GetAxis(_min, axis);
            }

            static double GetAxis(Vector3D value, int axis)
            {
                if (axis == 0)
                    return value.X;
                if (axis == 1)
                    return value.Y;
                return value.Z;
            }

            static float GetUvComponent(Vector2 value, int component)
            {
                return component == 0 ? value.X : value.Y;
            }

            static void SelectLargestAxes(Vector3D ranges, out int first, out int second)
            {
                first = -1;
                second = -1;
                for (int axis = 0; axis < 3; axis++)
                {
                    var range = GetAxis(ranges, axis);
                    if (range <= 1e-6)
                        continue;

                    if (first < 0 || range > GetAxis(ranges, first))
                    {
                        second = first;
                        first = axis;
                    }
                    else if (second < 0 || range > GetAxis(ranges, second))
                    {
                        second = axis;
                    }
                }
            }
        }
#endif
    }
}