using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Markdown
{
    public sealed partial class ButtonEditMarkdown : TerminalControlsWrapper
    {
        const string TITLE_ID = "BlockPropertyTitle_TextPanelShowTextPanel";

        public override IMyTerminalControl TerminalControl { get; }

        public ButtonEditMarkdown()
        {
            var button = CreateControl<IMyTerminalControlButton>("MarkdownTextInput");
            button.Action = EditText;
            button.Visible = Visible;
            button.Title = MyStringId.GetOrCompute(TITLE_ID);

            TerminalControl = button;
        }

        static void EditText(IMyTerminalBlock block)
        {
            var config = GetConfig(block);
            if (config == null)
                return;

            var oldText = config.RawText ?? string.Empty;
            TextInputHelper.SpawnForLocalPlayer(
                MyTexts.GetString(TITLE_ID),
                text => ApplyText(block, oldText, text),
                oldText,
                block != null ? block.CustomName : string.Empty);
        }

        static void ApplyText(IMyTerminalBlock block, string oldText, string text)
        {
            if (block == null)
                return;

            text = text ?? string.Empty;
            if (text == (oldText ?? string.Empty))
                return;

            ConfigManager.ModifyComponentForTerminalApp<MarkdownConfigComponent>(
                block,
                config => config.RawText = text);
        }

        static MarkdownConfigComponent GetConfig(IMyTerminalBlock block)
        {
            return ConfigManager.GetComponentForTerminalApp<MarkdownConfigComponent>(block);
        }
    }
}
