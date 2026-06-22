using System;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed class ButtonShowConfig : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }
        protected override bool RequiresAdvancedTweakables => true;

        public ButtonShowConfig()
        {
            var button = CreateControl<IMyTerminalControlButton>("ShowConfig");
            button.Action = ShowConfig;
            button.Visible = Visible;
            button.Title = MyStringId.GetOrCompute("Show Config XML");
            button.Tooltip = MyStringId.GetOrCompute("Open this block's LCD Mod config as XML.");

            TerminalControl = button;
        }

        public override bool VisibleForScript(string script)
        {
            return true;
        }

        static void ShowConfig(IMyTerminalBlock block)
        {
            try
            {
                var config = GetOrCreateConfig(block);
                if (config == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("lcdMod", "No LCD Mod config found for this block.");
                    return;
                }

                // The inherited Screens collection is only a runtime compatibility facade.
                // Capture it into the component graph before producing the debug XML.
                config.CaptureRuntimeScreens();
                var text = MyAPIGateway.Utilities.SerializeToXML(config);
                TextInputHelper.SpawnForLocalPlayer(
                    "LCD Mod Block Config",
                    xml => ApplyConfig(block, xml, text),
                    text,
                    block.CustomName);
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Invalid config XML: " + e.Message);
                LogHelper.Log(MyLogSeverity.Error, e.ToString());
            }

        }

        static ScreenProviderConfig GetOrCreateConfig(IMyTerminalBlock block)
        {
            if (block == null)
                return null;

            var config = ConfigManager.GetConfigForBlock(block) ?? ConfigManager.TryLoad(block);
            if (config != null)
                return config;

            return ConfigManager.CreateSettings(block);
        }

        static void ApplyConfig(IMyTerminalBlock block, string xml, string oldxml)
        {
            try
            {
                if(xml.Equals(oldxml))
                    return;
                
                var config = MyAPIGateway.Utilities.SerializeFromXML<ScreenProviderConfig>(xml);
                if (config == null)
                    throw new Exception("Empty config.");

                config.BindRuntimeParent(block);
                ConfigManager.Sync(block, config);
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Block config updated.");
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Invalid block config XML: " + e.Message);
                LogHelper.Log(MyLogSeverity.Error, e.ToString());
            }
        }
    }
}
