using System;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed class ButtonPasteSettings : TerminalControlsWrapper
    {
        readonly SettingsClipboard _clipboard;

        protected override bool RequiresAdvancedTweakables => false;

        public override IMyTerminalControl TerminalControl { get; }

        public ButtonPasteSettings(SettingsClipboard clipboard)
        {
            _clipboard = clipboard;

            var button = CreateControl<IMyTerminalControlButton>("PasteSettings");
            button.Action = Paste;
            button.Enabled = Enabled;
            button.Visible = Visible;
            button.Title = MyStringId.GetOrCompute("Paste settings");
            button.Tooltip = MyStringId.GetOrCompute(
                "Paste compatible LCD Mod settings and text-surface display properties without changing the destination app type.");
            button.SupportsMultipleBlocks = false;

            TerminalControl = button;
        }

        public override bool VisibleForScript(string script)
        {
            return true;
        }

        bool Enabled(IMyTerminalBlock block)
        {
            return _clipboard.HasSettings;
        }

        void Paste(IMyTerminalBlock block)
        {
            try
            {
                if (!_clipboard.Paste(block))
                {
                    MyAPIGateway.Utilities.ShowMessage("lcdMod", "No copied settings are available for this surface.");
                    return;
                }

                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Compatible settings pasted.");
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Failed to paste settings: " + e.Message);
                LogHelper.Log(MyLogSeverity.Error, e.ToString());
            }
        }
    }
}
