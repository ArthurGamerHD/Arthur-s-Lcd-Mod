using System;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Client.Helpers
{
    // The grid the local player has locked on (the game's target locking), seen from a screen's block: the player's
    // character, what they control or look through, the screen's cockpit, the main cockpit, then the grid itself.
    internal sealed class TargetLockReader
    {
        // A grid asked for while another was locked: the server takes any request during a lock as letting go, so this
        // one is asked for once the lock is gone.
        const double SWITCH_TIMEOUT_SECONDS = 3d;

        long _cachedCharacterId;
        Sandbox.Game.EntityComponents.MyTargetLockingComponent _cachedCharacterTargetLocking;
        MyTargetFocusComponent _cachedCharacterTargetFocus;
        long _switchToId;
        DateTime _switchUntil;

        // The grid the lock key would lock now (the game's own pick: in range, nearest the aim, in sight); null while
        // the player is not in a seat with target locking. While a lock is on, the key lets it go instead.
        public IMyCubeGrid GetLockCandidate()
        {
            RefreshCachedCharacterTargetLocking(MyAPIGateway.Session.LocalHumanPlayer);
            MyTargetFocusComponent focus = _cachedCharacterTargetFocus;
            if (focus == null || !focus.IsTargetLockingEnabledInController)
                return null;
            var grid = focus.CurrentTarget as IMyCubeGrid;
            return grid == null || grid.MarkedForClose ? null : grid;
        }

        // The player sits where target locking works, so a lock can be asked for.
        public bool CanRequestLock
        {
            get
            {
                RefreshCachedCharacterTargetLocking(MyAPIGateway.Session.LocalHumanPlayer);
                MyTargetFocusComponent focus = _cachedCharacterTargetFocus;
                return focus != null && _cachedCharacterTargetLocking != null && focus.IsTargetLockingEnabledInController;
            }
        }

        // Starts locking this grid, as the lock key does for its candidate; false when the game would refuse it.
        public bool RequestLock(IMyCubeGrid grid)
        {
            if (grid == null || !IsLockable(grid))
                return false;
            MyTargetLockingComponent locking = _cachedCharacterTargetLocking;
            MyEntity current = locking.TargetEntity;
            if (current == null)
            {
                _switchToId = 0;
                locking.OnTargetRequest((MyEntity)grid);
                return true;
            }
            if (current.EntityId == grid.EntityId)
                return true;
            _switchToId = grid.EntityId;
            _switchUntil = DateTime.UtcNow.AddSeconds(SWITCH_TIMEOUT_SECONDS);
            locking.ReleaseTargetLockRequest();
            return true;
        }

        // The server's own test: in lock range, and inside the lock cone of the seat or the camera looked through.
        public bool IsLockable(IMyCubeGrid grid)
        {
            if (grid == null || grid.MarkedForClose || !CanRequestLock)
                return false;
            MatrixD shooter;
            if (!TryGetShooterMatrix(out shooter))
                return false;
            double cosSquared;
            return _cachedCharacterTargetFocus.IsTargetInRange(shooter, (MyEntity)grid, out cosSquared, true);
        }

        // As the server aims a lock: through the camera block looked through, from the cockpit's head in first person,
        // otherwise along the controlled block.
        static bool TryGetShooterMatrix(out MatrixD matrix)
        {
            matrix = MatrixD.Identity;
            IMyCameraController camera = MyAPIGateway.Session.CameraController;
            IMyEntity viewed = camera == null ? null : camera.Entity;
            if (viewed is IMyCameraBlock)
            {
                matrix = viewed.WorldMatrix;
                return true;
            }
            if (viewed is IMyCockpit && camera.IsInFirstPersonView && MyAPIGateway.Session.Camera != null)
            {
                matrix = MyAPIGateway.Session.Camera.WorldMatrix;
                return true;
            }
            IMyEntity controlled = MyAPIGateway.Session.ControlledObject == null ? null : MyAPIGateway.Session.ControlledObject.Entity;
            if (controlled == null)
                return false;
            matrix = controlled.WorldMatrix;
            return true;
        }

        // Asks for the grid a switch is waiting on once the old lock is gone; called every update.
        public void Update()
        {
            if (_switchToId == 0)
                return;
            MyTargetLockingComponent locking = _cachedCharacterTargetLocking;
            if (locking == null || DateTime.UtcNow > _switchUntil)
            {
                _switchToId = 0;
                return;
            }
            if (locking.TargetEntity != null)
                return;
            var grid = MyAPIGateway.Entities.GetEntityById(_switchToId) as IMyCubeGrid;
            _switchToId = 0;
            if (IsLockable(grid))
                locking.OnTargetRequest((MyEntity)grid);
        }

        public long GetLockedGridId(IMyCubeBlock block)
        {
            long entityId;
            float percent;
            return GetLockedGridId(block, out entityId, out percent);
        }

        public long GetLockedGridId(IMyCubeBlock block, out long targetEntityId, out float targetLockPercent)
        {
            bool locked;
            return GetLockedGridId(block, out targetEntityId, out targetLockPercent, out locked);
        }

        // The grid being locked on, and whether the lock has finished (while it is still acquiring, locked is false).
        public long GetLockedGridId(IMyCubeBlock block, out bool locked)
        {
            long entityId;
            float percent;
            return GetLockedGridId(block, out entityId, out percent, out locked);
        }

        public long GetLockedGridId(IMyCubeBlock block, out long targetEntityId, out float targetLockPercent,
            out bool locked)
        {
            targetEntityId = 0;
            targetLockPercent = 0f;
            locked = false;

            long lockedEntityId;
            float lockedPercent;
            bool lockedDone;

            var player = MyAPIGateway.Session.LocalHumanPlayer;
            RefreshCachedCharacterTargetLocking(player);

            var lockedGridId = GetLockedTargetGridIdFromTargetLockingComponent(_cachedCharacterTargetLocking,
                out lockedEntityId,
                out lockedPercent,
                out lockedDone);
            if (lockedEntityId != 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
            {
                locked = lockedDone;
                return lockedGridId;
            }

            var playerCharacter = player?.Character;
            lockedGridId = GetLockedTargetGridIdFromEntity(playerCharacter, out lockedEntityId, out lockedPercent, out lockedDone);
            if (lockedEntityId != 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
            {
                locked = lockedDone;
                return lockedGridId;
            }

            var controlledEntity = player?.Controller?.ControlledEntity?.Entity;
            lockedGridId = GetLockedTargetGridIdFromEntity(controlledEntity, out lockedEntityId, out lockedPercent, out lockedDone);
            if (lockedEntityId != 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
            {
                locked = lockedDone;
                return lockedGridId;
            }

            var cameraEntity = MyAPIGateway.Session.CameraController?.Entity;
            lockedGridId = GetLockedTargetGridIdFromEntity(cameraEntity, out lockedEntityId, out lockedPercent, out lockedDone);
            if (lockedEntityId != 0 && targetEntityId == 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
            {
                locked = lockedDone;
                return lockedGridId;
            }

            var cockpit = block as IMyCockpit;
            lockedGridId = GetLockedTargetGridIdFromEntity(cockpit, out lockedEntityId, out lockedPercent, out lockedDone);
            if (lockedEntityId != 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
            {
                locked = lockedDone;
                return lockedGridId;
            }

            var myGrid = block?.CubeGrid as Sandbox.Game.Entities.MyCubeGrid;
            var mainCockpit = myGrid?.MainCockpit as IMyCockpit;
            lockedGridId = GetLockedTargetGridIdFromEntity(mainCockpit, out lockedEntityId, out lockedPercent, out lockedDone);
            if (lockedEntityId != 0 && targetEntityId == 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
            {
                locked = lockedDone;
                return lockedGridId;
            }

            lockedGridId = GetLockedTargetGridIdFromEntity(myGrid, out lockedEntityId, out lockedPercent, out lockedDone);
            if (lockedEntityId != 0 && targetEntityId == 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            locked = lockedGridId != 0 && lockedDone;
            return lockedGridId;
        }

        void RefreshCachedCharacterTargetLocking(IMyPlayer player)
        {
            var characterEntity = player?.Character as IMyEntity;
            var currentCharacterId = characterEntity?.EntityId ?? 0;
            if (currentCharacterId == _cachedCharacterId)
                return;

            _cachedCharacterId = currentCharacterId;
            _cachedCharacterTargetLocking = characterEntity?.Components?
                .Get<Sandbox.Game.EntityComponents.MyTargetLockingComponent>();
            _cachedCharacterTargetFocus = characterEntity?.Components?.Get<MyTargetFocusComponent>();
        }

        static long GetLockedTargetGridIdFromEntity(IMyEntity entity, out long targetEntityId,
            out float targetLockPercent, out bool locked)
        {
            targetEntityId = 0;
            targetLockPercent = 0f;
            locked = false;
            if (entity == null || entity.Components == null)
                return 0;

            var targetLockingBlock =
                entity.Components.Get<Sandbox.Game.EntityComponents.MyTargetLockingBlockComponent>();
            if (targetLockingBlock != null)
            {
                var blockTargetEntity = targetLockingBlock.TargetEntity;
                if (blockTargetEntity != null)
                {
                    targetEntityId = blockTargetEntity.EntityId;
                    targetLockPercent = targetLockingBlock.LockingProgressPercent;
                    locked = targetLockingBlock.IsTargetLocked;
                    var blockTargetGrid = blockTargetEntity as IMyCubeGrid;
                    if (blockTargetGrid != null)
                        return blockTargetGrid.EntityId;
                }
            }

            var targetLocking = entity.Components.Get<Sandbox.Game.EntityComponents.MyTargetLockingComponent>();
            return GetLockedTargetGridIdFromTargetLockingComponent(targetLocking, out targetEntityId,
                out targetLockPercent, out locked);
        }

        static long GetLockedTargetGridIdFromTargetLockingComponent(
            Sandbox.Game.EntityComponents.MyTargetLockingComponent targetLocking,
            out long targetEntityId,
            out float targetLockPercent,
            out bool locked)
        {
            targetEntityId = 0;
            targetLockPercent = 0f;
            locked = false;
            if (targetLocking == null)
                return 0;

            var targetEntity = targetLocking.TargetEntity;
            if (targetEntity == null)
                return 0;

            targetEntityId = targetEntity.EntityId;
            targetLockPercent = targetLocking.LockingProgressPercent;
            locked = targetLocking.IsTargetLocked;
            var targetGrid = targetEntity as IMyCubeGrid;
            return targetGrid?.EntityId ?? 0;
        }
    }
}
