using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyShipConnectorStatus = Sandbox.ModAPI.Ingame.MyShipConnectorStatus;

using LcdMod.Common.Config.Generation;

namespace LcdMod.Client.Apps
{
    [LcdApp(11)]
    [ConfigComponent(DOCKABLE_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "DockableReferenceComponent")]
    internal sealed partial class DockingAlignmentApp : App
    {
        const float TARGET_SEARCH_RANGE = 100f;
        const float TARGET_SEARCH_CONE_DEGREES = 45f;
        const long TARGET_SEARCH_DELAY_TICKS = 100L;
        const float MAX_TARGET_ANGLE_DEGREES = 45f;

        static readonly List<MyTerminalControlComboBoxItem> DockingDisplayModes =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)DockingDisplayMode.Default,
                    Value = MyStringId.GetOrCompute(MOD_PREFIX + "DockingAlignment_DisplayMode_Default")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)DockingDisplayMode.LcdReference,
                    Value = MyStringId.GetOrCompute(MOD_PREFIX + "DockingAlignment_DisplayMode_LcdReference")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)DockingDisplayMode.ControllerReference,
                    Value = MyStringId.GetOrCompute(MOD_PREFIX + "DockingAlignment_DisplayMode_ControllerReference")
                }
            };

        readonly List<IMyCubeGrid> _mechanicalGroup = new List<IMyCubeGrid>();
        readonly List<IMyCubeGrid> _parkingSearchGrids = new List<IMyCubeGrid>();
        readonly List<IMySlimBlock> _parkingSearchBlocks = new List<IMySlimBlock>();
        readonly List<IMyEntity> _entityBuffer = new List<IMyEntity>();
        readonly DockingAlignment.DockingAlignmentViewModel _viewModel;

        IMyTerminalBlock _referenceBlock;
        IMyTerminalBlock _targetBlock;
        IMyShipConnector _autoParkingReferenceBlock;
        IEnumerator<bool> _parkingConnectorSearch;
        long _targetSearchReferenceBlockId;
        long _lastTargetSearchFrame = -TARGET_SEARCH_DELAY_TICKS;
        string _pitchLabel;
        string _yawLabel;
        string _rollLabel;
        string _selectReferenceMessage;
        string _noTargetMessage;
        string _noCockpitMessage;
        string _dockingFailureMessage;
        string _displayReferenceWarning;
        double _lastDockingAxisDistance = double.NaN;
        long _lastDockingAxisDistanceFrame = -1L;

        IMyCubeBlock Block => Host.Block;
        IMyTextSurface Surface => Host.Surface;
        float Scale => GeneralComponent.GetScale();
        float FontScale => Host.Surface.FontSize;
        internal override float WindowRenderScaling => Scale > 0f ? Scale : 1f;
        Color BackgroundColor => Host.BackgroundColor;

        public DockingAlignmentApp(IAppHost host) : base(host)
        {
            _viewModel = new DockingAlignment.DockingAlignmentViewModel();
            SetContent(new DockingAlignment.DockingAlignmentView(_viewModel));
        }

        
        
        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DockingDisplayModes;
        }

        public bool IsReferenceBlockCandidate(IMyTerminalBlock block)
        {
            return block is IMyShipConnector || block is IMyShipMergeBlock;
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            _pitchLabel = LocHelper.GetLoc("BlockPropertyTitle_ProjectionRotationX");
            _yawLabel = LocHelper.GetLoc("BlockPropertyTitle_ProjectionRotationY");
            _rollLabel = LocHelper.GetLoc("BlockPropertyTitle_ProjectionRotationZ");
            _selectReferenceMessage = string.Format(
                LocHelper.GetLoc(MOD_PREFIX + "DockingAlignment_SelectReference"),
                LocHelper.GetLoc("DisplayName_Block_Connector"),
                LocHelper.GetLoc("DisplayName_Block_MergeBlock"));
            _noTargetMessage = LocHelper.GetLoc(MOD_PREFIX + "DockingAlignment_NoTarget");
            _noCockpitMessage = LocHelper.GetLoc("TssTargetingInfo_NoMainCockpit");
            _viewModel.RefreshLayout(FontScale, WindowRenderScaling, TextFont, Surface.ScriptForegroundColor,
                GetHeaderColor(), BackgroundColor, ColorComponent.ResolveWarningColor(),
                ColorComponent.ResolveErrorColor(), _pitchLabel, _yawLabel, _rollLabel);
        }

        public override void Update()
        {
            if (!ResolveReferenceAndTarget())
            {
                _viewModel.ShowWarning(_dockingFailureMessage, true);
                ResetDistanceRate();
                return;
            }

            Vector3D positionOffset;
            Vector3D rotationOffset;
            Vector3D velocityOffset;
            GetDockingOffset(_referenceBlock, _targetBlock, out positionOffset, out rotationOffset);
            if (Math.Abs(rotationOffset.X) > MAX_TARGET_ANGLE_DEGREES ||
                Math.Abs(rotationOffset.Y) > MAX_TARGET_ANGLE_DEGREES)
            {
                _targetBlock = null;
                _viewModel.ShowWarning(_noTargetMessage, true);
                ResetDistanceRate();
                return;
            }

            GetVelocityOffset(_referenceBlock, _targetBlock, out velocityOffset);
            var dockingAxisDistance = GetDockingAxisOffset(_referenceBlock, positionOffset);
            var closingVelocity = GetDockingAxisOffset(_referenceBlock, velocityOffset);
            _displayReferenceWarning = null;
            RemapOffsetsToDisplayReference(ref positionOffset, ref rotationOffset, ref velocityOffset);
            _viewModel.ShowWarning(_displayReferenceWarning, false);
            var targetGridName = FormatingHelper.TrimName(_targetBlock.CubeGrid?.CustomName);
            _viewModel.Update(positionOffset, rotationOffset, velocityOffset, GetStatusColor(),
                dockingAxisDistance, GetClosingDistanceRate(dockingAxisDistance), closingVelocity,
                _referenceBlock.CustomName ?? string.Empty,
                (string.IsNullOrEmpty(targetGridName) ? string.Empty : targetGridName + " > ") +
                (_targetBlock.CustomName ?? string.Empty));
        }

        void ResetDistanceRate()
        {
            _lastDockingAxisDistance = double.NaN;
            _lastDockingAxisDistanceFrame = -1L;
        }

        public override List<MySprite> GetSprites() { return new List<MySprite>(); }

        public override void Close()
        {
            ClearParkingConnectorSearch();
            base.Close();
        }

        IMyTerminalBlock GetConfiguredReferenceBlock()
        {
            if (DockableReferenceComponent.EntityId == 0L)
                return null;

            var entity = MyAPIGateway.Entities.GetEntityById(DockableReferenceComponent.EntityId) as IMyTerminalBlock;
            if (entity == null || entity.MarkedForClose || !IsReferenceBlockCandidate(entity))
                return null;

            return entity;
        }

        bool ResolveReferenceAndTarget()
        {
            _dockingFailureMessage = _selectReferenceMessage;
            _referenceBlock = GetConfiguredReferenceBlock();
            if (_referenceBlock != null)
            {
                ClearParkingConnectorSearch();
                _autoParkingReferenceBlock = null;
                _targetBlock = GetClosestTarget(_referenceBlock);
                _dockingFailureMessage = _noTargetMessage;
                return _targetBlock != null;
            }

            if (DockableReferenceComponent.EntityId != 0L)
                return false;

            _dockingFailureMessage = _noTargetMessage;
            _referenceBlock = GetAutoParkingReferenceBlock();
            if (_referenceBlock != null)
            {
                _targetBlock = GetClosestTarget(_referenceBlock);
                if (_targetBlock != null)
                    return true;

                _autoParkingReferenceBlock = null;
            }

            AdvanceParkingConnectorSearch();
            return _autoParkingReferenceBlock != null && _targetBlock != null;
        }

        IMyShipConnector GetAutoParkingReferenceBlock()
        {
            if (_autoParkingReferenceBlock == null
                || _autoParkingReferenceBlock.MarkedForClose
                || !_autoParkingReferenceBlock.IsParkingEnabled)
                return null;

            return _autoParkingReferenceBlock;
        }

        void AdvanceParkingConnectorSearch()
        {
            if (_parkingConnectorSearch == null)
                _parkingConnectorSearch = SearchParkingConnectorsCoroutine().GetEnumerator();

            if (_parkingConnectorSearch.MoveNext())
                return;

            ClearParkingConnectorSearch();
        }

        void ClearParkingConnectorSearch()
        {
            if (_parkingConnectorSearch != null)
                _parkingConnectorSearch.Dispose();

            _parkingConnectorSearch = null;
        }

        IEnumerable<bool> SearchParkingConnectorsCoroutine()
        {
            _parkingSearchGrids.Clear();

            if (Block?.CubeGrid == null)
                yield break;

            MyAPIGateway.GridGroups.GetGroup(Block.CubeGrid, GridLinkTypeEnum.Logical, _parkingSearchGrids);
            if (_parkingSearchGrids.Count == 0 || !_parkingSearchGrids.Contains(Block.CubeGrid))
                _parkingSearchGrids.Add(Block.CubeGrid);

            for (int i = 0; i < _parkingSearchGrids.Count; i++)
            {
                var grid = _parkingSearchGrids[i];
                if (grid == null)
                    continue;

                _parkingSearchBlocks.Clear();
                grid.GetBlocks(_parkingSearchBlocks);

                for (int j = 0; j < _parkingSearchBlocks.Count; j++)
                {
                    var connector = _parkingSearchBlocks[j].FatBlock as IMyShipConnector;
                    if (connector == null || connector.MarkedForClose || !connector.IsParkingEnabled)
                        continue;

                    var target = FindClosestTarget(connector);
                    if (target != null)
                    {
                        _autoParkingReferenceBlock = connector;
                        _referenceBlock = connector;
                        _targetBlock = target;
                        _targetSearchReferenceBlockId = connector.EntityId;
                        _lastTargetSearchFrame = MyAPIGateway.Session?.GameplayFrameCounter ?? 0L;
                        yield break;
                    }

                    yield return true;
                }
            }
        }

        IMyTerminalBlock GetClosestTarget(IMyTerminalBlock reference)
        {
            var frame = MyAPIGateway.Session?.GameplayFrameCounter ?? 0L;
            var referenceChanged = _targetSearchReferenceBlockId != reference.EntityId;
            if (referenceChanged)
            {
                _targetSearchReferenceBlockId = reference.EntityId;
                _lastTargetSearchFrame = frame - TARGET_SEARCH_DELAY_TICKS;
                _targetBlock = null;
            }

            if (_targetBlock != null && _targetBlock.MarkedForClose)
                _targetBlock = null;

            if (frame - _lastTargetSearchFrame < TARGET_SEARCH_DELAY_TICKS)
                return _targetBlock;

            _lastTargetSearchFrame = frame;
            _targetBlock = FindClosestTarget(reference);
            return _targetBlock;
        }

        IMyTerminalBlock FindClosestTarget(IMyTerminalBlock reference)
        {
            _mechanicalGroup.Clear();
            MyAPIGateway.GridGroups.GetGroup(reference.CubeGrid, GridLinkTypeEnum.Mechanical, _mechanicalGroup);

            var referencePosition = reference.GetPosition();
            var forwardPosition = referencePosition + reference.WorldMatrix.Forward * (TARGET_SEARCH_RANGE/3);
            var sphere = new BoundingSphereD(forwardPosition, TARGET_SEARCH_RANGE);
            var coneCos = Math.Cos(MathHelper.ToRadians(TARGET_SEARCH_CONE_DEGREES));

            _entityBuffer.Clear();
            _entityBuffer.AddRange(MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere));

            IMyTerminalBlock closest = null;
            var closestDistance = double.MaxValue;
            var referenceIsConnector = reference is IMyShipConnector;
            var referenceIsMergeBlock = reference is IMyShipMergeBlock;

            for (int i = 0; i < _entityBuffer.Count; i++)
            {
                var candidate = _entityBuffer[i] as IMyTerminalBlock;
                if (candidate == null || candidate == reference || candidate.MarkedForClose)
                    continue;

                if (_mechanicalGroup.Contains(candidate.CubeGrid))
                    continue;

                if ((referenceIsConnector && !(candidate is IMyShipConnector)) || (referenceIsMergeBlock && !(candidate is IMyShipMergeBlock)))
                    continue;

                var candidateDirection = candidate.GetPosition() - referencePosition;
                var distance = candidateDirection.Length();
                if (distance <= 0d)
                    continue;

                candidateDirection /= distance;
                if (Vector3D.Dot(candidateDirection, reference.WorldMatrix.Forward) < coneCos)
                    continue;

                if (distance >= closestDistance)
                    continue;

                closest = candidate;
                closestDistance = distance;
            }

            return closest;
        }

        static void GetDockingOffset(IMyTerminalBlock reference, IMyTerminalBlock target,
            out Vector3D positionOffset, out Vector3D rotationOffset)
        {
            var referenceRotation = reference.WorldMatrix;
            referenceRotation.Translation = Vector3D.Zero;

            var targetRotation = target.WorldMatrix;
            targetRotation.Translation = Vector3D.Zero;

            var inverseReferenceRotation = MatrixD.Transpose(referenceRotation);
            positionOffset = Vector3D.Transform(
                GetBlockForwardEdgePosition(target) - GetBlockForwardEdgePosition(reference),
                inverseReferenceRotation);

            targetRotation *= MatrixD.CreateFromAxisAngle(targetRotation.Up, Math.PI);

            var relativeRotation = targetRotation * inverseReferenceRotation;
            Vector3D relativeRadians;
            MatrixD.GetEulerAnglesXYZ(ref relativeRotation, out relativeRadians);

            var rollOffsetTo90 = -Math.Round(MathHelper.ToDegrees(relativeRadians.Z) / 90.0);
            targetRotation *= MatrixD.CreateFromAxisAngle(targetRotation.Forward, (Math.PI / 2.0) * rollOffsetTo90);

            relativeRotation = targetRotation * inverseReferenceRotation;
            MatrixD.GetEulerAnglesXYZ(ref relativeRotation, out relativeRadians);

            rotationOffset = new Vector3D(
                MathHelper.ToDegrees(relativeRadians.X),
                MathHelper.ToDegrees(relativeRadians.Y),
                MathHelper.ToDegrees(relativeRadians.Z));
        }

        static Vector3D GetBlockForwardEdgePosition(IMyTerminalBlock block)
        {
            var localConnectionDirection = GetLocalConnectionDirection(block);
            var connectionDirection = Base6Directions.GetDirection(localConnectionDirection);
            var gridConnectionDirection = block.Orientation.TransformDirection(connectionDirection);
            var forward = Base6Directions.GetIntVector(gridConnectionDirection);
            var blockSize = block.Max - block.Min + Vector3I.One;
            var forwardCellSize =
                Math.Abs(blockSize.X * forward.X) +
                Math.Abs(blockSize.Y * forward.Y) +
                Math.Abs(blockSize.Z * forward.Z);
            var forwardOffset = forwardCellSize * block.CubeGrid.GridSize * 0.5d;
            var worldForward = GetWorldDirection(block.WorldMatrix, localConnectionDirection);

            return block.GetPosition() + worldForward * forwardOffset;
        }

        static Vector3I GetLocalConnectionDirection(IMyTerminalBlock block)
        {
            var connectorDefinition =
                MyDefinitionManager.Static.GetCubeBlockDefinition(block.BlockDefinition) as MyShipConnectorDefinition;
            if (connectorDefinition != null && connectorDefinition.ConnectDirection != Vector3.Zero)
                return ToAxisVector(connectorDefinition.ConnectDirection);

            return Base6Directions.GetIntVector(Base6Directions.Direction.Forward);
        }

        static Vector3I ToAxisVector(Vector3 direction)
        {
            var absX = Math.Abs(direction.X);
            var absY = Math.Abs(direction.Y);
            var absZ = Math.Abs(direction.Z);

            if (absX >= absY && absX >= absZ)
                return new Vector3I(Math.Sign(direction.X), 0, 0);

            if (absY >= absX && absY >= absZ)
                return new Vector3I(0, Math.Sign(direction.Y), 0);

            return new Vector3I(0, 0, Math.Sign(direction.Z));
        }

        static Vector3D GetWorldDirection(MatrixD worldMatrix, Vector3I localDirection)
        {
            var direction =
                worldMatrix.Right * localDirection.X +
                worldMatrix.Up * localDirection.Y +
                worldMatrix.Backward * localDirection.Z;

            if (direction.LengthSquared() <= 0d)
                return worldMatrix.Forward;

            return Vector3D.Normalize(direction);
        }

        static void GetVelocityOffset(IMyTerminalBlock reference, IMyTerminalBlock target, out Vector3D velocityOffset)
        {
            var referenceRotation = reference.WorldMatrix;
            referenceRotation.Translation = Vector3D.Zero;

            var inverseReferenceRotation = MatrixD.Transpose(referenceRotation);
            velocityOffset = Vector3D.TransformNormal(
                target.CubeGrid.LinearVelocity - reference.CubeGrid.LinearVelocity,
                inverseReferenceRotation);
        }

        static double GetDockingAxisOffset(IMyTerminalBlock reference, Vector3D offset)
        {
            var connectionDirection = GetLocalConnectionDirection(reference);
            var axis = new Vector3D(connectionDirection.X, connectionDirection.Y, connectionDirection.Z);
            if (axis.LengthSquared() <= 0d)
                axis = new Vector3D(0d, 0d, -1d);

            axis.Normalize();

            return -Vector3D.Dot(offset, axis);
        }

        void RemapOffsetsToDisplayReference(ref Vector3D positionOffset, ref Vector3D rotationOffset,
            ref Vector3D velocityOffset)
        {
            MatrixD displayRotation;
            if (!TryGetDisplayReferenceRotation(out displayRotation))
                return;

            var referenceRotation = _referenceBlock.WorldMatrix;
            referenceRotation.Translation = Vector3D.Zero;
            displayRotation.Translation = Vector3D.Zero;

            var inverseDisplayRotation = MatrixD.Transpose(displayRotation);
            var positionWorld = Vector3D.TransformNormal(positionOffset, referenceRotation);
            var rotationWorld = Vector3D.TransformNormal(rotationOffset, referenceRotation);
            var velocityWorld = Vector3D.TransformNormal(velocityOffset, referenceRotation);

            positionOffset = Vector3D.TransformNormal(positionWorld, inverseDisplayRotation);
            rotationOffset = Vector3D.TransformNormal(rotationWorld, inverseDisplayRotation);
            velocityOffset = Vector3D.TransformNormal(velocityWorld, inverseDisplayRotation);
        }

        bool TryGetDisplayReferenceRotation(out MatrixD displayRotation)
        {
            displayRotation = MatrixD.Identity;
            switch ((DockingDisplayMode)GeneralComponent.DisplayMode)
            {
                case DockingDisplayMode.LcdReference:
                    displayRotation = Block.WorldMatrix;
                    displayRotation.Translation = Vector3D.Zero;
                    return true;
                case DockingDisplayMode.ControllerReference:
                    if (!Host.TryGetReferenceWorldMatrix((int)ReferenceMode.Controller, out displayRotation))
                    {
                        _displayReferenceWarning = _noCockpitMessage;
                        return false;
                    }
                    
                    displayRotation.Translation = Vector3D.Zero;
                    return true;
                default:
                    return false;
            }
        }

        double GetClosingDistanceRate(double dockingAxisDistance)
        {
            var frame = MyAPIGateway.Session?.GameplayFrameCounter ?? 0L;
            var result = 0d;
            if (!double.IsNaN(_lastDockingAxisDistance) && _lastDockingAxisDistanceFrame >= 0L &&
                frame > _lastDockingAxisDistanceFrame)
            {
                var seconds = (frame - _lastDockingAxisDistanceFrame) / 60d;
                if (seconds > 0d)
                    result = (dockingAxisDistance - _lastDockingAxisDistance) / seconds;
            }

            _lastDockingAxisDistance = dockingAxisDistance;
            _lastDockingAxisDistanceFrame = frame;
            return result;
        }

        Color GetStatusColor()
        {
            var connector = _referenceBlock as IMyShipConnector;
            if (connector == null)
                return GetHeaderColor();

            if (connector.Status == MyShipConnectorStatus.Connectable)
                return ColorComponent.ResolveWarningColor();

            if (connector.Status == MyShipConnectorStatus.Connected)
                return GetHeaderColor();

            return Surface.ScriptForegroundColor;
        }

        enum DockingDisplayMode
        {
            Default = 0,
            LcdReference = 1,
            ControllerReference = 2
        }
    }
}
