using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.SurfaceScripts.Abstract;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace LcdMod.Client.ScreenAreas
{
    // Resolving a screen's geometry reads the block's model, so it is resolved once per block surface.
    public static partial class ScreenAreaGeometry
    {
        sealed class ResolvedScreen
        {
            public IMyModel Model;
            public MinimalMwmScreenAreaGeometry Geometry;
        }

        static readonly Dictionary<long, Dictionary<int, ResolvedScreen>> ResolvedScreens =
            new Dictionary<long, Dictionary<int, ResolvedScreen>>();

        /// <summary>Drops a closed block's resolved screens, so closed blocks do not stay in the cache.</summary>
        public static void ForgetBlock(long blockEntityId)
        {
            ResolvedScreens.Remove(blockEntityId);
        }

        // The screen's geometry, resolved once per block surface and again only when the block's model changes.
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

    }
}
