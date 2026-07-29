using System;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed class ButtonCopySettings : TerminalControlsWrapper
    {
        readonly SettingsClipboard _clipboard;
        readonly ButtonPasteSettings _pasteButton;

        public override IMyTerminalControl TerminalControl { get; }

        public ButtonCopySettings(SettingsClipboard clipboard, ButtonPasteSettings pasteButton)
        {
            _clipboard = clipboard;
            _pasteButton = pasteButton;

            var button = CreateControl<IMyTerminalControlButton>("CopySettings");
            button.Action = Copy;
            button.Visible = Visible;
            button.Title = MyStringId.GetOrCompute("Copy settings");
            button.Tooltip = MyStringId.GetOrCompute(
                "Copy the selected LCD Mod settings and text-surface display properties into a temporary client-side clipboard.");
            button.SupportsMultipleBlocks = false;

            TerminalControl = button;
        }

        public override bool VisibleForScript(string script)
        {
            return true;
        }

        void Copy(IMyTerminalBlock block)
        {
            try
            {
                if (!_clipboard.Copy(block))
                {
                    MyAPIGateway.Utilities.ShowMessage("lcdMod", "No LCD Mod settings found for this surface.");
                    return;
                }

                _pasteButton.TerminalControl.UpdateVisual();
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Settings copied.");
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Failed to copy settings: " + e.Message);
                LogHelper.Log(MyLogSeverity.Error, e.ToString());
            }
        }
    }
}
