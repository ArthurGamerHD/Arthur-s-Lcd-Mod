namespace LcdMod.Client.Utility
{
    /// <summary>
    /// Surface scripts that should suppress primary and secondary tool input while the player is looking at them.
    /// </summary>
    public interface IInputBlock
    {
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; }
        VRage.Game.ModAPI.Ingame.IMyCubeBlock Block { get; }
        int RotationOrSurfaceIndex { get; }
        long LastRunTick { get; }
    }
}
