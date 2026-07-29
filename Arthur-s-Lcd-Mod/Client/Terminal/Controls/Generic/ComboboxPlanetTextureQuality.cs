using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.SurfaceScripts;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    /// <summary>
    /// Client-local planet texture quality shared by the Planetary Map and Star Map.
    /// The selected value is stored only in the local player's config.
    /// </summary>
    public sealed class ComboboxPlanetTextureQuality : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxPlanetTextureQuality()
        {
            var combo = CreateControl<IMyTerminalControlCombobox>("PlanetTextureQuality");
            combo.Getter = Getter;
            combo.Setter = Setter;
            combo.ComboBoxContent = Content;
            combo.Visible = Visible;
            combo.Title = MyStringId.GetOrCompute("Texture Quality");
            combo.Tooltip = MyStringId.GetOrCompute(
                "Client-local limit for planet texture rectangles. Ultra uses the current maximum.");
            TerminalControl = combo;
        }

        static void Content(List<MyTerminalControlComboBoxItem> items)
        {
            for (int i = 0; i < PlanetTextureQualitySettings.Options.Length; i++)
            {
                PlanetTextureQuality quality = PlanetTextureQualitySettings.Options[i];
                items.Add(new MyTerminalControlComboBoxItem
                {
                    Key = (long)quality,
                    Value = MyStringId.GetOrCompute(PlanetTextureQualitySettings.GetLabel(quality))
                });
            }
        }

        static long Getter(IMyTerminalBlock block)
        {
            return (long)LocalConfigManager.TextureQuality;
        }

        static void Setter(IMyTerminalBlock block, long value)
        {
            try
            {
                LocalConfigManager.SetTextureQuality((PlanetTextureQuality)value);
            }
            catch
            {
                if (block != null)
                    block.RefreshTerminal();
            }
        }

        public override bool VisibleForScript(string script)
        {
            return script == StarMapSurfaceScript.ID ||
                   script == PlanetaryMapSurfaceScript.ID;
        }
    }
}
