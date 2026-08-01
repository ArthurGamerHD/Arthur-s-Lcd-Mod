using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ModAPI;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;

namespace LcdMod.Client.Helpers
{
    internal struct RadioSignalMarker
    {
        public long EntityId;
        public string Name;
        public Vector3D WorldPosition;
        public MyRelationsBetweenPlayerAndBlock Relationship;
    }

    internal struct RadioSignalMarkerProjection
    {
        public RadioSignalMarker Marker;
        public Vector2 ScreenPosition;
    }

    internal struct RadioSignalMarkerCluster
    {
        public RadioSignalMarker RepresentativeMarker;
        public Vector2 ScreenPosition;
        public int Count;
    }

    internal sealed class RadioSignalMarkerCollector
    {
        readonly HashSet<IMyEntity> _entities = new HashSet<IMyEntity>();
        readonly List<IMySlimBlock> _signalBlocks = new List<IMySlimBlock>();
        readonly Dictionary<long, int> _markerIndexByEntityId = new Dictionary<long, int>();

        public void Collect(IMyCubeBlock receiverBlock, List<RadioSignalMarker> markers)
        {
            if (markers == null)
                return;

            markers.Clear();
            _entities.Clear();
            _markerIndexByEntityId.Clear();

            var session = MyAPIGateway.Session;
            var player = session == null ? null : session.Player;
            var receiverGrid = receiverBlock == null ? null : receiverBlock.CubeGrid;
            if (receiverBlock == null || receiverGrid == null || player == null || MyAPIGateway.Entities == null)
                return;

            Vector3D receiverPosition = receiverBlock.WorldMatrix.Translation;
            try
            {
                MyAPIGateway.Entities.GetEntities(_entities, IsPotentialSignalEntity);
                foreach (IMyEntity entity in _entities)
                {
                    if (entity == null || entity.Closed || entity.MarkedForClose)
                        continue;

                    if (IsSignalEntity(entity))
                        TryAddSignalMarker(entity, receiverPosition, receiverGrid, player.IdentityId, markers);

                    var grid = entity as IMyCubeGrid;
                    if (grid != null)
                        CollectGridSignals(grid, receiverPosition, receiverGrid, player.IdentityId, markers);
                }
            }
            catch (Exception exception)
            {
                ErrorHandlerHelper.LogError(exception, typeof(RadioSignalMarkerCollector));
            }
            finally
            {
                _entities.Clear();
                _signalBlocks.Clear();
                _markerIndexByEntityId.Clear();
            }
        }

        static bool IsPotentialSignalEntity(IMyEntity entity)
        {
            return entity is IMyCubeGrid || IsSignalEntity(entity);
        }

        static bool IsSignalEntity(IMyEntity entity)
        {
            return entity is IMyRadioAntenna ||
                   entity is IMyBeacon ||
                   entity is IMyLaserAntenna;
        }

        void CollectGridSignals(
            IMyCubeGrid grid,
            Vector3D receiverPosition,
            IMyCubeGrid receiverGrid,
            long playerIdentityId,
            List<RadioSignalMarker> markers)
        {
            if (grid == null || receiverGrid == null || grid.EntityId == receiverGrid.EntityId)
                return;

            try
            {
                _signalBlocks.Clear();
                grid.GetBlocks(
                    _signalBlocks,
                    slimBlock => IsSignalBlockInRange(slimBlock, receiverPosition, receiverGrid));

                for (int i = 0; i < _signalBlocks.Count; i++)
                {
                    var entity = _signalBlocks[i].FatBlock as IMyEntity;
                    if (entity != null)
                        TryAddSignalMarker(entity, receiverPosition, receiverGrid, playerIdentityId, markers);
                }
            }
            finally
            {
                _signalBlocks.Clear();
            }
        }

        static bool IsSignalBlockInRange(
            IMySlimBlock slimBlock,
            Vector3D receiverPosition,
            IMyCubeGrid receiverGrid)
        {
            var entity = slimBlock == null ? null : slimBlock.FatBlock as IMyEntity;
            return entity != null && IsSignalInRange(entity, receiverPosition, receiverGrid);
        }

