using LcdMod.Common.Networking;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Common.Helpers
{
    public static class FactionHelperCommon
    {
        public static Color DefaultColor => new Color(58, 32, 63);

        public static IMyFaction GetOwnerFaction(IMyTerminalBlock block)
        {
            return MyAPIGateway.Session.Factions.TryGetPlayerFaction(block?.OwnerId ?? 0);
        }

        public static Color GetIconColor(IMyFaction faction)
        {
            if (faction == null)
                return DefaultColor;

            return MyColorPickerConstants.HSVOffsetToHSV(faction.IconColor).HSVtoColor();
        }

        public static Color GetCustomColor(IMyFaction faction)
        {
            if (faction == null)
                return Color.White;

            return MyColorPickerConstants.HSVOffsetToHSV(faction.CustomColor).HSVtoColor();
        }

        public static Color GetAccent(IMyTerminalBlock block)
        {
            if (block == null)
                return DefaultColor;
            
            var icon = GetIconColor(GetOwnerFaction(block));

            if (!(icon.ColorToHSV().Y <= 0.01)) return icon;
            var background = GetCustomColor(GetOwnerFaction(block));
            return background.ColorToHSV().Y > 0.01 ? background : icon;
        }


        public static Color GetIconColor(IMyTerminalBlock block)
        {
            return block != null ? GetIconColor(GetOwnerFaction(block)) : DefaultColor;
        }

        public static void EditFaction(PacketEditFaction faction)
        {
            MyAPIGateway.Session.Factions.EditFaction(faction.FactionId,
                faction.Tag,
                faction.Name,
                faction.Description,
                faction.PrivateInfo,
                faction.Icon,
                faction.Color,
                faction.IconColor);
        }
    }
}