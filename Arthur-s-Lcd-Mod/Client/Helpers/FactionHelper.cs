using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Helpers
{
    public static class FactionHelper
    {
        const string DEFAULT_ICON = "Textures\\FactionLogo\\Others\\OtherIcon_18.dds";
        public static Color DefaultColor => new Color(58, 32, 63);
        public static Color DefaultBackgroundColor => Color.Black;

        public static IMyFaction GetOwnerFaction(IMyTerminalBlock block) => GetPlayerFaction(block?.OwnerId ?? 0);

        public static IMyFaction GetPlayerFaction(long identityId) =>
            MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityId);

        public static string GetIcon(IMyTerminalBlock block) =>
            block != null ? GetIcon(GetOwnerFaction(block)) : DEFAULT_ICON;

        public static string GetIcon(IMyFaction faction) => faction?.FactionIcon?.ToString() ?? DEFAULT_ICON;

        public static Color GetIconColor(IMyFaction faction)
        {
            if (faction?.IconColor == null)
                return DefaultColor;

            var color = MyColorPickerConstants.HSVOffsetToHSV(faction.IconColor).HSVtoColor();
            return color;
        }

        public static Color GetBackgroundColor(IMyFaction faction)
        {
            if (faction?.CustomColor == null)
                return DefaultBackgroundColor;

            var color = MyColorPickerConstants.HSVOffsetToHSV(faction.CustomColor).HSVtoColor();
            return color;
        }

        public static Color GetIconColor(IMyTerminalBlock block) => block != null ? GetIconColor(GetOwnerFaction(block)) : DefaultColor;
    }
}
