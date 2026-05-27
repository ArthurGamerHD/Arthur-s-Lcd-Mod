using Sandbox.Game.Entities;

namespace LcdMod.Client.Helpers
{
    public static class LocalPlayerBlockStateHelper
    {
        public static bool IsBlockPlacerActive()
        {
            var cubeBuilder = MyCubeBuilder.Static;
            return cubeBuilder != null &&
                   cubeBuilder.IsActivated &&
                   cubeBuilder.IsBuildToolActive();
        }
    }
}
