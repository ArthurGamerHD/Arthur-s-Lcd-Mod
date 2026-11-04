using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Snapshots
{
    public sealed partial class ButtonClearGridSnapshot : GridSnapshotButton
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ButtonClearGridSnapshot()
        {
            var button = CreateControl<IMyTerminalControlButton>("ClearGridSnapshot");
            button.Action = block => Find<IGridSnapshotScreen>(block)?.ClearGridSnapshot();
            button.Enabled = block =>
            {
                IGridSnapshotScreen screen = Find<IGridSnapshotScreen>(block);
                return screen != null && screen.HasGridSnapshot;
            };
            button.Visible = Visible;
            button.Title = MyStringId.GetOrCompute(MOD_PREFIX + "Integrity_ClearSnapshot_Title");
            button.Tooltip = MyStringId.GetOrCompute(MOD_PREFIX + "Integrity_ClearSnapshot_Tooltip");
            TerminalControl = button;
        }
    }
}
