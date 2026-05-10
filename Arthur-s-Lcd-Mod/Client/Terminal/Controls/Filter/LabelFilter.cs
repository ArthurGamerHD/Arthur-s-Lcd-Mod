using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Filter
{
    public sealed partial class LabelSeparator : TerminalControlFilter
    {
        public override IMyTerminalControl TerminalControl { get; }

        public LabelSeparator()
        {
            var label = CreateControl<IMyTerminalControlLabel>("ChartFilterLabel");
            label.Visible = Visible;
            label.Label = MyStringId.GetOrCompute("ScenarioSelectionScreen_Filter");
            TerminalControl = label;
        }
    }
}
