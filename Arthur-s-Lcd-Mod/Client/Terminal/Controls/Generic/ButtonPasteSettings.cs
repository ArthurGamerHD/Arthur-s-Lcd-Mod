using System;
using LcdMod.Client.Helpers;
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
            button.Title = MyStringId.GetOrCompute(Constants.MOD_PREFIX + "SettingsClipboard_Paste");
            button.Tooltip = MyStringId.GetOrCompute(Constants.MOD_PREFIX + "SettingsClipboard_PasteTooltip");
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
                    MyAPIGateway.Utilities.ShowMessage("lcdMod",
                        LocHelper.GetLoc(Constants.MOD_PREFIX + "SettingsClipboard_NoCopiedSettings"));
                    return;
                }

                MyAPIGateway.Utilities.ShowMessage("lcdMod",
                    LocHelper.GetLoc(Constants.MOD_PREFIX + "SettingsClipboard_Pasted"));
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", string.Format(
                    FormatingHelper.Culture,
                    LocHelper.GetLoc(Constants.MOD_PREFIX + "SettingsClipboard_PasteFailedFormat"),
                    e.Message));
                LogHelper.Log(MyLogSeverity.Error, e.ToString());
            }
        }
    }
}
