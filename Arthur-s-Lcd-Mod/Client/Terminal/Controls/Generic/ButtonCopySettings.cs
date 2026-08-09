using System;
using LcdMod.Client.Helpers;
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

        protected override bool RequiresAdvancedTweakables => false;

        public override IMyTerminalControl TerminalControl { get; }

        public ButtonCopySettings(SettingsClipboard clipboard, ButtonPasteSettings pasteButton)
        {
            _clipboard = clipboard;
            _pasteButton = pasteButton;

            var button = CreateControl<IMyTerminalControlButton>("CopySettings");
            button.Action = Copy;
            button.Visible = Visible;
            button.Title = MyStringId.GetOrCompute(Constants.MOD_PREFIX + "SettingsClipboard_Copy");
            button.Tooltip = MyStringId.GetOrCompute(Constants.MOD_PREFIX + "SettingsClipboard_CopyTooltip");
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
                    MyAPIGateway.Utilities.ShowMessage("lcdMod",
                        LocHelper.GetLoc(Constants.MOD_PREFIX + "SettingsClipboard_NoSettings"));
                    return;
                }

                _pasteButton.TerminalControl.UpdateVisual();
                MyAPIGateway.Utilities.ShowMessage("lcdMod",
                    LocHelper.GetLoc(Constants.MOD_PREFIX + "SettingsClipboard_Copied"));
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", string.Format(
                    FormatingHelper.Culture,
                    LocHelper.GetLoc(Constants.MOD_PREFIX + "SettingsClipboard_CopyFailedFormat"),
                    e.Message));
                LogHelper.Log(MyLogSeverity.Error, e.ToString());
            }
        }
    }
}
