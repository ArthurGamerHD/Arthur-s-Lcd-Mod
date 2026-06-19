using System;
using System.Collections.Generic;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using MyObjectBuilder_GasProperties = VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties;

namespace LcdMod.Client.Modules.Power
{
    public sealed class SePowerCollector
    {
        static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        const string ElectricThrusterSubtypeKey = "PowerConsumer/ElectricThruster";
        const string ElectricThrusterDisplayName = "Thrusters";
        readonly HashSet<MyResourceSinkComponent> _sinks = new HashSet<MyResourceSinkComponent>(ReferenceIdentityComparer<MyResourceSinkComponent>.Instance);
        readonly HashSet<long> _producerIds = new HashSet<long>();
        readonly HashSet<long> _batteryIds = new HashSet<long>();
        readonly HashSet<long> _jumpDriveIds = new HashSet<long>();
        readonly HashSet<long> _terminalIds = new HashSet<long>();
        readonly Dictionary<string, int> _syntheticSpriteKeyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<IMyPowerProducer> _producers = new List<IMyPowerProducer>();
        readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        readonly List<IMyJumpDrive> _jumpDrives = new List<IMyJumpDrive>();
        readonly List<IMyTerminalBlock> _terminals = new List<IMyTerminalBlock>();

        public PowerSnapshot Collect(IReadOnlyList<IMyCubeGrid> scopedGrids, long gameplayFrame)
        {
            var snapshot = PowerSnapshot.Empty(gameplayFrame);
            ClearScratch();

            if (scopedGrids == null)
                return snapshot;

            CollectProducers(scopedGrids, ref snapshot);
            CollectBatteries(scopedGrids, ref snapshot);
            CollectJumpDrives(scopedGrids, ref snapshot);
            CollectTerminalConsumers(scopedGrids, ref snapshot);

            FinalizeScopedTotals(ref snapshot);
            return snapshot;
        }

        void ClearScratch()
        {
            _sinks.Clear();
            _producerIds.Clear();
            _batteryIds.Clear();
            _jumpDriveIds.Clear();
            _terminalIds.Clear();
            _syntheticSpriteKeyCounts.Clear();
            _producers.Clear();
            _batteries.Clear();
            _jumpDrives.Clear();
            _terminals.Clear();
        }

        static void FinalizeScopedTotals(ref PowerSnapshot snapshot)
        {
            snapshot.KnownCurrentInputW = snapshot.ElectricThrusterCurrentInputW +
                                          snapshot.BatteryChargeInputW +
                                          snapshot.Consumers.OtherCurrentInputW;
            snapshot.ClassifiedRequiredInputW = snapshot.ElectricThrusterRequiredInputW +
                                                snapshot.Consumers.OtherRequiredInputW;
            snapshot.TotalRequiredInputW = Math.Max(snapshot.KnownCurrentInputW,
                snapshot.ClassifiedRequiredInputW + snapshot.BatteryChargeInputW);
            snapshot.UnclassifiedRequiredInputW = Math.Max(0,
                snapshot.TotalRequiredInputW - snapshot.ClassifiedRequiredInputW);
            snapshot.MaxAvailableW = snapshot.Producers.KnownMaxOutputW;
        }

        void CollectProducers(IReadOnlyList<IMyCubeGrid> grids, ref PowerSnapshot snapshot)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grids[i]);
                if (logic == null)
                    continue;

