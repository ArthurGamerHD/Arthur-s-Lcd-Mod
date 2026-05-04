using Sandbox.ModAPI.Interfaces.Terminal;

namespace Graph.System.TerminalControls.Filter
{
    public partial class SeparatorFilter : TerminalControlFilter
    {
        public override IMyTerminalControl TerminalControl { get; }
        
        public SeparatorFilter()
        {
            var separator = CreateControl<IMyTerminalControlSeparator>("ChartFilterSeparator");
            separator.Visible = Visible;
            TerminalControl = separator;
        }
    }
}
