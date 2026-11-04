using Generated;

namespace LcdMod.Client.Terminal.Controls.Snapshots
{
    // A screen keeping grid snapshots (Integrity Monitor, Target Info), for the terminal's snapshot buttons.
    public interface IGridSnapshotScreen : IUsesTerminalControl<ButtonClearGridSnapshot>
    {
        bool HasGridSnapshot { get; }
        void ClearGridSnapshot();
    }

    // A screen that can take a snapshot of its own grid.
    public interface IGridSnapshotTaker : IGridSnapshotScreen, IUsesTerminalControl<ButtonTakeGridSnapshot>
    {
        bool CanTakeGridSnapshot { get; }
        void TakeGridSnapshot();
    }
}
