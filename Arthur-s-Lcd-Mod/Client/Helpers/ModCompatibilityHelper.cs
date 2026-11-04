using VRage.Game.ModAPI;

namespace LcdMod.Client.Helpers
{
    /// <summary>
    /// Blocks from other mods that need special handling.
    /// </summary>
    internal static class ModCompatibilityHelper
    {
        // Wrist pad: a regular text panel (with terminal controls) on a physics-less grid.
        public const string WRIST_PAD_PANEL = "WristpadPanel";

        // True for screens on physics-less grids that are real, usable screens rather than projections or holograms.
        public static bool IsUsablePhysicslessScreen(IMyCubeBlock block) => IsWristPad(block);

        static bool IsWristPad(IMyCubeBlock block) => block != null && block.BlockDefinition.SubtypeId == WRIST_PAD_PANEL;
    }
}
