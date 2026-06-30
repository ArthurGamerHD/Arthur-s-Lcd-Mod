using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
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
using ColorExtensions = VRageMath.ColorExtensions;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using MyShipConnectorStatus = Sandbox.ModAPI.Ingame.MyShipConnectorStatus;

using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;

namespace LcdMod.Client.Apps
{
    [LcdApp(11)]
    [ConfigComponent(DOCKABLE_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "DockableReferenceComponent")]
    internal sealed partial class DockingAlignmentApp : App
    {
        const float TARGET_SEARCH_RANGE = 100f;
        const float TARGET_SEARCH_CONE_DEGREES = 45f;
        const long TARGET_SEARCH_DELAY_TICKS = 100L;
        const int ANGLE_TICK_COUNT = 9;
        const float ANGLE_TICK_DEGREES = 5f;
        const float VELOCITY_SCALE = 0.65f;
        const float REFERENCE_MARKER_SCALE = 0.5f;
        const float VECTOR_TEXTURE_SCALE_RANGE = 20f;
        const float MIN_VECTOR_TEXTURE_SCALE = 0f;
        const float MAX_VECTOR_TEXTURE_SCALE = 2f;
        const float MAX_VECTOR_ANGLE_DEGREES = ANGLE_TICK_COUNT * ANGLE_TICK_DEGREES;
        const float MAX_TARGET_ANGLE_DEGREES = MAX_VECTOR_ANGLE_DEGREES;
        const float MAX_VELOCITY_VECTOR_METERS_PER_SECOND = 10f;

        // todo: convert to interactive app
        public override IReadOnlyList<Control> VisualChildren { get; } = new Control[]{};
        
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
        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<MySprite> _staticSprites = new List<MySprite>();
        readonly Dictionary<string, string> _connectorNameCache = new Dictionary<string, string>();

        LayoutMetrics _layout;
        bool _staticSpritesDirty = true;
        int _staticChromeSpriteCount;

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
        double _lastDockingAxisDistance = double.NaN;
        long _lastDockingAxisDistanceFrame = -1L;

        IMyCubeBlock Block => Host.Block;
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface => Host.Surface;
        RectangleF ViewBox => Host.ViewBox;
        float Scale => GeneralComponent.GetScale();
        float FontScale => Host.Surface.FontSize;
        float LayoutScale => Scale * FontScale;
        Color BackgroundColor => Host.BackgroundColor;
        const float FOOTER_HEIGHT = 0f;

        public DockingAlignmentApp(IAppHost host) : base(host)
        {
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
            _sprites.Clear();
            _staticSprites.Clear();
            _staticSpritesDirty = true;
            _connectorNameCache.Clear();
            _pitchLabel = LocHelper.GetLoc("BlockPropertyTitle_ProjectionRotationX");
            _yawLabel = LocHelper.GetLoc("BlockPropertyTitle_ProjectionRotationY");
            _rollLabel = LocHelper.GetLoc("BlockPropertyTitle_ProjectionRotationZ");
            _selectReferenceMessage = string.Format(
                LocHelper.GetLoc(MOD_PREFIX + "DockingAlignment_SelectReference"),
                LocHelper.GetLoc("DisplayName_Block_Connector"),
                LocHelper.GetLoc("DisplayName_Block_MergeBlock"));
            _noTargetMessage = LocHelper.GetLoc(MOD_PREFIX + "DockingAlignment_NoTarget");
            _noCockpitMessage = LocHelper.GetLoc("TssTargetingInfo_NoMainCockpit");
        }

        public override void Update()
        {
            EnsureStaticSprites();

            _sprites.Clear();
            AddStaticChromeSprites();

            if (!ResolveReferenceAndTarget())
            {
                Host.DrawMessage(_sprites, _dockingFailureMessage, "Warning", ColorComponent.ResolveWarningColor(),
                    GeneralComponent.GetScale());
                return;
            }

            _sprites.Clear();
            _sprites.AddRange(_staticSprites);

            Vector3D positionOffset;
            Vector3D rotationOffset;
            Vector3D velocityOffset;
            GetDockingOffset(_referenceBlock, _targetBlock, out positionOffset, out rotationOffset);
            if (Math.Abs(rotationOffset.X) > MAX_TARGET_ANGLE_DEGREES ||
                Math.Abs(rotationOffset.Y) > MAX_TARGET_ANGLE_DEGREES)
            {
                _targetBlock = null;
                _sprites.Clear();
                AddStaticChromeSprites();
                Host.DrawMessage(_sprites, _noTargetMessage, "Warning", ColorComponent.ResolveWarningColor(),
                    GeneralComponent.GetScale());
                return;
            }

            GetVelocityOffset(_referenceBlock, _targetBlock, out velocityOffset);
            var dockingAxisDistance = GetDockingAxisOffset(_referenceBlock, positionOffset);
            var closingVelocity = GetDockingAxisOffset(_referenceBlock, velocityOffset);
            RemapOffsetsToDisplayReference(ref positionOffset, ref rotationOffset, ref velocityOffset);

            DrawAlignment(positionOffset, rotationOffset, velocityOffset, dockingAxisDistance, closingVelocity);
        }

