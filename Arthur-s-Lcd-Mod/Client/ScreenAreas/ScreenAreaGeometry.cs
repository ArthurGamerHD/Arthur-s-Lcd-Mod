using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Client.ScreenAreas
{
    public static partial class ScreenAreaGeometry
    {
        static readonly Dictionary<string, CachedScreenArea> Cache =
            new Dictionary<string, CachedScreenArea>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Matrix> LocalMatrixCache =
            new Dictionary<string, Matrix>(StringComparer.OrdinalIgnoreCase);

        public static bool TryGetScreenUvIntersection(
            SurfaceScriptBase screen,
            Vector3D rayOrigin,
            Vector3D rayDirection,
            out Vector2 uv)
        {
            uv = Vector2.Zero;
            if (screen == null || screen.Block == null)
                return false;
            if (rayDirection.LengthSquared() <= 1e-8)
                return false;

            rayDirection.Normalize();

            MinimalMwmScreenAreaGeometry geometry;
            if (!TryGetScreenAreaGeometry(screen, out geometry))
                return false;

            return TryGetScreenUvIntersection(
                screen.Block.Model,
                screen.Block.WorldMatrix,
                geometry,
                rayOrigin,
                rayDirection,
                out uv);
        }

        public static bool TryGetScreenPointIntersection(
            SurfaceScriptBase screen,
            Vector3D rayOrigin,
            Vector3D rayDirection,
            out Vector2 screenPoint)
        {
            screenPoint = Vector2.Zero;
            if (screen == null || screen.Surface == null)
                return false;

            Vector2 uv;
            if (!TryGetScreenUvIntersection(screen, rayOrigin, rayDirection, out uv))
                return false;

            screenPoint = ToSurfacePoint(screen.Surface, uv);
            return true;
        }

        public static bool IsScreenAreaVisibleToCamera(SurfaceScriptBase screen)
        {
            if (screen == null || screen.Block == null)
                return false;

            var session = MyAPIGateway.Session;
            var camera = session?.Camera;
            if (camera == null)
                return false;

            MinimalMwmScreenAreaGeometry geometry;
            if (!TryGetScreenAreaGeometry(screen, out geometry))
                return false;

            BoundingBoxD bounds;
            if (!TryGetScreenAreaWorldBounds(
                    screen.Block.Model,
                    screen.Block.WorldMatrix,
                    geometry,
                    camera.Position,
                    out bounds))
                return false;

            double maxDistance = Math.Max(1d, camera.FarPlaneDistance);
            double distanceSq = Vector3D.DistanceSquared(camera.Position, bounds.Center);
            if (distanceSq > maxDistance * maxDistance)
                return false;

            bounds.Inflate(0.05d);
            return camera.IsInFrustum(ref bounds);
        }

        public static bool TryGetScreenWorldNormalDirection(SurfaceScriptBase screen, out Vector3D normal)
        {
            normal = Vector3D.Zero;
            if (screen == null || screen.Block == null)
                return false;

            MinimalMwmScreenAreaGeometry geometry;
            if (!TryGetScreenAreaGeometry(screen, out geometry))
                return false;

            return TryGetScreenWorldNormalDirection(
                screen.Block.Model,
                screen.Block.WorldMatrix,
                geometry,
                out normal);
        }

        public static bool TryGetScreenLocalNormalDirection(SurfaceScriptBase screen, out Vector3 normal)
        {
            normal = Vector3.Zero;
            if (screen == null || screen.Block == null)
                return false;

            MinimalMwmScreenAreaGeometry geometry;
            if (!TryGetScreenAreaGeometry(screen, out geometry))
                return false;

            return TryGetScreenLocalNormalDirection(screen.Block.Model, geometry, out normal);
        }

        public static bool TryGetScreenLocalMatrix(SurfaceScriptBase screen, out Matrix localMatrix)
        {
            localMatrix = Matrix.Identity;
            if (screen == null || screen.Block == null)
                return false;

            var localKey = BuildLocalMatrixCacheKey(screen.Block, screen.RotationOrSurfaceIndex);
            if (!string.IsNullOrEmpty(localKey) && LocalMatrixCache.TryGetValue(localKey, out localMatrix))
                return true;

            MinimalMwmScreenAreaGeometry geometry;
            if (!TryGetScreenAreaGeometry(screen, out geometry))
                return false;

            if (!TryGetScreenLocalMatrix(screen.Block.Model, geometry, out localMatrix))
                return false;

            if (!string.IsNullOrEmpty(localKey))
                LocalMatrixCache[localKey] = localMatrix;

            return true;
        }

        public static bool TryGetScreenWorldMatrix(IMyCubeBlock block, int surfaceIndex, out MatrixD worldMatrix)
        {
            worldMatrix = MatrixD.Identity;
            if (block == null)
                return false;

            Matrix localMatrix;
            if (!TryGetScreenLocalMatrix(block, surfaceIndex, out localMatrix))
                return false;

            worldMatrix = (MatrixD)localMatrix * block.WorldMatrix;
            return true;
        }

        public static bool TryGetScreenLocalMatrix(IMyCubeBlock block, int surfaceIndex, out Matrix localMatrix)
        {
            localMatrix = Matrix.Identity;
            if (block == null)
                return false;

            var localKey = BuildLocalMatrixCacheKey(block, surfaceIndex);
            if (!string.IsNullOrEmpty(localKey) && LocalMatrixCache.TryGetValue(localKey, out localMatrix))
                return true;

            MinimalMwmScreenAreaGeometry geometry;
            if (!TryGetScreenAreaGeometry(block, surfaceIndex, out geometry))
                return false;

            var blockEntity = (IMyEntity)block;
            if (blockEntity.Model == null)
                return false;

            if (!TryGetScreenLocalMatrix(blockEntity.Model, geometry, out localMatrix))
                return false;

            if (!string.IsNullOrEmpty(localKey))
                LocalMatrixCache[localKey] = localMatrix;

            return true;
        }

        public static bool TryGetScreenWorldMatrix(SurfaceScriptBase screen, out MatrixD worldMatrix)
        {
            worldMatrix = MatrixD.Identity;
            if (screen == null || screen.Block == null)
                return false;

            Matrix localMatrix;
            if (!TryGetScreenLocalMatrix(screen, out localMatrix))
                return false;

            worldMatrix = (MatrixD)localMatrix * screen.Block.WorldMatrix;
            return true;
        }

        static Vector2 ToSurfacePoint(Sandbox.ModAPI.Ingame.IMyTextSurface surface, Vector2 uv)
        {
            if (surface == null)
                return Vector2.Zero;

            var offset = (surface.TextureSize - surface.SurfaceSize) * 0.5f;
            return new Vector2(
                offset.X + uv.X * surface.SurfaceSize.X,
                offset.Y + uv.Y * surface.SurfaceSize.Y);
        }

        static bool TryGetScreenAreaWorldBounds(
            IMyModel blockModel,
            MatrixD worldMatrix,
            MinimalMwmScreenAreaGeometry geometry,
            Vector3D cameraPosition,
            out BoundingBoxD bounds)
        {
            bounds = default(BoundingBoxD);
            if (blockModel == null || geometry == null || geometry.TriangleIndices == null)
                return false;

            var min = new Vector3D(double.MaxValue);
            var max = new Vector3D(double.MinValue);
            var hasPoint = false;
            var hasFacingTriangle = false;

            for (int i = 0; i < geometry.TriangleIndices.Count; i++)
            {
                var triangle = blockModel.GetTriangle(geometry.TriangleIndices[i]);

                Vector3 aLocal;
                Vector3 bLocal;
                Vector3 cLocal;
                blockModel.GetVertex(triangle.I0, triangle.I1, triangle.I2, out aLocal, out bLocal, out cLocal);

                var a = Vector3D.Transform((Vector3D)aLocal, worldMatrix);
                var b = Vector3D.Transform((Vector3D)bLocal, worldMatrix);
                var c = Vector3D.Transform((Vector3D)cLocal, worldMatrix);

                IncludePoint(ref min, ref max, a);
                IncludePoint(ref min, ref max, b);
                IncludePoint(ref min, ref max, c);
                hasPoint = true;

                var normal = Vector3D.Cross(b - a, c - a);
                var center = (a + b + c) / 3d;
                if (Vector3D.Dot(normal, cameraPosition - center) > 1e-9)
                    hasFacingTriangle = true;
            }

            if (!hasPoint || !hasFacingTriangle)
                return false;

            bounds = new BoundingBoxD(min, max);
            return true;
        }

        static bool TryGetScreenWorldNormalDirection(
            IMyModel blockModel,
            MatrixD worldMatrix,
            MinimalMwmScreenAreaGeometry geometry,
            out Vector3D normal)
        {
            normal = Vector3D.Zero;
            Vector3 localNormal;
            if (!TryGetScreenLocalNormalDirection(blockModel, geometry, out localNormal))
                return false;

            normal = Vector3D.TransformNormal((Vector3D)localNormal, worldMatrix);
            if (normal.LengthSquared() <= 1e-12)
                return false;

            normal.Normalize();
            return true;
        }

        static bool TryGetScreenLocalNormalDirection(
            IMyModel blockModel,
            MinimalMwmScreenAreaGeometry geometry,
            out Vector3 normal)
        {
            normal = Vector3.Zero;
            if (blockModel == null || geometry == null || geometry.TriangleIndices == null ||
                geometry.UvTriangles == null)
                return false;

            int bestTriangleIndex;
            if (!TryGetCenterTriangleIndex(geometry, out bestTriangleIndex))
                return false;

            return TryGetScreenTriangleLocalNormal(
                blockModel,
                geometry.TriangleIndices[bestTriangleIndex],
                out normal);
        }

        static bool TryGetScreenLocalMatrix(
            IMyModel blockModel,
            MinimalMwmScreenAreaGeometry geometry,
            out Matrix localMatrix)
        {
            localMatrix = Matrix.Identity;
            if (blockModel == null || geometry == null || geometry.TriangleIndices == null ||
                geometry.UvTriangles == null)
                return false;

            int bestTriangleIndex;
            if (!TryGetCenterTriangleIndex(geometry, out bestTriangleIndex))
                return false;

            Vector3 centerPoint;
            Vector3 right;
            Vector3 upFromUv;
            Vector3 forward;
            if (!TryGetScreenLocalAxesFromTriangleUv(blockModel, geometry, bestTriangleIndex, out right, out upFromUv, out forward))
                return false;

            var rawCenter = GetScreenCenterRawUv(geometry);
            if (!TryGetLocalPointAtRawUv(blockModel, geometry, rawCenter, out centerPoint))
            {
                var triangle = blockModel.GetTriangle(geometry.TriangleIndices[bestTriangleIndex]);
                Vector3 aLocal;
                Vector3 bLocal;
                Vector3 cLocal;
                blockModel.GetVertex(triangle.I0, triangle.I1, triangle.I2, out aLocal, out bLocal, out cLocal);
                centerPoint = (aLocal + bLocal + cLocal) / 3f;
            }

            localMatrix = Matrix.Identity;
            localMatrix.Forward = forward;
            localMatrix.Right = right;
            localMatrix.Up = upFromUv;
            localMatrix.Translation = centerPoint;
            return true;
        }

        static bool TryGetScreenLocalAxesFromTriangleUv(
            IMyModel blockModel,
            MinimalMwmScreenAreaGeometry geometry,
            int geometryTriangleIndex,
            out Vector3 right,
            out Vector3 up,
            out Vector3 forward)
        {
            right = Vector3.Zero;
            up = Vector3.Zero;
            forward = Vector3.Zero;
            if (blockModel == null || geometry == null || geometry.TriangleIndices == null || geometry.UvTriangles == null)
                return false;

            if (geometryTriangleIndex < 0 || geometryTriangleIndex >= geometry.TriangleIndices.Count ||
                geometryTriangleIndex >= geometry.UvTriangles.Count)
                return false;

            var modelTriangle = blockModel.GetTriangle(geometry.TriangleIndices[geometryTriangleIndex]);
            Vector3 p0;
            Vector3 p1;
            Vector3 p2;
            blockModel.GetVertex(modelTriangle.I0, modelTriangle.I1, modelTriangle.I2, out p0, out p1, out p2);

            Vector2 uv0;
            Vector2 uv1;
            Vector2 uv2;
            if (!TryGetRuntimeTriangleUvs(geometry, modelTriangle.I0, modelTriangle.I1, modelTriangle.I2, out uv0, out uv1, out uv2))
            {
                var fallback = geometry.UvTriangles[geometryTriangleIndex];
                uv0 = fallback.A;
                uv1 = fallback.B;
                uv2 = fallback.C;
            }

            var dp1 = p1 - p0;
            var dp2 = p2 - p0;
            var duv1 = uv1 - uv0;
            var duv2 = uv2 - uv0;
            var det = duv1.X * duv2.Y - duv1.Y * duv2.X;
            if (Math.Abs(det) <= 1e-10f)
                return false;

            var invDet = 1f / det;
            var dPdu = (dp1 * duv2.Y - dp2 * duv1.Y) * invDet;
            var dPdv = (dp2 * duv1.X - dp1 * duv2.X) * invDet;

            right = dPdu;
            if (right.LengthSquared() <= 1e-12f)
                return false;
            right.Normalize();

            // Screen Y grows downward; raw UV Y grows upward, so "up" in screen space is -dPdv.
            up = -dPdv;
            up -= right * Vector3.Dot(up, right);
            if (up.LengthSquared() <= 1e-12f)
                return false;
            up.Normalize();

            // right x up points toward away-facing side in this screen-space convention.
            forward = Vector3.Cross(right, up);
            if (forward.LengthSquared() <= 1e-12f)
                return false;
            forward.Normalize();

            // Re-orthonormalize up from right/forward to preserve away-facing forward
            // without introducing vertical mirroring.
            up = Vector3.Cross(right, forward);
            if (up.LengthSquared() <= 1e-12f)
                return false;
            up.Normalize();

            return true;
        }

        static Vector2 GetScreenCenterRawUv(MinimalMwmScreenAreaGeometry geometry)
        {
            if (geometry != null && geometry.HasUvBounds)
                return (geometry.UvMin + geometry.UvMax) * 0.5f;

            return new Vector2(0.5f, -0.5f);
        }

        static bool TryGetCenterTriangleIndex(MinimalMwmScreenAreaGeometry geometry, out int triangleIndex)
        {
            triangleIndex = -1;
            if (geometry == null || geometry.TriangleIndices == null || geometry.UvTriangles == null)
                return false;

            var centerRawUv = GetScreenCenterRawUv(geometry);
            var count = Math.Min(geometry.TriangleIndices.Count, geometry.UvTriangles.Count);
            var bestDistanceSq = double.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var uvTriangle = geometry.UvTriangles[i];
                float u;
                float v;
                float w;
                if (TryGetUvBarycentric(centerRawUv, uvTriangle.A, uvTriangle.B, uvTriangle.C,
                        out u, out v, out w))
                {
                    triangleIndex = i;
                    return true;
                }

                var distanceSq = DistanceSquaredToUvTriangle(centerRawUv, uvTriangle);
                if (distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                triangleIndex = i;
            }

            return triangleIndex >= 0;
        }

        static bool TryGetScreenTriangleLocalNormal(
            IMyModel blockModel,
            int triangleIndex,
            out Vector3 normal)
        {
            normal = Vector3.Zero;
            if (blockModel == null)
                return false;

            var triangle = blockModel.GetTriangle(triangleIndex);

            Vector3 aLocal;
            Vector3 bLocal;
            Vector3 cLocal;
            blockModel.GetVertex(triangle.I0, triangle.I1, triangle.I2, out aLocal, out bLocal, out cLocal);

            var edge1 = bLocal - aLocal;
            var edge2 = cLocal - aLocal;
            normal = Vector3.Cross(edge1, edge2);
            if (normal.LengthSquared() <= 1e-12f)
                return false;

            normal.Normalize();
            return true;
        }

        static bool TryGetLocalPointAtRawUv(
            IMyModel blockModel,
            MinimalMwmScreenAreaGeometry geometry,
            Vector2 rawUv,
            out Vector3 point)
        {
            point = Vector3.Zero;
            if (blockModel == null || geometry == null || geometry.TriangleIndices == null ||
                geometry.UvTriangles == null)
                return false;

            var count = Math.Min(geometry.TriangleIndices.Count, geometry.UvTriangles.Count);
            var bestTriangleIndex = -1;
            var bestDistanceSq = double.MaxValue;
            float bestU = 0f;
            float bestV = 0f;
            float bestW = 0f;

            for (int i = 0; i < count; i++)
            {
                var triangle = blockModel.GetTriangle(geometry.TriangleIndices[i]);
                Vector2 uv0;
                Vector2 uv1;
                Vector2 uv2;
                if (!TryGetRuntimeTriangleUvs(geometry, triangle.I0, triangle.I1, triangle.I2, out uv0, out uv1, out uv2))
                {
                    var fallback = geometry.UvTriangles[i];
                    uv0 = fallback.A;
                    uv1 = fallback.B;
                    uv2 = fallback.C;
                }

                float u;
                float v;
                float w;
                if (TryGetUvBarycentric(rawUv, uv0, uv1, uv2, out u, out v, out w))
                {
                    bestTriangleIndex = i;
                    bestU = u;
                    bestV = v;
                    bestW = w;
                    break;
                }

                var distanceSq = DistanceSquaredToUvTriangle(rawUv, new MinimalMwmUvTriangle(uv0, uv1, uv2));
                if (distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                bestTriangleIndex = i;
                GetClosestUvBarycentric(rawUv, uv0, uv1, uv2, out bestU, out bestV, out bestW);
            }

            if (bestTriangleIndex < 0)
                return false;

            var bestTriangle = blockModel.GetTriangle(geometry.TriangleIndices[bestTriangleIndex]);
            Vector3 aLocal;
            Vector3 bLocal;
            Vector3 cLocal;
            blockModel.GetVertex(bestTriangle.I0, bestTriangle.I1, bestTriangle.I2, out aLocal, out bLocal, out cLocal);
            point = aLocal * bestU + bLocal * bestV + cLocal * bestW;
            return true;
        }

        static void GetClosestUvBarycentric(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            out float u,
            out float v,
            out float w)
        {
            var abPoint = ClosestPointOnUvSegment(point, a, b);
            var bcPoint = ClosestPointOnUvSegment(point, b, c);
            var caPoint = ClosestPointOnUvSegment(point, c, a);
            var abDistance = Vector2.DistanceSquared(point, abPoint);
            var bcDistance = Vector2.DistanceSquared(point, bcPoint);
            var caDistance = Vector2.DistanceSquared(point, caPoint);

            if (abDistance <= bcDistance && abDistance <= caDistance)
            {
                var t = GetUvSegmentT(abPoint, a, b);
                u = 1f - t;
                v = t;
                w = 0f;
                return;
            }

            if (bcDistance <= caDistance)
            {
                var t = GetUvSegmentT(bcPoint, b, c);
                u = 0f;
                v = 1f - t;
                w = t;
                return;
            }

            var caT = GetUvSegmentT(caPoint, c, a);
            u = caT;
            v = 0f;
            w = 1f - caT;
        }

        static Vector2 ClosestPointOnUvSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var segment = b - a;
            var lengthSq = segment.LengthSquared();
            if (lengthSq <= 1e-12f)
                return a;

            var t = MathHelper.Clamp(Vector2.Dot(point - a, segment) / lengthSq, 0f, 1f);
            return a + segment * t;
        }

        static float GetUvSegmentT(Vector2 point, Vector2 a, Vector2 b)
        {
            var segment = b - a;
            var lengthSq = segment.LengthSquared();
            if (lengthSq <= 1e-12f)
                return 0f;

            return MathHelper.Clamp(Vector2.Dot(point - a, segment) / lengthSq, 0f, 1f);
        }

        static bool TryGetRuntimeTriangleUvs(
            MinimalMwmScreenAreaGeometry geometry,
            int i0,
            int i1,
            int i2,
            out Vector2 uv0,
            out Vector2 uv1,
            out Vector2 uv2)
        {
            uv0 = Vector2.Zero;
            uv1 = Vector2.Zero;
            uv2 = Vector2.Zero;

            if (geometry == null || geometry.UvByVertexIndex == null)
                return false;

            return geometry.UvByVertexIndex.TryGetValue(i0, out uv0) &&
                   geometry.UvByVertexIndex.TryGetValue(i1, out uv1) &&
                   geometry.UvByVertexIndex.TryGetValue(i2, out uv2);
        }

        static bool TryGetUvBarycentric(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            out float u,
            out float v,
            out float w)
        {
            const float epsilon = 1e-5f;
            u = 0f;
            v = 0f;
            w = 0f;

            var v0 = b - a;
            var v1 = c - a;
            var v2 = point - a;
            var d00 = Vector2.Dot(v0, v0);
            var d01 = Vector2.Dot(v0, v1);
            var d11 = Vector2.Dot(v1, v1);
            var d20 = Vector2.Dot(v2, v0);
            var d21 = Vector2.Dot(v2, v1);
            var denom = d00 * d11 - d01 * d01;
            if (Math.Abs(denom) <= epsilon)
                return false;

            v = (d11 * d20 - d01 * d21) / denom;
            w = (d00 * d21 - d01 * d20) / denom;
            u = 1f - v - w;
            return u >= -epsilon && v >= -epsilon && w >= -epsilon;
        }

        static double DistanceSquaredToUvTriangle(Vector2 point, MinimalMwmUvTriangle triangle)
        {
            float u;
            float v;
            float w;
            if (TryGetUvBarycentric(point, triangle.A, triangle.B, triangle.C, out u, out v, out w))
                return 0d;

            return Math.Min(
                DistanceSquaredToUvSegment(point, triangle.A, triangle.B),
                Math.Min(
                    DistanceSquaredToUvSegment(point, triangle.B, triangle.C),
                    DistanceSquaredToUvSegment(point, triangle.C, triangle.A)));
        }

        static double DistanceSquaredToUvSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var segment = b - a;
            var lengthSq = segment.LengthSquared();
            if (lengthSq <= 1e-12f)
                return Vector2.DistanceSquared(point, a);

            var t = MathHelper.Clamp(Vector2.Dot(point - a, segment) / lengthSq, 0f, 1f);
            var closest = a + segment * t;
            return Vector2.DistanceSquared(point, closest);
        }

        static void IncludePoint(ref Vector3D min, ref Vector3D max, Vector3D point)
        {
            if (point.X < min.X)
                min.X = point.X;
            if (point.Y < min.Y)
                min.Y = point.Y;
            if (point.Z < min.Z)
                min.Z = point.Z;

            if (point.X > max.X)
                max.X = point.X;
            if (point.Y > max.Y)
                max.Y = point.Y;
            if (point.Z > max.Z)
                max.Z = point.Z;
        }

        static bool TryGetScreenAreaGeometry(SurfaceScriptBase screen, out MinimalMwmScreenAreaGeometry geometry)
        {
            geometry = null;
            if (screen == null || screen.Block == null)
                return false;

            return TryGetScreenAreaGeometry(
                screen.Block,
                screen.RotationOrSurfaceIndex,
                screen.Description(),
                out geometry);
        }

        static bool TryGetScreenAreaGeometry(IMyCubeBlock block, int surfaceIndex, out MinimalMwmScreenAreaGeometry geometry)
        {
            return TryGetScreenAreaGeometry(block, surfaceIndex, block?.ToString() ?? "<null>", out geometry);
        }

        static bool TryGetScreenAreaGeometry(
            IMyCubeBlock block,
            int surfaceIndex,
            string description,
            out MinimalMwmScreenAreaGeometry geometry)
        {
            geometry = null;

            try
            {
                if (block == null)
                    return false;

                var blockEntity = (IMyEntity)block;
                if (blockEntity.Model == null)
                {
                    LogHelper.LogOnce("skip:no-model:" + block.EntityId,
                        "block has no loaded model: " + description);
                    return false;
                }

                var assetName = blockEntity.Model.AssetName;

                if (string.IsNullOrWhiteSpace(assetName))
                {
                    LogHelper.LogOnce("skip:no-asset:" + block.EntityId,
                        "block model has empty AssetName: " + description);
                    return false;
                }

                var materials = ResolveMaterialCandidates(block, surfaceIndex);
                if (materials.Count == 0)
                {
                    LogHelper.LogOnce("skip:no-materials:" + block.EntityId,
                        "no material candidates: " + description);
                    return false;
                }

                LogHelper.LogOnce(
                    "try:" + block.EntityId + ":" + surfaceIndex + ":" + assetName,
                    "trying " + description + ", asset=" + assetName + ", materials=" +
                    string.Join(", ", materials.ToArray()));

                for (int i = 0; i < materials.Count; i++)
                {
                    if (TryGetScreenAreaGeometry(assetName, materials[i], out geometry))
                        return true;
                }

                LogHelper.LogOnce(
                    "skip:no-match:" + block.EntityId + ":" + surfaceIndex + ":" + assetName,
                    "no matching screen geometry for " + description +
                    ", asset=" + assetName + ", materials=" + string.Join(", ", materials.ToArray()));
                return false;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, nameof(ScreenAreaGeometry));
            }

            return false;
        }

        static List<string> ResolveMaterialCandidates(IMyCubeBlock block, int surfaceIndex)
        {
            var result = new List<string>();
            var definition = block?.SlimBlock?.BlockDefinition as MyFunctionalBlockDefinition;

            if (definition == null)
            {
                LogHelper.LogOnce("skip:invalid-definition:" + block?.EntityId,
                    $"no matching MyFunctionalBlockDefinition for block {block?.BlockDefinition}");
                return result;
            }

            LogHelper.LogOnce($"offset:{block.BlockDefinition}",
                $"{block.BlockDefinition} " +
                $"Offset: {definition.ModelOffset} " +
                $"Scale: {block.Model.ScaleFactor}x " +
                $"Size: {block.Model.BoundingBoxSize}");

            if (surfaceIndex >= definition.ScreenAreas.Count)
            {
                LogHelper.LogOnce("skip:index-out-of-range:" + block.EntityId,
                    $"no matching surface {surfaceIndex} for block {block.BlockDefinition}, (range from 0 to {definition.ScreenAreas.Count}");
                return result;
            }

            AddMaterialCandidate(result, definition.ScreenAreas[surfaceIndex].Name);
            return result;
        }

        static void AddMaterialCandidate(List<string> materials, string material)
        {
            if (materials == null || string.IsNullOrWhiteSpace(material))
                return;

            for (int i = 0; i < materials.Count; i++)
                if (string.Equals(materials[i], material, StringComparison.OrdinalIgnoreCase))
                    return;

            materials.Add(material);
        }

        static string BuildLocalMatrixCacheKey(IMyCubeBlock block, int surfaceIndex)
        {
            if (block == null)
                return null;

            var definition = block.BlockDefinition;
            if (definition.TypeId.IsNull)
                return null;

            return definition.TypeIdString + "/" + definition.SubtypeName + "#" + surfaceIndex;
        }

        static bool TryGetScreenAreaGeometry(string assetName, string material,
            out MinimalMwmScreenAreaGeometry geometry)
        {
            geometry = null;
            if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(material))
                return false;

            var cacheKey = assetName + "|" + material;
            CachedScreenArea cached;
            if (Cache.TryGetValue(cacheKey, out cached))
            {
                geometry = cached.Geometry;
                if (geometry == null || geometry.TriangleCount <= 0)
                {
                    LogHelper.LogOnce("cache:miss:" + cacheKey,
                        "cached miss: " + cacheKey + ", reason=" + cached.LoadError);
                }

                return geometry != null && geometry.TriangleCount > 0;
            }

            cached = new CachedScreenArea();
            Cache[cacheKey] = cached;

            var modelPath = ToModelPath(assetName);

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                cached.LoadError = "could not normalize asset name to content path";
                LogHelper.LogOnce("path:invalid:" + cacheKey,
                    "invalid model path for " + cacheKey + ", asset=" + assetName);
                return false;
            }

