using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Utility;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Snapshots
{
    public sealed partial class ButtonTakeGridSnapshot : GridSnapshotButton
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ButtonTakeGridSnapshot()
        {
            var button = CreateControl<IMyTerminalControlButton>("TakeGridSnapshot");
            button.Action = block => Find<IGridSnapshotTaker>(block)?.TakeGridSnapshot();
            button.Enabled = block =>
            {
                IGridSnapshotTaker screen = Find<IGridSnapshotTaker>(block);
                return screen != null && screen.CanTakeGridSnapshot;
            };
            button.Visible = Visible;
            button.Title = MyStringId.GetOrCompute(MOD_PREFIX + "Integrity_Snapshot_Title");
            button.Tooltip = MyStringId.GetOrCompute(MOD_PREFIX + "Integrity_Snapshot_Tooltip");
            TerminalControl = button;
        }
    }
}
