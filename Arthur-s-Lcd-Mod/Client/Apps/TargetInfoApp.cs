using System;
using Acrux.Render3D;
using LcdMod.Client.Apps.Abstract;
using Acrux.Controls;
using LcdMod.Client.Apps.IntegrityMonitor;
using LcdMod.Client.Apps.TargetInfo;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Snapshots;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps
{
    // The grid the player has locked on, like the Integrity Monitor shows its own: a snapshot taken once the first lock
    // completes is what the target should be, so what was shot off shows as missing. Once the lock is lost, the target as last
    // seen stays on the screen from memory for a few seconds, then is forgotten. Both snapshots are kept on the screen's block.
    [LcdApp(31)]
    [ConfigComponent(APP, typeof(DiagnosticConfigComponent), PropertyName = "DiagnosticComponent")]
    internal sealed partial class TargetInfoApp : GridInspectorApp
    {
        public const string TITLE = MOD_PREFIX + "TargetInfo";
        const string BASELINE_PURPOSE = "target";
        const string LAST_SEEN_PURPOSE = "target-last";
        // How often the locked target's last seen state is taken again.
        const double LAST_SEEN_SECONDS = 5d;
        // Real time a lost target stays on the screen before it is forgotten.
        const double MEMORY_SECONDS = 10d;
        const double DAMAGE_FLASH_SECONDS = 0.5d;
        // How far a scan has to get before the target's class, then its identification code, is known.
        const float CLASS_REVEAL_PERCENT = 50f;
        const float CODE_REVEAL_PERCENT = 75f;

        readonly TargetLockReader _targetLock = new TargetLockReader();
        readonly GridSnapshotSlot _baseline;
        readonly GridSnapshotSlot _lastSeen;
        // The target the baseline was (or is being) taken of.
        long _baselineGridId;
        long _lockedGridId;
        DateTime _nextLastSeen;
        DateTime _nextFlash;
        DateTime _forgetAt;
        // The class and code of the grid being scanned, worked out once per grid.
        long _classGridId;
        string _className;
        string _classCode;
        // The camera was pointed this run (by a lock, or from the saved aim); the aim changed since it was saved.
        bool _aimed;
        bool _aimUnsaved;

        protected override string DefaultTitle => TITLE;
        protected override bool FixedCamera => true;
        protected override bool Starts3D => true;

        public TargetInfoApp(IAppHost host) : base(host)
        {
            _baseline = new GridSnapshotSlot(GridSnapshotStorage.KeyFor(host.SurfaceIndex, BASELINE_PURPOSE));
            _lastSeen = new GridSnapshotSlot(GridSnapshotStorage.KeyFor(host.SurfaceIndex, LAST_SEEN_PURPOSE));
            ViewModel.SetFixedCamera(true);
            // What the enemy shows from outside: missing blocks as their own model's outline behind the hull that
            // still stands, damaged ones in plain red that flashes.
            GridSceneState state = ViewModel.State;
            state.MissingStyle = GridHighlightStyle.ModelWireframe;
            state.DamagedStyle = GridHighlightStyle.Overlay;
            state.DamagedColor = new Color(255, 0, 0);
            state.ShadeDamage = false;
            ViewModel.LockRequested += OnLockRequested;
        }

        // The lock button: the grid the game's lock key would lock now.
        void OnLockRequested()
        {
            _targetLock.RequestLock(_targetLock.GetLockCandidate());
        }

        protected override UserControl CreateView()
        {
            return new TargetInfoView { DataContext = ViewModel };
        }

        // A target is locked now (raised when a lock starts), or the screen shows one from memory.
        public event Action TargetAcquired;
        public bool HasTarget => _lockedGridId != 0;
        public bool IsShowingMemory => _lockedGridId == 0 && (_lastSeen.Snapshot != null || _baseline.Snapshot != null);

        public bool HasSnapshot => _baseline.Snapshot != null || _lastSeen.Snapshot != null;

        // Forgets the target; the next lock starts over.
        public void ClearSnapshot()
        {
            _baseline.Set(Block, null, true);
            _lastSeen.Set(Block, null, true);
            _baselineGridId = 0;
            _lockedGridId = 0;
            _forgetAt = default(DateTime);
        }

        protected override bool UpdateSources()
        {
            _baseline.Update(Block);
            _lastSeen.Update(Block);
            ViewModel.SetSnapshotButton(false, false);
            if (_baselineGridId == 0 && _baseline.Snapshot != null)
                _baselineGridId = _baseline.Snapshot.GridEntityId;

            _targetLock.Update();
            bool locked = false;
            long lockedEntityId;
            float lockPercent = 0f;
            long lockedId = Block == null ? 0 : _targetLock.GetLockedGridId(Block, out lockedEntityId, out lockPercent, out locked);
            var target = lockedId == 0 || !locked ? null : MyAPIGateway.Entities.GetEntityById(lockedId) as IMyCubeGrid;
            if (target != null && !target.MarkedForClose)
            {
                DateTime now = DateTime.UtcNow;
                FlashDamage(now);
                if (!IsBaselineOf(target))
                {
                    _baselineGridId = target.EntityId;
                    _baseline.Capture(target, 0, true);
                    _lastSeen.Set(Block, null, false);
                    _nextLastSeen = now.AddSeconds(LAST_SEEN_SECONDS);
                }
                else if (now >= _nextLastSeen && !_lastSeen.IsBusy && !_baseline.IsBusy)
                {
                    GridSnapshot baseline = _baseline.Snapshot;
                    _lastSeen.Capture(target, baseline == null ? 0 : baseline.PivotEntityId, false);
                    _nextLastSeen = now.AddSeconds(LAST_SEEN_SECONDS);
                }
                if (_lockedGridId != target.EntityId)
                {
                    ViewModel.RequestFit(GeneralComponent.GetScale());
                    TargetAcquired?.Invoke();
                }
                _lockedGridId = target.EntityId;
                AimCamera(target);
                SetTitle(target.CustomName);
                ViewModel.SetSideWarning(null);
                ViewModel.SetAlert(null);
                ViewModel.SetLockButton(null);
                if (Identify(target))
                    // Distance first: right-aligned, the code holds still while the distance's width changes.
                    ViewModel.SetTargetInfo(_className, FormatingHelper.DistanceToString(
                        (float)Vector3D.Distance(Block.GetPosition(), target.WorldVolume.Center)) + " " + _classCode);
                ViewModel.ShowGrids(target.EntityId, 0, null, _baseline.Snapshot);
                return true;
            }

            // The lock was just lost: what was last seen is kept with the world too, and so is the view of it.
            if (_lockedGridId != 0)
            {
                _lockedGridId = 0;
                _forgetAt = DateTime.UtcNow.AddSeconds(MEMORY_SECONDS);
                if (_lastSeen.Snapshot != null)
                    _lastSeen.Set(Block, _lastSeen.Snapshot, true);
            }
            if (_aimUnsaved)
            {
                _aimUnsaved = false;
                SaveSettings();
            }
            if (!_aimed)
                RestoreAim();
            GridSnapshot memory = _lastSeen.Snapshot ?? _baseline.Snapshot;
            if (memory != null && DateTime.UtcNow >= ForgetTime(memory))
            {
                ClearSnapshot();
                memory = null;
            }
            // A lock still being acquired.
            if (lockedId != 0)
            {
                ViewModel.SetSideWarning(null);
                ViewModel.SetAlert(null);
                ViewModel.SetLockButton(null);
                ViewModel.SetTargetInfo(null, null);
                SetTitle(null);
                ShowWithoutMap(string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "TargetInfo_Scanning"),
                    (int)MathHelper.Clamp(lockPercent, 0f, 99f)) + "\n" + ScanReading(lockedId, lockPercent), "Lock",
                    Host.ForegroundColor);
                Host.RequestFrames();
                return false;
            }
            IMyCubeGrid candidate = _targetLock.GetLockCandidate();
            ViewModel.SetLockButton(candidate == null
                ? null
                : string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "TargetInfo_LockCandidate"),
                    candidate.CustomName));
            if (memory == null)
            {
                ViewModel.SetSideWarning(null);
                ViewModel.SetAlert(null);
                ViewModel.SetTargetInfo(null, null);
                SetTitle(null);
                ShowWithoutMap(LocHelper.GetLoc(MOD_PREFIX + "TargetInfo_NoTarget"), "Warning",
                    ColorComponent.ResolveWarningColor());
                return false;
            }
            SetTitle(memory.GridName);
            ViewModel.SetSideWarning(LocHelper.GetLoc(MOD_PREFIX + "TargetInfo_Lost"));
            ViewModel.SetAlert(LocHelper.GetLoc(MOD_PREFIX + "TargetInfo_LossAlert"));
            if (_classGridId != 0 && _classGridId == memory.GridEntityId)
                ViewModel.SetTargetInfo(_className, _classCode);
            else
                ViewModel.SetTargetInfo(null, null);
            ViewModel.ShowGrids(0, 0, memory, _baseline.Snapshot);
            return true;
        }

        // The class, then the code under it, each unknown until the scan is far enough along.
        string ScanReading(long gridId, float percent)
        {
            if (percent < CLASS_REVEAL_PERCENT)
                return "???\n???";
            if (!Identify(MyAPIGateway.Entities.GetEntityById(gridId) as IMyCubeGrid))
                return "???\n???";
            return _className + "\n" + (percent < CODE_REVEAL_PERCENT ? "???" : _classCode);
        }

        // Reads the grid's class and code once; a lock kept from the scan keeps what the scan read.
        bool Identify(IMyCubeGrid grid)
        {
            if (grid == null || grid.MarkedForClose)
                return false;
            if (grid.EntityId != _classGridId)
            {
                _classGridId = grid.EntityId;
                ShipClassifier.Identify(grid, out _className, out _classCode);
            }
            return true;
        }

        void FlashDamage(DateTime now)
        {
            GridSceneState state = ViewModel.State;
            if (now < _nextFlash || (ViewModel.DamagedCount == 0 && !state.HideDamaged))
                return;
            _nextFlash = now.AddSeconds(DAMAGE_FLASH_SECONDS);
            state.HideDamaged = !state.HideDamaged;
            ViewModel.InvalidateScene();
            Host.RequestFrames();
        }

        // A memory restored from the block (no lock lost this run) counts from when it was taken.
        DateTime ForgetTime(GridSnapshot memory)
        {
            if (_forgetAt == default(DateTime))
                _forgetAt = new DateTime(memory.CapturedTicks, DateTimeKind.Utc).AddSeconds(MEMORY_SECONDS);
            return _forgetAt;
        }

        // The target as seen from this ship: the camera on the line from the target to the ship, its up the ship's
        // up, in the target's own axes. A lost target keeps the last view.
        void AimCamera(IMyCubeGrid target)
        {
            MatrixD own = OwnOrientation();
            MatrixD toTarget = MatrixD.Transpose(target.WorldMatrix.GetOrientation());
            Vector3D toCamera = Block.GetPosition() - target.WorldVolume.Center;
            if (toCamera.LengthSquared() < 1e-6d)
                return;
            toCamera = Vector3D.TransformNormal(Vector3D.Normalize(toCamera), toTarget);
            double yaw = Math.Atan2(toCamera.X, toCamera.Z);
            double pitch = Math.Asin(MathHelper.Clamp(toCamera.Y, -1d, 1d));
            // The renderer's own axes for this yaw and pitch, before its roll.
            var right = new Vector3D(Math.Cos(yaw), 0d, -Math.Sin(yaw));
            Vector3D up = Vector3D.Normalize(Vector3D.Cross(right, -toCamera));
            Vector3D wanted = Vector3D.TransformNormal(own.Up, toTarget);
            double roll = Math.Atan2(-Vector3D.Dot(wanted, right), Vector3D.Dot(wanted, up));
            GridSceneState state = ViewModel.State;
            _aimed = true;
            if (Math.Abs(state.Yaw - yaw) + Math.Abs(state.Pitch - pitch) + Math.Abs(state.Roll - roll) < 0.002d)
                return;
            state.Yaw = (float)yaw;
            state.Pitch = (float)pitch;
            state.Roll = (float)roll;
            DiagnosticConfigComponent config = Settings;
            config.CameraYaw = state.Yaw;
            config.CameraPitch = state.Pitch;
            config.CameraRoll = state.Roll;
            _aimUnsaved = true;
            ViewModel.InvalidateScene();
            Host.RequestFrames();
        }

        // Without a lock since the screen started: the view the target was last seen from.
        void RestoreAim()
        {
            _aimed = true;
            DiagnosticConfigComponent config = Settings;
            GridSceneState state = ViewModel.State;
            state.Yaw = config.CameraYaw;
            state.Pitch = config.CameraPitch;
            state.Roll = config.CameraRoll;
            ViewModel.InvalidateScene();
        }

        // The ship's orientation: the cockpit the screen is in, the main cockpit, or the grid.
        MatrixD OwnOrientation()
        {
            var controller = Block as IMyShipController;
            if (controller != null)
                return controller.WorldMatrix;
            var grid = Block.CubeGrid as Sandbox.Game.Entities.MyCubeGrid;
            var main = grid?.MainCockpit as IMyShipController;
            return main != null ? main.WorldMatrix : Block.CubeGrid.WorldMatrix;
        }

        // The same ship as the baseline: the grid it was taken of, or what is left of it after a split (the grid now
        // holding its pivot block).
        bool IsBaselineOf(IMyCubeGrid target)
        {
            if (target.EntityId == _baselineGridId)
                return true;
            GridSnapshot baseline = _baseline.Snapshot;
            if (baseline == null || baseline.PivotEntityId == 0)
                return false;
            var pivot = MyAPIGateway.Entities.GetEntityById(baseline.PivotEntityId) as IMyCubeBlock;
            if (pivot == null || pivot.CubeGrid != target)
                return false;
            _baselineGridId = target.EntityId;
            return true;
        }
    }
}
