using System;
using System.Collections.Generic;
using Acrux.Render3D;
using LcdMod.Client.Apps.Abstract;
using Acrux.Controls;
using LcdMod.Client.Apps.IntegrityMonitor;
using LcdMod.Client.Apps.SelfDiagnostic;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Snapshots;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps
{
    // The screen's own ship in combat: compared with a snapshot of it taken when whole (a projector stands in until
    // there is one), the blocks hit in the last seconds flash, and missiles closing in are marked with where they go.
    // Building (what a projection still needs) stays with the Integrity Monitor.
    [LcdApp(32)]
    [ConfigComponent(APP, typeof(DiagnosticConfigComponent), PropertyName = "DiagnosticComponent")]
    [ConfigComponent(PROJECTOR_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "ProjectorReferenceComponent")]
    internal sealed partial class SelfDiagnosticApp : GridInspectorApp
    {
        public const string TITLE = MOD_PREFIX + "SelfDiagnostic";
        const string SNAPSHOT_PURPOSE = "self";
        // A hit block flashes until it has gone this long without being hit again; the damage flashes while any does.
        const double HIT_SECONDS = 3d;
        const double FLASH_SECONDS = 0.5d;
        // Missiles are looked for this far past the ship's own radius.
        const double MISSILE_RANGE_METRES = 3000d;
        const int MISSILE_SCAN_TICKS = 6;
        static readonly Color HitColor = new Color(255, 0, 0);
        static readonly Color MissileColor = new Color(255, 60, 60);

        struct Hit
        {
            public long Grid;
            public Vector3I Position;
            public DateTime Time;
        }

        readonly GridSnapshotSlot _snapshot;
        readonly List<Hit> _hits = new List<Hit>();
        readonly List<IMyMissile> _missiles = new List<IMyMissile>();
        IMyProjector _projector;
        long[] _seenHits;
        int _shownHits = -1;
        int _incoming;
        int _missileTick;
        DateTime _nextFlash;
        bool _flashOff;

        protected override string DefaultTitle => TITLE;
        protected override bool Starts3D => true;

        protected override UserControl CreateView()
        {
            return new SelfDiagnosticView { DataContext = ViewModel };
        }

        public SelfDiagnosticApp(IAppHost host) : base(host)
        {
            _snapshot = new GridSnapshotSlot(GridSnapshotStorage.KeyFor(host.SurfaceIndex, SNAPSHOT_PURPOSE));
            ViewModel.SnapshotRequested += TakeSnapshot;
            GridSceneState state = ViewModel.State;
            state.BlockHighlightStyle = GridHighlightStyle.Overlay;
            state.DamagedStyle = GridHighlightStyle.Overlay;
            state.DamagedColor = HitColor;
            state.ShadeDamage = false;
        }

        public override void Close()
        {
            ViewModel.SnapshotRequested -= TakeSnapshot;
            base.Close();
        }

        // Hits or incoming missiles are on the screen (a FTUE tip explains them).
        public bool ShowsCombat => _hits.Count > 0 || _incoming > 0;

        public bool CanTakeSnapshot => Block != null && Block.CubeGrid != null && !_snapshot.IsBusy;
        public bool HasSnapshot => _snapshot.Snapshot != null;

        // Of the ship and its subgrids, kept relative to the screen's block.
        public void TakeSnapshot()
        {
            if (CanTakeSnapshot)
                _snapshot.Capture(Block.CubeGrid, Block.EntityId, true);
        }

        public void ClearSnapshot()
        {
            _snapshot.Set(Block, null, true);
        }

        protected override string LegendSignature => _hits.Count + "/" + _incoming;

        protected override void AddLegendEntries(List<IntegrityLegendEntry> entries)
        {
            if (_hits.Count > 0)
                entries.Add(new IntegrityLegendEntry(HitColor,
                    LocHelper.GetLoc(MOD_PREFIX + "SelfDiagnostic_Hits") + " (" + _hits.Count + ")"));
            if (_incoming > 0)
                entries.Add(new IntegrityLegendEntry(MissileColor,
                    LocHelper.GetLoc(MOD_PREFIX + "SelfDiagnostic_Missiles") + " (" + _incoming + ")"));
        }

        protected override bool UpdateSources()
        {
            _snapshot.Update(Block);
            IMyCubeGrid grid = Block?.CubeGrid;
            ViewModel.SetSnapshotButton(_snapshot.Snapshot == null, CanTakeSnapshot);
            if (grid == null)
            {
                ShowWithoutMap(LocHelper.Empty, "Warning", ColorComponent.ResolveWarningColor());
                return false;
            }
            SetTitle(grid.CustomName);

            if (_snapshot.Snapshot != null)
            {
                ViewModel.SetSideWarning(ViewModel.ReferenceMismatch
                    ? LocHelper.GetLoc(MOD_PREFIX + "Integrity_SnapshotMismatch")
                    : _snapshot.Error);
                ViewModel.ShowGrids(grid.EntityId, 0, null, _snapshot.Snapshot);
            }
            else
            {
                FindProjector(grid, ref _projector);
                bool projector = _projector != null && _projector.IsFunctional && !_projector.Closed &&
                                 _projector.Enabled && _projector.ProjectedGrid != null && _projector.CubeGrid == grid;
                ViewModel.SetSideWarning(_snapshot.Error ?? LocHelper.GetLoc(MOD_PREFIX + (projector
                    ? "SelfDiagnostic_ProjectorBackup"
                    : "SelfDiagnostic_NoSnapshot")));
                ViewModel.ShowGrids(grid.EntityId, projector ? _projector.ProjectedGrid.EntityId : 0);
            }
            UpdateHits();
            UpdateMissiles(grid);
            return true;
        }

        // Each hit block flashes until it has gone HIT_SECONDS without another hit, then drops out on its own; the
        // damage flashes while any hit block remains, then stays steady. The highlights are rebuilt only when what
        // shows changes.
        void UpdateHits()
        {
            DateTime now = DateTime.UtcNow;
            long[] hits = ViewModel.LastHits;
            if (hits != null && !ReferenceEquals(hits, _seenHits))
            {
                _seenHits = hits;
                for (int i = 0; i + 3 < hits.Length; i += 4)
                {
                    var position = new Vector3I((int)hits[i + 1], (int)hits[i + 2], (int)hits[i + 3]);
                    _hits.RemoveAll(hit => hit.Grid == hits[i] && hit.Position == position);
                    _hits.Add(new Hit { Grid = hits[i], Position = position, Time = now });
                }
                _shownHits = -1;
            }
            if (_hits.RemoveAll(hit => (now - hit.Time).TotalSeconds >= HIT_SECONDS) > 0)
                _shownHits = -1;
            bool flashing = _hits.Count > 0;
            bool flashOff = _flashOff;
            if (!flashing)
                flashOff = false;
            else if (now >= _nextFlash)
            {
                flashOff = !flashOff;
                _nextFlash = now.AddSeconds(FLASH_SECONDS);
            }
            if (flashOff == _flashOff && _shownHits == _hits.Count)
                return;
            _flashOff = flashOff;
            _shownHits = _hits.Count;
            GridSceneState state = ViewModel.State;
            state.HideDamaged = flashOff;
            state.ClearBlockHighlights();
            for (int i = 0; !flashOff && i < _hits.Count; i++)
                state.HighlightBlock(_hits[i].Grid, _hits[i].Position, HitColor);
            ViewModel.InvalidateScene();
            if (flashing)
                Host.RequestFrames();
        }

        // Counts the missiles not launched from this ship and closing in on it, for the legend; the scene draws
        // every missile itself.
        void UpdateMissiles(IMyCubeGrid grid)
        {
            if (++_missileTick < MISSILE_SCAN_TICKS)
                return;
            _missileTick = 0;
            _incoming = 0;
            BoundingSphereD ship = grid.WorldVolume;
            MissileScanner.Collect(new BoundingSphereD(ship.Center, ship.Radius + MISSILE_RANGE_METRES), _missiles);
            for (int i = 0; i < _missiles.Count; i++)
            {
                IMyMissile missile = _missiles[i];
                if (IsOwn(missile, grid))
                    continue;
                Vector3D position = ((IMyEntity)missile).GetPosition();
                Vector3 velocity = missile.LinearVelocity;
                if (Vector3D.Dot(ship.Center - position, velocity) <= 0d)
                    continue;
                _incoming++;
            }
            _missiles.Clear();
        }

        static bool IsOwn(IMyMissile missile, IMyCubeGrid grid)
        {
            var launcher = MyAPIGateway.Entities.GetEntityById(missile.LauncherId) as IMyCubeBlock;
            return launcher != null && launcher.CubeGrid != null && launcher.CubeGrid.IsInSameLogicalGroupAs(grid);
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
}
