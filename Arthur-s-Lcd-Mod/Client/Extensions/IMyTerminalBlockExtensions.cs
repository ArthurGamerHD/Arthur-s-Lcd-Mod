using Sandbox.ModAPI;

namespace LcdMod.Client.Extensions
{
    public static class IMyTerminalBlockExtensions
    {
        public static void RefreshTerminal(this IMyTerminalBlock block)
        {
            var old = block.ShowInToolbarConfig;
            block.ShowInToolbarConfig = !old;
            block.ShowInToolbarConfig = old;
        }
    }
}