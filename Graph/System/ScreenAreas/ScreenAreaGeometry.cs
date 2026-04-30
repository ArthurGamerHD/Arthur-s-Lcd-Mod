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
        static readonly Dictionary<string, CachedScreenArea> Cache =
            new Dictionary<string, CachedScreenArea>(StringComparer.OrdinalIgnoreCase);

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

        static Vector2 ToSurfacePoint(Sandbox.ModAPI.Ingame.IMyTextSurface surface, Vector2 uv)
        {
            if (surface == null)
                return Vector2.Zero;

            var offset = (surface.TextureSize - surface.SurfaceSize) * 0.5f;
            return new Vector2(
                offset.X + uv.X * surface.SurfaceSize.X,
                offset.Y + uv.Y * surface.SurfaceSize.Y);
        }

        static bool TryGetScreenAreaGeometry(SurfaceScriptBase screen, out MinimalMwmScreenAreaGeometry geometry)
        {
            geometry = null;
            if (screen == null || screen.Block == null)
                return false;

            var blockEntity = screen.Block as IMyEntity;
            if (blockEntity == null || blockEntity.Model == null)
            {
                LogHelper.LogOnce("skip:no-model:" + screen.Block.EntityId,
                    "block has no loaded model: " + screen.Description());

                return false;
            }

            var assetName = blockEntity.Model.AssetName;
            if (string.IsNullOrWhiteSpace(assetName))
            {
                LogHelper.LogOnce("skip:no-asset:" + screen.Block.EntityId,
                    "block model has empty AssetName: " + screen.Description());
                return false;
            }

            var materials = ResolveMaterialCandidates(screen);
            if (materials.Count == 0)
            {
                LogHelper.LogOnce("skip:no-materials:" + screen.Block.EntityId,
                    "no material candidates: " + screen.Description());
                return false;
            }

            LogHelper.LogOnce(
                "try:" + screen.Block.EntityId + ":" + screen.RotationOrSurfaceIndex + ":" + assetName,
                "trying " + screen.Description() + ", asset=" + assetName + ", materials=" +
                string.Join(", ", materials.ToArray()));

            for (int i = 0; i < materials.Count; i++)
            {
                if (TryGetScreenAreaGeometry(assetName, materials[i], out geometry))
                    return true;
            }

            LogHelper.LogOnce(
                "skip:no-match:" + screen.Block.EntityId + ":" + screen.RotationOrSurfaceIndex + ":" + assetName,
                "no matching screen geometry for " + screen.Description() +
                ", asset=" + assetName + ", materials=" + string.Join(", ", materials.ToArray()));
            return false;
        }

        static List<string> ResolveMaterialCandidates(SurfaceScriptBase screen)
        {
            var result = new List<string>();
            var definition = screen.Block.SlimBlock.BlockDefinition as MyFunctionalBlockDefinition;

            if (definition == null)
            {
                LogHelper.LogOnce("skip:invalid-definition:" + screen.Block.EntityId,
                    $"no matching MyFunctionalBlockDefinition for block {screen.Block.BlockDefinition}");
                return result;
            }

            LogHelper.LogOnce($"offset:{screen.Block.BlockDefinition}",
                $"{screen.Block.BlockDefinition} " +
                $"Offset: {definition.ModelOffset} " +
                $"Scale: {screen.Block.Model.ScaleFactor}x " +
                $"Size: {screen.Block.Model.BoundingBoxSize}");

            var surfaceIndex = screen.RotationOrSurfaceIndex;

            if (surfaceIndex >= definition.ScreenAreas.Count)
            {
                LogHelper.LogOnce("skip:index-out-of-range:" + screen.Block.EntityId,
                    $"no matching surface {surfaceIndex} for block {screen.Block.BlockDefinition}, (range from 0 to {definition.ScreenAreas.Count}");
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

            var lodPath = ToLod1ContentPath(assetName);
            if (string.IsNullOrWhiteSpace(lodPath))
            {
                cached.LoadError = "could not normalize asset name to _LOD1.mwm content path";
                LogHelper.LogOnce("path:invalid:" + cacheKey,
                    "invalid model path for " + cacheKey + ", asset=" + assetName);
                return false;
            }

            LogHelper.LogOnce("path:" + cacheKey, "normalized asset to LOD1 path: " + assetName + " -> " + lodPath);

            using (var reader = OpenMwm(lodPath))
            {
                if (reader == null)
                {
                    cached.LoadError = "LOD1 file not found in mod location or game content: " + lodPath;
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

        static BinaryReader OpenMwm(string contentPath)
        {
            var utilities = MyAPIGateway.Utilities;
            if (utilities == null || string.IsNullOrWhiteSpace(contentPath))
                return null;

            foreach (var mod in MyAPIGateway.Session.Mods.Where(mod =>
                         utilities.FileExistsInModLocation(contentPath, mod)))
            {
                LogHelper.LogOnce("file:mod:" + contentPath, "opening MWM from mod location: " + contentPath);
                return utilities.ReadBinaryFileInModLocation(contentPath, mod);
            }

            if (utilities.FileExistsInGameContent(contentPath))
            {
                LogHelper.LogOnce("file:game:" + contentPath, "opening MWM from game content: " + contentPath);
                return utilities.ReadBinaryFileInGameContent(contentPath);
            }

            return null;
        }

        static string ToLod1ContentPath(string assetName)
        {
            var path = assetName.Replace('\\', '/');
            const string contentMarker = "/Content/";
            var contentIndex = path.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
            if (contentIndex >= 0)
                path = path.Substring(contentIndex + contentMarker.Length);

            while (path.StartsWith("/", StringComparison.Ordinal))
                path = path.Substring(1);

            if (!path.EndsWith(".mwm", StringComparison.OrdinalIgnoreCase))
                return null;
            if (path.EndsWith("_LOD1.mwm", StringComparison.OrdinalIgnoreCase))
                return path;

            var extensionIndex = path.LastIndexOf(".mwm", StringComparison.OrdinalIgnoreCase);
            if (extensionIndex < 0)
                return null;

            return path.Substring(0, extensionIndex) + "_LOD1" + path.Substring(extensionIndex);
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