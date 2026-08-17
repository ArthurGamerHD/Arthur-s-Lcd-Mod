using System;
using System.Collections.Generic;
using LcdMod.Client.Modules.Defense.Providers.Deflector;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Defense.Providers.EnergyShield
{
    /// <summary>
    /// Shared adapter for Cython's Energy Shields lineage. Both the 2015 mod and
    /// Deflector use the same CustomInfo layout and channel-5854 live packet.
    /// Definition ownership keeps their overlapping subtype IDs unambiguous.
    /// </summary>
    public abstract class CythonPointShieldProvider : IShieldProvider
    {
        const ushort SHIELD_SYNC_CHANNEL = 5854;
        const long LIVE_DATA_TTL_FRAMES = 300L;

        readonly ulong _workshopId;
        readonly HashSet<string> _supportedSubtypes;
        readonly Dictionary<long, LiveShieldValue> _liveValues = new Dictionary<long, LiveShieldValue>();
        readonly Dictionary<long, CachedShieldState> _shieldStates = new Dictionary<long, CachedShieldState>();
        readonly List<IMyTerminalBlock> _shieldBlocks = new List<IMyTerminalBlock>();
        bool _loaded;
        bool _availabilityKnown;
        bool _modAvailable;
        long _gameplayFrame;

        struct LiveShieldValue
        {
            public float CurrentPoints;
            public long ReceivedFrame;
        }

        sealed class CachedShieldState
        {
            public readonly ShieldInfo Info = new ShieldInfo();
            public float CachedCurrentPoints;
            public bool HasCachedInfo;
        }

        protected CythonPointShieldProvider(string name, ulong workshopId, params string[] supportedSubtypes)
        {
            Name = name;
            _workshopId = workshopId;
            _supportedSubtypes = new HashSet<string>(supportedSubtypes ?? new string[0], StringComparer.Ordinal);
        }

        public string Name { get; private set; }

        public void Load()
        {
            RefreshAvailability();
            if (_loaded || !_modAvailable || MyAPIGateway.Multiplayer == null)
                return;

            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(SHIELD_SYNC_CHANNEL, HandleShieldSync);
            _loaded = true;
        }

        public void Update(long gameplayFrame)
        {
            Load();
            _gameplayFrame = gameplayFrame;
        }

        public void Unload()
        {
            if (_loaded && MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(SHIELD_SYNC_CHANNEL, HandleShieldSync);

            _loaded = false;
            _availabilityKnown = false;
            _modAvailable = false;
            _liveValues.Clear();
            _shieldStates.Clear();
            _shieldBlocks.Clear();
        }

        public bool TryUpdateShieldInfo(
            IReadOnlyList<IMyCubeGrid> grids,
            IEnumerable<IMyTerminalBlock> terminalBlocks,
            long gameplayFrame,
            bool refreshCachedData,
            ShieldInfo info)
        {
            RefreshAvailability();
            if (!_modAvailable || info == null)
                return false;

            CollectShieldBlocks(terminalBlocks);
            var representative = SelectRepresentative(_shieldBlocks);
            if (representative == null)
                return false;

            CachedShieldState state;
            if (!_shieldStates.TryGetValue(representative.EntityId, out state))
            {
                state = new CachedShieldState();
                _shieldStates[representative.EntityId] = state;
            }

            if ((refreshCachedData || !state.HasCachedInfo) &&
                !RefreshCachedInfo(representative, gameplayFrame, state) && !state.HasCachedInfo)
                return false;

            ApplyCachedProperties(state.Info, info);
            ApplyLiveCurrent(_shieldBlocks, gameplayFrame, state, info);
            return true;
        }

        void RefreshAvailability()
        {
            if (_availabilityKnown)
                return;

            var mods = MyAPIGateway.Session?.Mods;
            if (mods == null || mods.Count == 0)
                return;

            _availabilityKnown = true;
            for (int i = 0; i < mods.Count; i++)
            {
                if (mods[i].PublishedFileId != _workshopId)
                    continue;

                _modAvailable = true;
                return;
            }
        }

        void CollectShieldBlocks(IEnumerable<IMyTerminalBlock> terminalBlocks)
        {
            _shieldBlocks.Clear();
            if (terminalBlocks == null)
                return;

            foreach (var block in terminalBlocks)
            {
                if (IsOwnedShieldBlock(block))
                    _shieldBlocks.Add(block);
            }
        }

        bool IsOwnedShieldBlock(IMyTerminalBlock block)
        {
            if (block == null || !_supportedSubtypes.Contains(block.BlockDefinition.SubtypeName))
                return false;

            var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(block.BlockDefinition);
            return definition != null && definition.Context != null &&
                   definition.Context.ModItem.PublishedFileId == _workshopId;
        }

        bool RefreshCachedInfo(IMyTerminalBlock block, long gameplayFrame, CachedShieldState state)
        {
            try
            {
                // Both mods build their calculated/static status in AppendingCustomInfo.
                // Force it only on the shared service's 100-frame cache turn.
                block.RefreshCustomInfo();

                DeflectorShieldCustomInfo parsed;
                if (!DeflectorShieldCustomInfoParser.TryParse(block.CustomInfo, out parsed))
                    return false;

                bool useShip = parsed.HasShipCapacity;
                var functional = block as IMyFunctionalBlock;
                ShieldInfo info = state.Info;
                state.CachedCurrentPoints = useShip
                    ? parsed.ShipCurrentPoints
                    : parsed.LocalCurrentPoints;
                info.ProviderName = Name;
                info.RepresentativeEntityId = block.EntityId;
                info.RepresentativeName = block.CustomName ?? string.Empty;
                info.ValueUnit = "Pt";
                info.UseSiPrefixes = true;
                info.MaximumPoints = useShip ? parsed.ShipMaximumPoints : parsed.LocalMaximumPoints;
                info.RechargePointsPerSecond = parsed.RechargePointsPerSecond;
                info.MaximumRechargePointsPerSecond = 0f;
                info.EffectivenessRatio = parsed.EffectivenessRatio;
                info.TicksUntilRecharge = 0;
                info.HasCapacity = useShip ? parsed.HasShipCapacity : parsed.HasLocalCapacity;
                info.HasRecharge = parsed.HasRecharge;
                info.HasMaximumRecharge = false;
                info.HasEffectiveness = parsed.HasEffectiveness;
                info.HasRechargeDelay = false;
                info.IsWorking = functional == null || functional.IsWorking;
                info.LastCachedDataFrame = gameplayFrame;
                state.HasCachedInfo = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        void ApplyLiveCurrent(
            IReadOnlyList<IMyTerminalBlock> blocks,
            long gameplayFrame,
            CachedShieldState state,
            ShieldInfo info)
        {
            if (blocks == null || blocks.Count == 0)
            {
                ApplyCachedCurrent(state, info);
                return;
            }

            float total = 0f;
            long newestFrame = long.MinValue;
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                LiveShieldValue live;
                if (block == null || !_liveValues.TryGetValue(block.EntityId, out live) ||
                    gameplayFrame - live.ReceivedFrame > LIVE_DATA_TTL_FRAMES)
                {
                    ApplyCachedCurrent(state, info);
                    return;
                }

                total += live.CurrentPoints;
                if (live.ReceivedFrame > newestFrame)
                    newestFrame = live.ReceivedFrame;
            }

            info.CurrentPoints = total;
            info.UsesLiveData = true;
            info.LastLiveDataFrame = newestFrame;
        }

        static void ApplyCachedProperties(ShieldInfo cached, ShieldInfo info)
        {
            info.ProviderName = cached.ProviderName;
            info.RepresentativeEntityId = cached.RepresentativeEntityId;
            info.RepresentativeName = cached.RepresentativeName;
            info.ValueUnit = cached.ValueUnit;
            info.UseSiPrefixes = cached.UseSiPrefixes;
            info.MaximumPoints = cached.MaximumPoints;
            info.RechargePointsPerSecond = cached.RechargePointsPerSecond;
            info.MaximumRechargePointsPerSecond = cached.MaximumRechargePointsPerSecond;
            info.EffectivenessRatio = cached.EffectivenessRatio;
            info.TicksUntilRecharge = cached.TicksUntilRecharge;
            info.IsWorking = cached.IsWorking;
            info.HasCapacity = cached.HasCapacity;
            info.HasRecharge = cached.HasRecharge;
            info.HasMaximumRecharge = cached.HasMaximumRecharge;
            info.HasEffectiveness = cached.HasEffectiveness;
            info.HasRechargeDelay = cached.HasRechargeDelay;
            info.LastCachedDataFrame = cached.LastCachedDataFrame;
        }

        static void ApplyCachedCurrent(CachedShieldState state, ShieldInfo info)
        {
            info.CurrentPoints = state.CachedCurrentPoints;
            info.UsesLiveData = false;
            info.LastLiveDataFrame = 0L;
        }

        static IMyTerminalBlock SelectRepresentative(IReadOnlyList<IMyTerminalBlock> blocks)
        {
            if (blocks == null)
                return null;

            IMyTerminalBlock fallback = null;
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null || block.Closed || block.MarkedForClose)
                    continue;

                if (fallback == null)
                    fallback = block;

                var functional = block as IMyFunctionalBlock;
                if (functional != null && functional.Enabled && functional.IsFunctional)
                    return block;
            }

            return fallback;
        }

        void HandleShieldSync(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            if (!fromServer || data == null || data.Length != 12)
                return;

            long entityId = BitConverter.ToInt64(data, 0);
            float currentPoints = BitConverter.ToSingle(data, 8);
            if (entityId == 0L || currentPoints < 0f || float.IsNaN(currentPoints) || float.IsInfinity(currentPoints))
                return;

            _liveValues[entityId] = new LiveShieldValue
            {
                CurrentPoints = currentPoints,
                ReceivedFrame = MyAPIGateway.Session != null
                    ? MyAPIGateway.Session.GameplayFrameCounter
                    : _gameplayFrame
            };
        }
    }
}