        void TryAddSignalMarker(IMyEntity entity,
            Vector3D receiverPosition,
            IMyCubeGrid receiverGrid,
            long playerIdentityId,
            List<RadioSignalMarker> markers)
        {
            var block = entity as IMyCubeBlock;
            var signalGrid = block == null ? null : block.CubeGrid;
            if (signalGrid != null && receiverGrid != null && signalGrid.EntityId == receiverGrid.EntityId) return;

            if (!IsSignalInRange(entity, receiverPosition, receiverGrid)) return;

            long entityId = signalGrid == null ? entity.EntityId : signalGrid.EntityId;
            var terminalBlock = block as IMyTerminalBlock;
            var marker = new RadioSignalMarker
            {
                EntityId = entityId,
                Name = GetSignalName(entity, terminalBlock, signalGrid),
                WorldPosition = entity.WorldMatrix.Translation,
                Relationship = terminalBlock == null
                    ? MyRelationsBetweenPlayerAndBlock.Neutral
                    : terminalBlock.GetUserRelationToOwner(playerIdentityId)
            };

            int existingIndex;
            if (!_markerIndexByEntityId.TryGetValue(entityId, out existingIndex))
            {
                _markerIndexByEntityId[entityId] = markers.Count;
                markers.Add(marker);
                return;
            }

            double existingDistanceSquared = Vector3D.DistanceSquared(
                markers[existingIndex].WorldPosition,
                receiverPosition);
            double candidateDistanceSquared = Vector3D.DistanceSquared(
                marker.WorldPosition,
                receiverPosition);
            if (candidateDistanceSquared < existingDistanceSquared)
                markers[existingIndex] = marker;
        }

        static bool IsSignalInRange(
            IMyEntity entity,
            Vector3D receiverPosition,
            IMyCubeGrid receiverGrid)
        {
            var functional = entity as IMyFunctionalBlock;
            if (functional == null || !functional.IsFunctional || !functional.Enabled)
                return false;

            var radio = entity as IMyRadioAntenna;
            if (radio != null)
            {
                return radio.IsBroadcasting &&
                       BroadcastRangeReaches(radio.WorldMatrix.Translation, radio.Radius, receiverPosition);
            }

            var beacon = entity as IMyBeacon;
            if (beacon != null)
                return BroadcastRangeReaches(beacon.WorldMatrix.Translation, beacon.Radius, receiverPosition);

            var laser = entity as IMyLaserAntenna;
            if (laser == null || laser.Other == null || receiverGrid == null)
                return false;

            return laser.Other.CubeGrid != null &&
                   laser.Other.CubeGrid.EntityId == receiverGrid.EntityId &&
                   laser.IsInRange(laser.Other);
        }

        static bool BroadcastRangeReaches(
            Vector3D broadcastPosition,
            float radius,
            Vector3D receiverPosition)
        {
            if (radius <= 0f)
                return false;

            double radiusSquared = radius;
            radiusSquared *= radiusSquared;
            return Vector3D.DistanceSquared(broadcastPosition, receiverPosition) <= radiusSquared;
        }

        static string GetSignalName(
            IMyEntity entity,
            IMyTerminalBlock terminalBlock,
            IMyCubeGrid signalGrid)
        {
            var radio = entity as IMyRadioAntenna;
            if (radio != null && !string.IsNullOrWhiteSpace(radio.HudText))
                return radio.HudText;

            var beacon = entity as IMyBeacon;
            if (beacon != null && !string.IsNullOrWhiteSpace(beacon.HudText))
                return beacon.HudText;

            if (terminalBlock != null && !string.IsNullOrWhiteSpace(terminalBlock.CustomName))
                return terminalBlock.CustomName;

            if (signalGrid != null && !string.IsNullOrWhiteSpace(signalGrid.DisplayName))
                return signalGrid.DisplayName;

            return string.IsNullOrWhiteSpace(entity.DisplayName) ? string.Empty : entity.DisplayName;
        }
    }

    internal static class RadioSignalMarkerLayout
    {
        public static void Cluster(
            IList<RadioSignalMarkerProjection> projections,
            float maximumDistance,
            List<RadioSignalMarkerCluster> clusters,
            List<byte> consumed)
        {
            clusters.Clear();
            consumed.Clear();

            int count = projections == null ? 0 : projections.Count;
            for (int i = 0; i < count; i++)
                consumed.Add(0);

            float maximumDistanceSquared = maximumDistance * maximumDistance;
            for (int i = 0; i < count; i++)
            {
                if (consumed[i] != 0)
                    continue;

                if (projections != null)
                {
                    RadioSignalMarkerProjection anchor = projections[i];
                    consumed[i] = 1;
                    Vector2 positionSum = anchor.ScreenPosition;
                    int clusterCount = 1;

                    for (int j = i + 1; j < count; j++)
                    {
                        if (consumed[j] != 0)
                            continue;

                        Vector2 offset = projections[j].ScreenPosition - anchor.ScreenPosition;
                        if (offset.LengthSquared() > maximumDistanceSquared)
                            continue;

                        consumed[j] = 1;
                        positionSum += projections[j].ScreenPosition;
                        clusterCount++;
                    }

                    clusters.Add(new RadioSignalMarkerCluster
                    {
                        RepresentativeMarker = anchor.Marker,
                        ScreenPosition = positionSum / clusterCount,
                        Count = clusterCount
                    });
                }
            }
        }
    }
}