#if DEBUG
            LogHelper.LogOnce("path:" + cacheKey, "normalized asset to path: " + assetName + " -> " + modelPath);
#endif
            string lodPath = modelPath;

            try
            {
                using (var model = OpenMwm(modelPath))
                {
                    List<string> loDs;
                    if (MinimalMwmReader.TryReadLodPaths(model, out loDs))
                    {
#if DEBUG
                        foreach (var lod in loDs)
                            LogHelper.LogOnce("path:lod:" + lod, $"Found lod {lod}");
#endif
                        lodPath = ToModelPath(loDs[0] + ".mwm");
                        LogHelper.LogOnce("using:lod:" + lodPath, $"Using {lodPath} for {modelPath}");
                    }
                    else
                    {
                        LogHelper.LogOnce("lod:missing:" + modelPath, $"No lod found for {modelPath}");
                    }
                }
            }
            catch (Exception e)
            {
                LogHelper.LogOnce("lod:error:" + modelPath, $"ERROR when trying to load LoDs for {modelPath}: {e}");
            }

            using (var reader = OpenMwm(lodPath) ?? OpenMwm(modelPath))
            {
                if (reader == null)
                {
                    cached.LoadError = "Mwm file not found in mod location or game content: " + assetName + ", " +
                                       lodPath;
                    LogHelper.LogOnce("file:missing:" + cacheKey, cached.LoadError);

                    return false;
                }

                MinimalMwmScreenAreaGeometry parsed;
                if (!MinimalMwmReader.TryReadScreenArea(reader, material, out parsed))
                {
                    cached.LoadError = "MWM parsed, but material was not found or had no triangles: " + material;
                    LogHelper.LogOnce("parse:no-material:" + cacheKey, cached.LoadError);
                    return false;
                }

                cached.Geometry = parsed;
                cached.LoadError = null;
                LogHelper.LogOnce(
                    "parse:ok:" + cacheKey,
                    "parsed screen geometry: " + cacheKey +
                    ", triangles=" + parsed.TriangleCount +
                    ", widthM=" + parsed.WidthM +
                    ", heightM=" + parsed.HeightM +
                    ", aspect=" + parsed.Aspect +
                    ", meshUvMin=" + parsed.UvMin +
                    ", meshUvMax=" + parsed.UvMax);
                geometry = parsed;
                return true;
            }
        }

        static BinaryReader OpenMwm(string content)
        {
            var utilities = MyAPIGateway.Utilities;
            if (string.IsNullOrWhiteSpace(content))
                return null;

            foreach (var mod in MyAPIGateway.Session.Mods.Where(mod => utilities.FileExistsInModLocation(content, mod)))
            {
                try
                {
                    LogHelper.LogOnce("file:mod:" + content,
                        $"opening MWM from mod ({mod.Name} {mod.PublishedServiceName}) location: " + content);
                    return utilities.ReadBinaryFileInModLocation(content, mod);
                }
                catch (Exception e)
                {   // mods can be unpredictable... had a guy uploading his entire bin64 folder to workshop and crashing this method
                    LogHelper.LogOnce("fail:file:mod:" + content,
                        $"Fail to load MWM from mod ({mod.Name} {mod.PublishedServiceName}) location: " + content + $"\n{e}");
                }
            }

            if (utilities.FileExistsInGameContent(content))
            {
                LogHelper.LogOnce("file:game:" + content, "opening MWM from game content: " + content);
                return utilities.ReadBinaryFileInGameContent(content);
            }

            LogHelper.LogOnce("fail:file:" + content, "Unable to locate MWM: " + content);
            return null;
        }

        static string ToModelPath(string assetName)
        {
            var path = assetName.Replace('\\', '/');

            const string contentMarker = "/Content/";

            var contentIndex = path.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
            if (contentIndex >= 0)
                path = path.Substring(contentIndex + contentMarker.Length);

            while (path.StartsWith("/", StringComparison.Ordinal))
                path = path.Substring(1);

            // most likely a mod, so remove the SpaceEngineersId/WorkshopId/ prefix
            if (path.StartsWith("244850/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring("244850/".Length);
                path = path.Substring(path.IndexOf("/", StringComparison.Ordinal) + 1); // skip mod id;
            }

            if (!path.EndsWith(".mwm", StringComparison.OrdinalIgnoreCase))
                return null;

            var extensionIndex = path.LastIndexOf(".mwm", StringComparison.OrdinalIgnoreCase);
            if (extensionIndex < 0)
                return null;

            return path.Substring(0, extensionIndex) + path.Substring(extensionIndex);
        }

        static bool TryGetScreenUvIntersection(
            IMyModel blockModel,
            MatrixD worldMatrix,
            MinimalMwmScreenAreaGeometry geometry,
            Vector3D rayOrigin,
            Vector3D rayDirection,
            out Vector2 uv)
        {
            double distance;
            Vector2 rawUv;
            return TryGetScreenIntersection(
                blockModel,
                worldMatrix,
                geometry,
                rayOrigin,
                rayDirection,
                double.MaxValue,
                out uv,
                out rawUv,
                out distance);
        }

        static bool TryGetScreenIntersection(
            IMyModel blockModel,
            MatrixD worldMatrix,
            MinimalMwmScreenAreaGeometry geometry,
            Vector3D rayOrigin,
            Vector3D rayDirection,
            double maxDistance,
            out Vector2 uv,
            out Vector2 rawUv,
            out double hitDistance)
        {
            uv = Vector2.Zero;
            rawUv = Vector2.Zero;
            hitDistance = 0d;
            if (blockModel == null || geometry == null || geometry.TriangleIndices == null ||
                geometry.UvTriangles == null)
                return false;

            var bestDistance = double.MaxValue;
            var bestRawUv = Vector2.Zero;
            var hit = false;
            var count = Math.Min(geometry.TriangleIndices.Count, geometry.UvTriangles.Count);
            for (int i = 0; i < count; i++)
            {
                var triangleIndex = geometry.TriangleIndices[i];
                var t = blockModel.GetTriangle(triangleIndex);

                Vector3 aLocal;
                Vector3 bLocal;
                Vector3 cLocal;
                blockModel.GetVertex(t.I0, t.I1, t.I2, out aLocal, out bLocal, out cLocal);

                var a = Vector3D.Transform((Vector3D)aLocal, worldMatrix);
                var b = Vector3D.Transform((Vector3D)bLocal, worldMatrix);
                var c = Vector3D.Transform((Vector3D)cLocal, worldMatrix);

                var normal = Vector3D.Cross(b - a, c - a);
                if (Vector3D.Dot(normal, rayDirection) >= -1e-9)
                    continue;

                double distance;
                double u;
                double v;
                if (!TryIntersectTriangle(rayOrigin, rayDirection, a, b, c, out distance, out u, out v))
                    continue;
                if (distance > maxDistance)
                    continue;
                if (distance >= bestDistance)
                    continue;

                var w = 1d - u - v;
                if (!TryGetRuntimeTriangleUv(geometry, t.I0, t.I1, t.I2, w, u, v, out bestRawUv))
                {
                    var uvTriangle = geometry.UvTriangles[i];
                    bestRawUv = uvTriangle.A * (float)w + uvTriangle.B * (float)u + uvTriangle.C * (float)v;
                }

                bestDistance = distance;
                hit = true;
            }

            if (!hit)
                return false;

            rawUv = bestRawUv;
            uv = ToScreenUv(rawUv);
            hitDistance = bestDistance;
            return true;
        }

        static bool TryGetRuntimeTriangleUv(
            MinimalMwmScreenAreaGeometry geometry,
            int i0,
            int i1,
            int i2,
            double w,
            double u,
            double v,
            out Vector2 uv)
        {
            uv = Vector2.Zero;

            if (geometry == null || geometry.UvByVertexIndex == null)
                return false;

            Vector2 uv0;
            Vector2 uv1;
            Vector2 uv2;
            if (!geometry.UvByVertexIndex.TryGetValue(i0, out uv0) ||
                !geometry.UvByVertexIndex.TryGetValue(i1, out uv1) ||
                !geometry.UvByVertexIndex.TryGetValue(i2, out uv2))
                return false;

            uv = uv0 * (float)w + uv1 * (float)u + uv2 * (float)v;
            return true;
        }

        static Vector2 ToScreenUv(Vector2 rawUv)
        {
            return new Vector2(rawUv.X, -rawUv.Y);
        }


        static bool TryIntersectTriangle(
            Vector3D rayOrigin,
            Vector3D rayDirection,
            Vector3D a,
            Vector3D b,
            Vector3D c,
            out double distance,
            out double u,
            out double v)
        {
            distance = 0d;
            u = 0d;
            v = 0d;

            var edge1 = b - a;
            var edge2 = c - a;
            var p = Vector3D.Cross(rayDirection, edge2);
            var det = Vector3D.Dot(edge1, p);
            if (Math.Abs(det) < 1e-9)
                return false;

            var invDet = 1d / det;
            var t = rayOrigin - a;
            u = Vector3D.Dot(t, p) * invDet;
            if (u < 0d || u > 1d)
                return false;

            var q = Vector3D.Cross(t, edge1);
            v = Vector3D.Dot(rayDirection, q) * invDet;
            if (v < 0d || u + v > 1d)
                return false;

            distance = Vector3D.Dot(edge2, q) * invDet;
            return distance > 0d;
        }

        class CachedScreenArea
        {
            public MinimalMwmScreenAreaGeometry Geometry;
            public string LoadError;
        }
    }
}