        public override List<MySprite> GetSprites()
        {
            return _sprites;
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
                        Host.DrawMessage(_sprites, _noCockpitMessage, "Warning", ColorComponent.ResolveWarningColor(),
                            GeneralComponent.GetScale());
                        return false;
                    }
                    
                    displayRotation.Translation = Vector3D.Zero;
                    return true;
                default:
                    return false;
            }
        }

        void EnsureStaticSprites()
        {
            if (!_staticSpritesDirty)
                return;

            _staticSpritesDirty = false;
            _staticSprites.Clear();
            Host.AddBackground(_staticSprites);
            Host.DrawTitle(_staticSprites);
            _staticChromeSpriteCount = _staticSprites.Count;

            _layout = CalculateLayoutMetrics();

            var accent = GetHeaderColor();
            var mutedAccent = ColorExtensions.Alpha(accent, 0.66f);

            DrawAlignmentTicks(_staticSprites, _layout.Square, mutedAccent);
            DrawRollArcTicks(_staticSprites, _layout.Square, mutedAccent);
            AddTexture(_staticSprites, "CircleHollow", _layout.Square.Center,
                new Vector2(_layout.MarkerSize * REFERENCE_MARKER_SCALE), mutedAccent, 0f);

            AddText(_staticSprites, _pitchLabel, NormalizedToSquare(_layout.Square, 0.35f, -0.06f),
                _layout.Font * 0.65f, accent, TextAlignment.RIGHT);

            AddText(_staticSprites, _yawLabel,
                new Vector2(_layout.Square.Center.X, _layout.BottomY - _layout.Font * 32f),
                _layout.Font * 0.65f, accent, TextAlignment.CENTER);

            AddText(_staticSprites, _rollLabel,
                new Vector2(_layout.Square.Center.X, _layout.RollArcTopY - _layout.Font * 22f),
                _layout.Font * 0.65f, accent, TextAlignment.CENTER);

            AddTexture(_staticSprites, "SquareSimple",
                new Vector2(ViewBox.Center.X, _layout.FooterTop + _layout.FooterHeight * 0.5f),
                new Vector2(ViewBox.Width, _layout.FooterHeight),
                new Color(BackgroundColor.MulValue(0.8f), 0.5f),
                0f);
        }

        void AddStaticChromeSprites()
        {
            for (int i = 0; i < _staticChromeSpriteCount && i < _staticSprites.Count; i++)
                _sprites.Add(_staticSprites[i]);
        }

        LayoutMetrics CalculateLayoutMetrics()
        {
            var footerHeight = 58f * LayoutScale;
            var referenceTextMargin = ViewBox.Height * 0.02f;
            var contentTop = ContentTop();
            var contentBottom = ViewBox.Bottom - FOOTER_HEIGHT - footerHeight;
            var contentHeight = Math.Max(0f, contentBottom - contentTop);
            var content = new RectangleF(ViewBox.X, contentTop, ViewBox.Width, contentHeight);
            var squareSize = Math.Min(content.Width, content.Height);
            var square = new RectangleF(
                content.Center.X - squareSize / 2f,
                content.Center.Y - squareSize / 2f,
                squareSize,
                squareSize);

            var font = Math.Max(0.45f, Math.Min(0.95f, squareSize / 560f)) * Scale * FontScale;
            var angleAxisLength = square.Width * 0.72f;
            var angleHalfAxis = angleAxisLength * 0.5f;

            return new LayoutMetrics
            {
                FooterHeight = footerHeight,
                ReferenceTextMargin = referenceTextMargin,
                FooterTop = ViewBox.Bottom - FOOTER_HEIGHT - footerHeight,
                Square = square,
                MarkerSize = squareSize * 0.1f,
                Font = font,
                BottomY = square.Bottom - squareSize * 0.08f,
                PitchLineTop = square.Center.Y - angleHalfAxis,
                RollArcTopY = square.Center.Y - squareSize * 0.42f,
                RollArcLeftX = square.Center.X - square.Width * 0.32f
            };
        }

        float ContentTop()
        {
            return Host.TitleVisible ? ViewBox.Y + 40f * LayoutScale : ViewBox.Y;
        }

        void DrawAlignment(Vector3D positionOffset, Vector3D rotationOffset, Vector3D velocityOffset,
            double dockingAxisDistance, double closingVelocity)
        {
            var foreground = Surface.ScriptForegroundColor;
            var statusColor = GetStatusColor();

            DrawAngleGuides(_sprites, _layout.Square, statusColor, (float)rotationOffset.X, (float)rotationOffset.Y);
            DrawRollIndicator(_sprites, _layout.Square, statusColor, (float)rotationOffset.Z);

            var positionVector = new Vector2(
                MathHelper.Clamp(-(float)positionOffset.X * 0.025f, -0.36f, 0.36f),
                MathHelper.Clamp((float)positionOffset.Y * 0.025f, -0.36f, 0.36f));
            AddTexture(_sprites, "AH_VelocityVector", NormalizedToSquare(_layout.Square, positionVector.X, positionVector.Y),
                new Vector2(_layout.MarkerSize * GetVectorTextureScale(positionOffset.Z)), ColorComponent.ResolveErrorColor(), 0f);

            var velocityVector = new Vector2(
                MathHelper.Clamp(-(float)velocityOffset.X / MAX_VELOCITY_VECTOR_METERS_PER_SECOND, -1f, 1f) * 0.36f,
                MathHelper.Clamp((float)velocityOffset.Y / MAX_VELOCITY_VECTOR_METERS_PER_SECOND, -1f, 1f) * 0.36f);


            var velocityOpacity = MathHelper.Clamp((float)velocityOffset.Length(), 0f, 1f);

            AddTexture(_sprites, "AH_VelocityVector", NormalizedToSquare(_layout.Square, velocityVector.X, velocityVector.Y),
                new Vector2(_layout.MarkerSize * VELOCITY_SCALE * GetVectorTextureScale(velocityOffset.Z)),
                statusColor.Alpha(velocityOpacity), 0);

            AddText(_sprites, $"{rotationOffset.X:0.0}°", NormalizedToSquare(_layout.Square, 0.39f, -0.02f),
                _layout.Font, foreground, TextAlignment.LEFT);

            var yawValue = $"{rotationOffset.Y:0.0}°";

            AddText(_sprites, yawValue, new Vector2(_layout.Square.Center.X, _layout.BottomY), _layout.Font,
                foreground, TextAlignment.CENTER);

            AddText(_sprites, $"{rotationOffset.Z:0.0}°",
                new Vector2(_layout.RollArcLeftX, _layout.PitchLineTop), _layout.Font, foreground,
                TextAlignment.RIGHT);

            DrawReferenceNames(_layout.FooterHeight, _layout.ReferenceTextMargin, foreground, _layout.Font * 0.75f,
                yawValue, _layout.Font);
            DrawPositionFooter(positionOffset, rotationOffset, dockingAxisDistance, closingVelocity, _layout.FooterHeight,
                foreground);
        }

        void DrawReferenceNames(float footerHeight, float referenceTextMargin, Color foreground, float font,
            string yawValue, float yawFont)
        {
            var textOffsetY = FormatingHelper.GetSizeInPixel("A", TextFont, font, Surface).Y * 0.5f;
            var y = ViewBox.Bottom - FOOTER_HEIGHT - footerHeight - referenceTextMargin - 8f * LayoutScale - textOffsetY;
            var targetGridName = FormatingHelper.TrimName(_targetBlock.CubeGrid?.CustomName);
            var labels = new[]
            {
                "Ref: " + (_referenceBlock.CustomName ?? string.Empty),
                "Tgt: " + (string.IsNullOrEmpty(targetGridName) ? string.Empty : targetGridName + " > ") +
                (_targetBlock.CustomName ?? string.Empty)
            };

            var columnWidth = ViewBox.Width * 0.5f;
            var margin = 8f * LayoutScale;
            var yawReservedWidth = FormatingHelper.GetSizeInPixel(yawValue, TextFont, yawFont, Surface).X + margin * 2f;
            var centerLeft = ViewBox.Center.X - yawReservedWidth * 0.5f;
            var centerRight = ViewBox.Center.X + yawReservedWidth * 0.5f;
            for (int i = 0; i < labels.Length; i++)
            {
                var left = ViewBox.X + columnWidth * i + margin;
                var right = ViewBox.X + columnWidth * (i + 1) - margin;
                if (i == 0)
                    right = Math.Min(right, centerLeft);
                else
                    left = Math.Max(left, centerRight);

                var availableWidth = Math.Max(0f, right - left);
                var centerX = (left + right) * 0.5f;
                var text = GetTrimmedConnectorName(labels[i], availableWidth, font);

                AddText(_sprites, text, new Vector2(centerX, y), font, foreground, TextAlignment.CENTER);
            }
        }

        string GetTrimmedConnectorName(string source, float availableWidth, float font)
        {
            var cacheKey = source + "|" + ((int)Math.Round(availableWidth)) + "|" + ((int)Math.Round(font * 1000f));
            string cached;
            if (_connectorNameCache.TryGetValue(cacheKey, out cached))
                return cached;

            var sb = new StringBuilder(source ?? string.Empty);
            Host.TrimText(ref sb, availableWidth, font);
            cached = sb.ToString();
            _connectorNameCache[cacheKey] = cached;
            return cached;
        }

        void DrawPositionFooter(Vector3D positionOffset, Vector3D rotationOffset, double dockingAxisDistance,
            double closingVelocity, float footerHeight, Color valueColor)
        {
            var footerTop = ViewBox.Bottom - FOOTER_HEIGHT - footerHeight;
            var row1Y = footerTop + footerHeight * 0.36f;
            var row2Y = footerTop + footerHeight * 0.78f;
            var footerTextScale = Math.Max(0.36f, 0.52f * Scale) * FontScale;
            var footerTextOffsetY = FormatingHelper.GetSizeInPixel("A", TextFont, footerTextScale, Surface).Y * 0.5f;
            var closingDistance = GetClosingDistanceRate(dockingAxisDistance);

            var labels = new[]
            {
                $"L/R {-positionOffset.X:0.00}m",
                $"F/B {positionOffset.Z:0.00}m",
                $"U/D {-positionOffset.Y:0.00}m",
                $"DST {positionOffset.Length():0.##}m",
                $"ADST {dockingAxisDistance:0.00}m",
                $"CDST {closingDistance:0.00}m/s",
                $"CVEL {closingVelocity:0.00}m/s",
                $"R° {rotationOffset.Z:0.0}°"
            };

            var columnWidth = ViewBox.Width / 4f;
            for (int i = 0; i < 4; i++)
            {
                var x = ViewBox.X + columnWidth * (i + 0.5f);
                AddText(_sprites, labels[i], new Vector2(x, row1Y - footerTextOffsetY), footerTextScale, valueColor,
                    TextAlignment.CENTER);
                AddText(_sprites, labels[i + 4], new Vector2(x, row2Y - footerTextOffsetY), footerTextScale, valueColor,
                    TextAlignment.CENTER);
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

        static float GetVectorTextureScale(double value)
        {
            return MathHelper.Clamp(1f + (float)value / VECTOR_TEXTURE_SCALE_RANGE, MIN_VECTOR_TEXTURE_SCALE,
                MAX_VECTOR_TEXTURE_SCALE);
        }

        void DrawRollArcTicks(List<MySprite> sprites, RectangleF square, Color color)
        {
            var tickScale = Scale * FontScale;
            var radius = square.Width * 0.32f;
            var center = new Vector2(square.Center.X, square.Center.Y - square.Height * 0.10f);

            for (int i = -ANGLE_TICK_COUNT; i <= ANGLE_TICK_COUNT; i++)
            {
                if (i == 0)
                    continue;

                var angleDegrees = i * ANGLE_TICK_DEGREES - 90f;
                var angleRadians = MathHelper.ToRadians(angleDegrees);
                var direction = new Vector2((float)Math.Cos(angleRadians), (float)Math.Sin(angleRadians));
                var isEven = Math.Abs(i) % 2 == 0;
                var thickness = isEven ? 2.2f * tickScale : 1.2f * tickScale;
                var tickLength = isEven ? 18f * tickScale : 10f * tickScale;
                var tickColor = color.Alpha(isEven ? 0.75f : 0.35f);
                var tickCenter = center + direction * radius;

                AddTexture(sprites, "SquareSimple", tickCenter, new Vector2(tickLength, thickness), tickColor, angleRadians);
            }
        }

        void DrawRollIndicator(List<MySprite> sprites, RectangleF square, Color indicatorColor, float rollOffsetDegrees)
        {
            var tickScale = Scale * FontScale;
            var radius = square.Width * 0.32f;
            var center = new Vector2(square.Center.X, square.Center.Y - square.Height * 0.10f);
            var rollAngle = MathHelper.Clamp(rollOffsetDegrees, -MAX_VECTOR_ANGLE_DEGREES, MAX_VECTOR_ANGLE_DEGREES);
            var indicatorAngleRadians = MathHelper.ToRadians(rollAngle - 90f);
            var indicatorDirection = new Vector2((float)Math.Cos(indicatorAngleRadians),
                (float)Math.Sin(indicatorAngleRadians));
            AddTexture(sprites, "SquareSimple",
                center + indicatorDirection * radius,
                new Vector2(28f * tickScale, 3.5f * tickScale),
                indicatorColor.Alpha(0.95f),
                indicatorAngleRadians);
        }

        void DrawAngleGuides(List<MySprite> sprites, RectangleF square, Color color, float pitchDegrees, float yawDegrees)
        {
            var axisLength = square.Width * 0.72f;
            var halfAxis = axisLength * 0.5f;
            var thickness = 3f * Scale * FontScale;
            var guideColor = color.Alpha(0.5f);
            var yawOffset = MathHelper.Clamp(yawDegrees / MAX_VECTOR_ANGLE_DEGREES, -1f, 1f) * halfAxis;
            var pitchOffset = MathHelper.Clamp(-pitchDegrees / MAX_VECTOR_ANGLE_DEGREES, -1f, 1f) * halfAxis;

            AddTexture(sprites, "SquareSimple",
                new Vector2(square.Center.X + yawOffset, square.Center.Y),
                new Vector2(thickness, axisLength),
                guideColor,
                0f);

            AddTexture(sprites, "SquareSimple",
                new Vector2(square.Center.X, square.Center.Y + pitchOffset),
                new Vector2(axisLength, thickness),
                guideColor,
                0f);
        }

        void DrawAlignmentTicks(List<MySprite> sprites, RectangleF square, Color color)
        {
            var tickScale = Scale * FontScale;
            var axisLength = square.Width * 0.72f;
            var pixelsPerDegree = axisLength / (MAX_VECTOR_ANGLE_DEGREES * 2f);
            for (int i = -ANGLE_TICK_COUNT; i <= ANGLE_TICK_COUNT; i++)
            {
                if (i == 0)
                    continue;

                var isEven = Math.Abs(i) % 2 == 0;
                var thickness = isEven ? 2.2f * tickScale : 1.2f * tickScale;
                var tickLength = isEven ? 18f * tickScale : 10f * tickScale;
                var tickColor = color.Alpha(isEven ? 0.75f : 0.35f);
                var offset = i * ANGLE_TICK_DEGREES * pixelsPerDegree;

                AddTexture(sprites, "SquareSimple",
                    new Vector2(square.Center.X, square.Center.Y + offset),
                    new Vector2(tickLength, thickness),
                    tickColor,
                    0f);

                AddTexture(sprites, "SquareSimple",
                    new Vector2(square.Center.X + offset, square.Center.Y),
                    new Vector2(thickness, tickLength),
                    tickColor,
                    0f);
            }
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

        static Vector2 NormalizedToSquare(RectangleF square, float x, float y)
        {
            return new Vector2(square.Center.X + x * square.Width, square.Center.Y + y * square.Height);
        }

        void AddTexture(List<MySprite> sprites, string texture, Vector2 position, Vector2 size, Color color, float rotation)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = texture,
                Position = position,
                Size = size,
                Color = color,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = rotation
            });
        }

        void AddText(List<MySprite> sprites, string text, Vector2 position, float scale, Color color,
            TextAlignment alignment)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = position,
                RotationOrScale = scale,
                Color = color,
                Alignment = alignment,
                FontId = TextFont
            });
        }

        struct LayoutMetrics
        {
            public float FooterHeight;
            public float ReferenceTextMargin;
            public float FooterTop;
            public RectangleF Square;
            public float MarkerSize;
            public float Font;
            public float BottomY;
            public float PitchLineTop;
            public float RollArcTopY;
            public float RollArcLeftX;
        }

        enum DockingDisplayMode
        {
            Default = 0,
            LcdReference = 1,
            ControllerReference = 2
        }
    }
}
