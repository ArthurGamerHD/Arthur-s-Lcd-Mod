using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.SurfaceScripts.Abstract;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Client.ScreenAreas
{
    /// <summary>
    /// Caches that keep pointer ray tests cheap: pointer tracking tests every nearby screen every frame, so resolving the
    /// screen's geometry and reading its triangles from the model each time cost about 0.3 ms per screen.
    /// </summary>
    public static partial class ScreenAreaGeometry
    {
        const float BOUNDING_SPHERE_MARGIN = 1.01f;

        sealed class ResolvedScreen
        {
            public IMyModel Model;
            public MinimalMwmScreenAreaGeometry Geometry;
        }

        /// <summary>A screen area's triangles in model space, with a bounding sphere for a cheap ray rejection.</summary>
        sealed class LocalScreenMesh
        {
            public Vector3[] A;
            public Vector3[] B;
            public Vector3[] C;
            public int[] I0;
            public int[] I1;
            public int[] I2;
            public Vector3 Center;
            public float Radius;
        }

        static readonly Dictionary<long, Dictionary<int, ResolvedScreen>> ResolvedScreens =
            new Dictionary<long, Dictionary<int, ResolvedScreen>>();
        static readonly Dictionary<MinimalMwmScreenAreaGeometry, LocalScreenMesh> LocalMeshes =
            new Dictionary<MinimalMwmScreenAreaGeometry, LocalScreenMesh>();

        /// <summary>The screen's geometry, resolved once per block surface and again only when the block's model changes.</summary>
        static bool TryGetCachedScreenAreaGeometry(SurfaceScriptBase screen, out MinimalMwmScreenAreaGeometry geometry)
        {
            geometry = null;
            if (screen == null || screen.Block == null)
                return false;

            Dictionary<int, ResolvedScreen> surfaces;
            if (!ResolvedScreens.TryGetValue(screen.Block.EntityId, out surfaces))
                ResolvedScreens[screen.Block.EntityId] = surfaces = new Dictionary<int, ResolvedScreen>();
            IMyModel model = ((IMyEntity)screen.Block).Model;
            ResolvedScreen resolved;
            if (surfaces.TryGetValue(screen.RotationOrSurfaceIndex, out resolved) && ReferenceEquals(resolved.Model, model))
            {
                geometry = resolved.Geometry;
                return geometry != null;
            }

            bool found = TryGetScreenAreaGeometry(screen.Block, screen.RotationOrSurfaceIndex, screen.Description(),
                out geometry);
            surfaces[screen.RotationOrSurfaceIndex] = new ResolvedScreen { Model = model, Geometry = found ? geometry : null };
            return found;
        }

        static LocalScreenMesh GetLocalMesh(IMyModel blockModel, MinimalMwmScreenAreaGeometry geometry)
        {
            LocalScreenMesh mesh;
            if (LocalMeshes.TryGetValue(geometry, out mesh))
                return mesh;

            int count = Math.Min(geometry.TriangleIndices.Count, geometry.UvTriangles.Count);
            mesh = new LocalScreenMesh
            {
                A = new Vector3[count],
                B = new Vector3[count],
                C = new Vector3[count],
                I0 = new int[count],
                I1 = new int[count],
                I2 = new int[count]
            };
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < count; i++)
            {
                var triangle = blockModel.GetTriangle(geometry.TriangleIndices[i]);
                blockModel.GetVertex(triangle.I0, triangle.I1, triangle.I2, out mesh.A[i], out mesh.B[i], out mesh.C[i]);
                mesh.I0[i] = triangle.I0;
                mesh.I1[i] = triangle.I1;
                mesh.I2[i] = triangle.I2;
                min = Vector3.Min(min, Vector3.Min(mesh.A[i], Vector3.Min(mesh.B[i], mesh.C[i])));
                max = Vector3.Max(max, Vector3.Max(mesh.A[i], Vector3.Max(mesh.B[i], mesh.C[i])));
            }

            if (count > 0)
            {
                mesh.Center = (min + max) * 0.5f;
                float radiusSq = 0f;
                for (int i = 0; i < count; i++)
                {
                    radiusSq = Math.Max(radiusSq, Vector3.DistanceSquared(mesh.Center, mesh.A[i]));
                    radiusSq = Math.Max(radiusSq, Vector3.DistanceSquared(mesh.Center, mesh.B[i]));
                    radiusSq = Math.Max(radiusSq, Vector3.DistanceSquared(mesh.Center, mesh.C[i]));
                }
                mesh.Radius = (float)Math.Sqrt(radiusSq) * BOUNDING_SPHERE_MARGIN;
            }

            LocalMeshes[geometry] = mesh;
            return mesh;
        }

        /// <summary>False when the ray cannot touch the mesh's bounding sphere; blocks are rigid, so the radius holds.</summary>
        static bool RayMayHit(LocalScreenMesh mesh, MatrixD worldMatrix, Vector3D rayOrigin, Vector3D rayDirection,
            double maxDistance)
        {
            double directionLength = rayDirection.Length();
            if (directionLength <= 1e-9)
                return false;

            Vector3D center = Vector3D.Transform((Vector3D)mesh.Center, worldMatrix);
            Vector3D toCenter = center - rayOrigin;
            double along = Vector3D.Dot(toCenter, rayDirection) / directionLength;
            double radius = mesh.Radius;
            if (along < -radius || along - radius > maxDistance)
                return false;
            double offAxisSq = toCenter.LengthSquared() - along * along;
            return offAxisSq <= radius * radius;
        }
    }
}