                _producers.Clear();
                _producers.AddRange(logic.GetTerminalBlocks<IMyPowerProducer>());
                for (int p = 0; p < _producers.Count; p++)
                {
                    var producer = _producers[p];
                    if (producer == null || !_producerIds.Add(producer.EntityId))
                        continue;

                    double cur = MegaWattsToWatts(producer.CurrentOutput);
                    double max = MegaWattsToWatts(producer.MaxOutput);
                    if (producer is IMyBatteryBlock)
                    {
                        snapshot.Producers.BatteryDischargeOutputW += cur;
                        snapshot.Producers.BatteryMaxOutputW += max;
                    }
                    else if (producer is IMySolarPanel)
                    {
                        snapshot.Producers.SolarCurrentOutputW += cur;
                        snapshot.Producers.SolarMaxOutputW += max;
                    }
                    else if (producer is IMyWindTurbine)
                    {
                        snapshot.Producers.WindCurrentOutputW += cur;
                        snapshot.Producers.WindMaxOutputW += max;
                    }
                    else if (producer is IMyReactor)
                    {
                        snapshot.Producers.ReactorCurrentOutputW += cur;
                        snapshot.Producers.ReactorMaxOutputW += max;
                    }
                    else if (IsHydrogenEngine(producer))
                    {
                        snapshot.Producers.HydrogenEngineCurrentOutputW += cur;
                        snapshot.Producers.HydrogenEngineMaxOutputW += max;
                    }
                    else
                    {
                        snapshot.Producers.OtherCurrentOutputW += cur;
                        snapshot.Producers.OtherMaxOutputW += max;
                    }

                    AddSubtypeValue(snapshot.ProducerSubtypes, producer, cur, cur, max);
                }
            }
        }

        void CollectBatteries(IReadOnlyList<IMyCubeGrid> grids, ref PowerSnapshot snapshot)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grids[i]);
                if (logic == null)
                    continue;

                _batteries.Clear();
                _batteries.AddRange(logic.GetTerminalBlocks<IMyBatteryBlock>());
                for (int b = 0; b < _batteries.Count; b++)
                {
                    var battery = _batteries[b];
                    if (battery == null || !_batteryIds.Add(battery.EntityId))
                        continue;

                    snapshot.BatteryChargeInputW += MegaWattsToWatts(battery.CurrentInput);
                    snapshot.BatteryDischargeOutputW += MegaWattsToWatts(battery.CurrentOutput);
                    snapshot.StoredEnergyWh += battery.CurrentStoredPower * 1000000.0;
                    snapshot.MaxStoredEnergyWh += battery.MaxStoredPower * 1000000.0;
                    AddSubtypeValue(snapshot.ChargeSubtypes, battery, battery.CurrentStoredPower * 1000000.0,
                        battery.CurrentStoredPower * 1000000.0, battery.MaxStoredPower * 1000000.0);
                    AddSubtypeValue(snapshot.ConsumerSubtypes, battery, MegaWattsToWatts(battery.CurrentInput),
                        MegaWattsToWatts(battery.CurrentInput), MegaWattsToWatts(battery.MaxInput));
                }
            }
        }

        void CollectJumpDrives(IReadOnlyList<IMyCubeGrid> grids, ref PowerSnapshot snapshot)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grids[i]);
                if (logic == null)
                    continue;

                _jumpDrives.Clear();
                _jumpDrives.AddRange(logic.GetTerminalBlocks<IMyJumpDrive>());
                for (int j = 0; j < _jumpDrives.Count; j++)
                {
                    var jumpDrive = _jumpDrives[j];
                    if (jumpDrive == null || !_jumpDriveIds.Add(jumpDrive.EntityId))
                        continue;

                    double storedWh = jumpDrive.CurrentStoredPower * 1000000.0;
                    double maxWh = jumpDrive.MaxStoredPower * 1000000.0;
                    snapshot.StoredEnergyWh += storedWh;
                    snapshot.MaxStoredEnergyWh += maxWh;
                    AddSubtypeValue(snapshot.ChargeSubtypes, jumpDrive, storedWh, storedWh, maxWh);
                }
            }
        }

        void CollectTerminalConsumers(IReadOnlyList<IMyCubeGrid> grids, ref PowerSnapshot snapshot)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grids[i]);
                if (logic == null)
                    continue;

                _terminals.Clear();
                _terminals.AddRange(logic.GetTerminalBlocks<IMyTerminalBlock>());
                for (int t = 0; t < _terminals.Count; t++)
                {
                    var terminal = _terminals[t];
                    if (terminal == null || terminal is IMyPowerProducer || !_terminalIds.Add(terminal.EntityId))
                        continue;
                    if (terminal is IMyGyro)
                        continue;

                    MyResourceSinkComponent sink = null;
                    try
                    {
                        terminal.Components.TryGet(out sink);
                    }
                    catch
                    {
                    }

                    if (!TryRegisterElectricSink(sink))
                        continue;

                    double current = MegaWattsToWatts(sink.CurrentInputByType(ElectricityId));
                    double required = MegaWattsToWatts(sink.RequiredInputByType(ElectricityId));
                    double maxRequired = MegaWattsToWatts(sink.MaxRequiredInputByType(ElectricityId));

                    if (terminal is IMyThrust)
                    {
                        snapshot.ElectricThrusterCurrentInputW += current;
                        snapshot.ElectricThrusterRequiredInputW += required;
                        snapshot.ElectricThrusterMaxRequiredInputW += maxRequired;
                        AddSyntheticSubtypeValue(snapshot.ConsumerSubtypes, terminal, ElectricThrusterSubtypeKey,
                            "PowerConsumer", "ElectricThruster", ElectricThrusterDisplayName, current, required, maxRequired);
                    }
                    else
                    {
                        snapshot.Consumers.OtherCurrentInputW += current;
                        snapshot.Consumers.OtherRequiredInputW += required;
                        snapshot.Consumers.OtherMaxRequiredInputW += maxRequired;
                        AddSubtypeValue(snapshot.ConsumerSubtypes, terminal, current, required, maxRequired);
                    }
                }
            }
        }

        void AddSubtypeValue(List<PowerSubtypeSnapshot> entries, IMyTerminalBlock block, double currentW, double requiredW, double maxW)
        {
            if (entries == null || block == null)
                return;

            string typeId;
            string subtypeId;
            GetBlockDefinitionIds(block, out typeId, out subtypeId);
            var key = typeId + "/" + subtypeId;
            var displayName = GetSubtypeDisplayName(block, subtypeId);
            AddSyntheticSubtypeValue(entries, block, key, typeId, subtypeId, displayName, currentW, requiredW, maxW);
        }

        void AddSyntheticSubtypeValue(List<PowerSubtypeSnapshot> entries, IMyTerminalBlock block, string key,
            string typeId, string subtypeId, string displayName, double currentW, double requiredW, double maxW)
        {
            if (entries == null)
                return;

            string spriteKey = GetSpriteKey(block);
            var entry = FindSubtypeEntry(entries, key);
            if (entry == null)
            {
                entry = new PowerSubtypeSnapshot
                {
                    Key = key,
                    SpriteKey = spriteKey,
                    TypeId = typeId,
                    SubtypeId = subtypeId,
                    DisplayName = displayName,
                    RepresentativeBlockEntityId = block?.EntityId ?? 0,
                    BlockCount = 0
                };
                entries.Add(entry);
            }

            entry.CurrentW += currentW;
            entry.RequiredW += requiredW;
            entry.MaxW += maxW;
            entry.BlockCount++;
            UpdateRepresentativeSprite(entry, key, spriteKey);
        }

        static PowerSubtypeSnapshot FindSubtypeEntry(List<PowerSubtypeSnapshot> entries, string key)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Key, key, StringComparison.Ordinal))
                    return entries[i];
            }

            return null;
        }

        static void GetBlockDefinitionIds(IMyTerminalBlock block, out string typeId, out string subtypeId)
        {
            typeId = "Unknown";
            subtypeId = string.Empty;
            try
            {
                if (block != null)
                {
                    if (!string.IsNullOrEmpty(block.BlockDefinition.TypeIdString))
                        typeId = block.BlockDefinition.TypeIdString;
                    subtypeId = block.BlockDefinition.SubtypeName ?? string.Empty;
                }
            }
            catch
            {
            }
        }

        static string GetSpriteKey(IMyTerminalBlock block)
        {
            string typeId;
            string subtypeId;
            GetBlockDefinitionIds(block, out typeId, out subtypeId);
            return typeId + "/" + subtypeId;
        }

        void UpdateRepresentativeSprite(PowerSubtypeSnapshot entry, string aggregateKey, string spriteKey)
        {
            if (entry == null || string.IsNullOrEmpty(spriteKey))
                return;

            if (string.Equals(aggregateKey, spriteKey, StringComparison.Ordinal))
            {
                entry.SpriteKey = spriteKey;
                return;
            }

            var counterKey = aggregateKey + "\n" + spriteKey;
            int count;
            _syntheticSpriteKeyCounts.TryGetValue(counterKey, out count);
            count++;
            _syntheticSpriteKeyCounts[counterKey] = count;

            var currentSpriteKey = entry.SpriteKey ?? string.Empty;
            var currentCounterKey = aggregateKey + "\n" + currentSpriteKey;
            int currentCount;
            _syntheticSpriteKeyCounts.TryGetValue(currentCounterKey, out currentCount);
            if (count > currentCount)
                entry.SpriteKey = spriteKey;
        }

        static string GetSubtypeDisplayName(IMyTerminalBlock block, string subtypeId)
        {
            try
            {
                if (block != null && !string.IsNullOrEmpty(block.DefinitionDisplayNameText))
                    return block.DefinitionDisplayNameText;
            }
            catch
            {
            }

            return string.IsNullOrEmpty(subtypeId) ? "Unknown" : subtypeId;
        }

        bool TryRegisterElectricSink(MyResourceSinkComponent sink)
        {
            return sink != null &&
                   AcceptsResource(sink, ElectricityId) &&
                   _sinks.Add(sink);
        }

        static bool AcceptsResource(MyResourceSinkComponent sink, MyDefinitionId resourceId)
        {
            if (sink == null)
                return false;

            foreach (var accepted in sink.AcceptedResources)
            {
                if (accepted == resourceId)
                    return true;
            }

            return false;
        }

        static bool IsHydrogenEngine(IMyPowerProducer producer)
        {
            try
            {
                var typeId = producer.BlockDefinition.TypeIdString ?? string.Empty;
                var subtype = producer.BlockDefinition.SubtypeName ?? string.Empty;
                return typeId.IndexOf("HydrogenEngine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       subtype.IndexOf("HydrogenEngine", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        static double MegaWattsToWatts(float megawatts)
        {
            return megawatts * 1000000.0;
        }
    }
}
