using System;
using System.Collections.Generic;
using System.Globalization;
using LcdMod.Common.Networking;
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

        public static void SetColor(string[] obj)
        {
            Vector3 factionColor;
            string error;
            if (!TryParseFactionColor(obj, out factionColor, out error))
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", LocHelper.GetLoc(error));
                return;
            }

            var faction = GetPlayerFaction(MyAPIGateway.Session.LocalHumanPlayer.IdentityId);
            if (faction == null)
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", LocHelper.GetLoc("LcdMod_FactionColor_NoFaction"));
                return;
            }

            var packet = new PacketEditFaction(
                faction.FactionId,
                faction.Tag,
                faction.Name,
                faction.Description,
                faction.PrivateInfo,
                faction.FactionIcon.ToString(),
                faction.CustomColor, 
                factionColor);
            
            if(MyAPIGateway.Session.IsServer)
                Common.Helpers.FactionHelperCommon.EditFaction(packet);
            else
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, false);

            MyAPIGateway.Utilities.ShowMessage("lcdMod", LocHelper.GetLoc("LcdMod_FactionColor_Updated"));
        }

        static bool TryParseFactionColor(string[] args, out Vector3 factionColor, out string error)
        {
            factionColor = Vector3.Zero;
            var tokens = NormalizeArgs(args);
            if (tokens.Length == 0)
            {
                error = "LcdMod_FactionColor_Usage";
                return false;
            }

            if (tokens.Length == 1 && Extensions.ColorExtensions.TryParseHexFactionColor(tokens[0], out factionColor))
            {
                error = null;
                return true;
            }

            if (tokens.Length == 4 && tokens[0] == "hsv")
            {
                float h;
                float s;
                float v;
                if (!float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out h) ||
                    !float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out s) ||
                    !float.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    error = "LcdMod_FactionColor_HsvFormat";
                    return false;
                }

                if (h > 1f && h <= 360f)
                    h /= 360f;

                if (h < 0f || h > 1f || s < 0f || s > 1f || v < 0f || v > 1f)
                {
                    error = "LcdMod_FactionColor_HsvRange";
                    return false;
                }

                factionColor = MyColorPickerConstants.HSVToHSVOffset(new Vector3(h, s, v));
                error = null;
                return true;
            }

            if (tokens.Length == 3)
            {
                int r;
                int g;
                int b;
                if (int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out r) &&
                    int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out g) &&
                    int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out b) &&
                    r >= 0 && r <= 255 &&
                    g >= 0 && g <= 255 &&
                    b >= 0 && b <= 255)
                {
                    factionColor = Extensions.ColorExtensions.ToFactionColor(new Color((byte)r, (byte)g, (byte)b));
                    error = null;
                    return true;
                }
            }

            error = "LcdMod_FactionColor_Invalid";
            return false;
        }

        static string[] NormalizeArgs(string[] args)
        {
            if (args == null || args.Length == 0)
                return new string[0];

            var tokens = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(args[i]))
                    continue;

                var parts = args[i].Split(new[] { ' ', ',', ';', '|', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int j = 0; j < parts.Length; j++)
                    tokens.Add(parts[j]);
            }

            return tokens.ToArray();
        }
    }
}
