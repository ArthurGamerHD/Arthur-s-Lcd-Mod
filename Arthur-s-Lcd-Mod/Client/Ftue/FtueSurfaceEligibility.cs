using System;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.ModAPI;

namespace LcdMod.Client.Ftue
{
    internal static class FtueSurfaceEligibility
    {
        const float MIN_VISIBLE_DIMENSION = 256f;
        const float MAX_ASPECT_RATIO = 3f;

        public static bool IsEligible(InteractiveSurfaceScript surface)
        {
            return IsSurfaceEligible(surface) && surface.RequiresAlt;
        }

        public static bool IsSurfaceEligible(InteractiveSurfaceScript surface)
        {
            var player = MyAPIGateway.Session?.LocalHumanPlayer;
            if (surface == null || surface.Block == null || player == null)
                return false;

            var terminalBlock = surface.Block as IMyTerminalBlock;
            if (terminalBlock == null || terminalBlock.OwnerId != player.IdentityId)
                return false;

            var size = surface.Surface.SurfaceSize;
            float shortSide = Math.Min(size.X, size.Y);
            float longSide = Math.Max(size.X, size.Y);

            if (shortSide < MIN_VISIBLE_DIMENSION)
                return false;

            return shortSide > 0f && longSide / shortSide <= MAX_ASPECT_RATIO;
        }
    }
}
