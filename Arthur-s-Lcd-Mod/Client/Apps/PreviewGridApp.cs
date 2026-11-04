using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.IntegrityMonitor;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Snapshots;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using System;

namespace LcdMod.Client.Apps
{
    // The screen's own grid compared with a projector's projection or, without one, with a snapshot of the grid the
    // screen took earlier (kept relative to the screen's block, so it still lines up after the grid changed).
    [LcdApp(10)]
    [ConfigComponent(APP, typeof(DiagnosticConfigComponent), PropertyName = "DiagnosticComponent")]
    [ConfigComponent(PROJECTOR_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "ProjectorReferenceComponent")]
    internal sealed partial class PreviewGridApp : GridInspectorApp
    {
        public const string TITLE = "BroadcastStatus_IsPreviewGrid";
        const string SNAPSHOT_PURPOSE = "integrity";

        readonly GridSnapshotSlot _snapshot;
        IMyProjector _projector;

        protected override string DefaultTitle => TITLE;

        public PreviewGridApp(IAppHost host) : base(host)
        {
            _snapshot = new GridSnapshotSlot(GridSnapshotStorage.KeyFor(host.SurfaceIndex, SNAPSHOT_PURPOSE));
            ViewModel.SnapshotRequested += TakeSnapshot;
        }

        public override void Close()
        {
            ViewModel.SnapshotRequested -= TakeSnapshot;
            base.Close();
        }

        public bool CanTakeSnapshot => Block != null && Block.CubeGrid != null && !_snapshot.IsBusy;
        public bool HasSnapshot => _snapshot.Snapshot != null;

        // Of the screen's grid, kept relative to the screen's block.
        public void TakeSnapshot()
        {
            if (CanTakeSnapshot)
                _snapshot.Capture(Block.CubeGrid, Block.EntityId, true);
        }

        public void ClearSnapshot()
        {
            _snapshot.Set(Block, null, true);
        }

        protected override bool UpdateSources()
        {
            _snapshot.Update(Block);
            bool projectorSelected = ProjectorReferenceComponent.EntityId != 0;
            ViewModel.SetSnapshotButton(!projectorSelected, CanTakeSnapshot);
            FindProjector(Block?.CubeGrid, ref _projector);
            if (_projector != null && _projector.IsFunctional && !_projector.Closed && _projector.Enabled &&
                _projector.ProjectedGrid != null && _projector.CubeGrid != null)
            {
                SetTitle(_projector.ProjectedGrid.CustomName);
                ViewModel.SetSideWarning(null);
                ViewModel.ShowGrids(_projector.CubeGrid.EntityId, _projector.ProjectedGrid.EntityId);
                return true;
            }

            IMyCubeGrid grid = Block?.CubeGrid;
            if (grid == null)
            {
                ShowWithoutMap(LocHelper.Empty, "Warning", ColorComponent.ResolveWarningColor());
                return false;
            }
            // While the projector cannot be compared with, the snapshot stands in; the note says why.
            string warning = projectorSelected ? ProjectorProblem() : LocHelper.GetLoc(MOD_PREFIX + "Integrity_NoProjector");
            if (_snapshot.Snapshot != null && ViewModel.ReferenceMismatch)
                warning = LocHelper.GetLoc(MOD_PREFIX + "Integrity_SnapshotMismatch");
            else if (_snapshot.Error != null)
                warning = _snapshot.Error;
            SetTitle(grid.CustomName);
            ViewModel.SetSideWarning(warning);
            ViewModel.ShowGrids(grid.EntityId, 0, null, _snapshot.Snapshot);
            return true;
        }

        string ProjectorProblem()
        {
            string projector = LocHelper.GetLoc("DisplayName_Block_Projector");
            if (_projector == null || _projector.Closed)
                return string.Format(LocHelper.GetLoc("SignalConnectivity_State_NoLaserLink"), projector);
            if (!_projector.IsFunctional)
                return string.Format(LocHelper.GetLoc("SignalConnectivity_State_NotOperational"), projector);
            if (!_projector.Enabled)
                return (_projector.CustomName ?? string.Empty) + " " + LocHelper.GetLoc("AssemblerState_Disabled");
            return LocHelper.GetLoc("ScreenBlueprintsRew_NoBlueprints");
        }

        void FindProjector(IMyCubeGrid grid, ref IMyProjector projector)
        {
            if (ProjectorReferenceComponent.EntityId == 0)
            {
                projector = null;
                return;
            }

            if (projector != null && projector.EntityId == ProjectorReferenceComponent.EntityId)
                return;

            var entity = MyAPIGateway.Entities.GetEntityById(ProjectorReferenceComponent.EntityId) as IMyProjector;
            projector = entity?.CubeGrid.IsInSameLogicalGroupAs(grid) ?? false ? entity : null;
        }
    }

    public struct DepthMap2D
    {
        public float?[,] Percent;
        public bool[,] Damaged;
        public bool[,] Missing;
        public CellKind[,] CellType;
        public int Width;
        public int Height;
        public int UMin;
        public int VMin;
    }

    public struct ViewAxes
    {
        public Func<Vector3I, int> U; // 2D X
        public Func<Vector3I, int> V; // 2D Y
        public Func<Vector3I, int> D; // depth, normalized later
    }


    // The same values as Acrux.Render3D.GridBlockCategory: the X-ray bits mean the same in 2D and 3D.
    public enum CellKind
    {
        None,
        Thruster,
        Power,
        Gyro,
        Weapons,
        Gravity,
        Production,
        Cargo,
        Conveyor
    }

    
    public enum View
    {
        Xpos = 0,
        Xneg = 1,
        Ypos = 2,
        Yneg = 3,
        Zpos = 4,
        Zneg = 5,
    }
}
