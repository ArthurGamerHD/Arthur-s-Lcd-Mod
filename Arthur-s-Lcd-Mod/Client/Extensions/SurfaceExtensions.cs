using LcdMod.Client.Apps.Abstract;

namespace LcdMod.Client.Extensions
{
    public static class SurfaceScriptBaseExtensions
    {
        public static string Description(this SurfaceScriptBase screen)
        {
            if (screen == null || screen.Block == null)
                return "<null>";

            var subtype = screen.Block.BlockDefinition.SubtypeName;
            if (string.IsNullOrWhiteSpace(subtype))
                subtype = "<no subtype>";

            return "block=" + subtype +
                   ", entityId=" + screen.Block.EntityId +
                   ", surfaceIndex=" + screen.RotationOrSurfaceIndex;
        }
    }
}