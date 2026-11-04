using System;
using System.Collections.Generic;
using LcdMod.Client.Helpers;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Power
{
    public sealed class SePowerCollector
    {
        static readonly MyDefinitionId ElectricityId = PowerBlockMath.ElectricityId;
        const string ELECTRIC_THRUSTER_SUBTYPE_KEY = "PowerConsumer/ElectricThruster";
        const string GYROSCOPE_SUBTYPE_KEY = "PowerConsumer/Gyroscope";
        const string UNKNOWN_SUBTYPE_KEY = "Power/Unknown";
        const double MIN_UNKNOWN_W = 10.0;
        const double MIN_UNKNOWN_RATIO = 1e-4;
        readonly HashSet<MyResourceSinkComponent> _sinks = new HashSet<MyResourceSinkComponent>(ReferenceIdentityComparer<MyResourceSinkComponent>.Instance);
        readonly HashSet<long> _producerIds = new HashSet<long>();
        readonly HashSet<long> _batteryIds = new HashSet<long>();
        readonly HashSet<long> _jumpDriveIds = new HashSet<long>();
        readonly HashSet<long> _terminalIds = new HashSet<long>();
        readonly HashSet<long> _gyroIds = new HashSet<long>();
        readonly HashSet<long> _thrustIds = new HashSet<long>();
        readonly Dictionary<string, int> _syntheticSpriteKeyCounts = new Dictionary<string, int>(StringComparer.Ordinal);

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
            CollectGyros(scopedGrids, ref snapshot);
            CountElectricThrusters(scopedGrids, snapshot.ConsumerSubtypes);

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
            _gyroIds.Clear();
            _thrustIds.Clear();
            _syntheticSpriteKeyCounts.Clear();
        }

        void FinalizeScopedTotals(ref PowerSnapshot snapshot)
        {
            AddUnknownBalance(ref snapshot);
            snapshot.KnownCurrentInputW = snapshot.ElectricThrusterCurrentInputW +
                                          snapshot.BatteryChargeInputW +
                                          snapshot.Consumers.OtherCurrentInputW;
            snapshot.ClassifiedRequiredInputW = snapshot.ElectricThrusterRequiredInputW +
                                                snapshot.Consumers.OtherRequiredInputW;
            snapshot.TotalRequiredInputW = Math.Max(snapshot.KnownCurrentInputW,
                snapshot.ClassifiedRequiredInputW + snapshot.BatteryChargeInputW);
            snapshot.UnclassifiedRequiredInputW = Math.Max(0,
                snapshot.TotalRequiredInputW - snapshot.ClassifiedRequiredInputW);
            snapshot.TotalRequiredInputW += snapshot.Consumers.UnknownExportW;
            snapshot.MaxAvailableW = snapshot.Producers.KnownMaxOutputW;
        }

        // Everything produced in scope is consumed somewhere, so a gap between the two is power that left or entered
        // the scope (connectors outside a mechanical scope) or went to sinks that belong to no block, such as a
        // character recharging its suit.
        void AddUnknownBalance(ref PowerSnapshot snapshot)
        {
            double produced = snapshot.Producers.KnownCurrentOutputW;
            double consumed = snapshot.ElectricThrusterCurrentInputW + snapshot.BatteryChargeInputW +
                              snapshot.Consumers.OtherCurrentInputW;
            double balance = produced - consumed;
            if (Math.Abs(balance) < Math.Max(MIN_UNKNOWN_W, Math.Max(produced, consumed) * MIN_UNKNOWN_RATIO))
                return;

            string name = LocHelper.GetLoc("BlockOwner_Unknown");
            if (balance > 0)
            {
                snapshot.Consumers.UnknownExportW = balance;
                AddSyntheticSubtypeValue(snapshot.ConsumerSubtypes, null, UNKNOWN_SUBTYPE_KEY, string.Empty, string.Empty,
                    name, balance, balance, balance, 0);
            }
            else
            {
                snapshot.Producers.UnknownImportW = -balance;
                AddSyntheticSubtypeValue(snapshot.ProducerSubtypes, null, UNKNOWN_SUBTYPE_KEY, string.Empty, string.Empty,
                    name, -balance, -balance, -balance, 0);
            }
        }

        void CollectProducers(IReadOnlyList<IMyCubeGrid> grids, ref PowerSnapshot snapshot)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grids[i]);
                if (logic == null)
                    continue;

                var producers = logic.Blocks.PowerProducers;
                for (int p = 0; p < producers.Count; p++)
                {
                    var producer = producers[p];
                    if (producer == null || !_producerIds.Add(producer.EntityId))
                        continue;

                    double cur = MegaWattsToWatts(producer.CurrentOutput);
                    double max = MegaWattsToWatts(PowerBlockMath.AvailableOutput(producer));
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

                var batteries = logic.Blocks.BatteryBlocks;
                for (int b = 0; b < batteries.Count; b++)
                {
                    var battery = batteries[b];
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

                var jumpDrives = logic.Blocks.JumpDrives;
                for (int j = 0; j < jumpDrives.Count; j++)
                {
                    var jumpDrive = jumpDrives[j];
                    if (jumpDrive == null || !_jumpDriveIds.Add(jumpDrive.EntityId))
                        continue;

                    double storedWh = jumpDrive.CurrentStoredPower * 1000000.0;
                    double maxWh = jumpDrive.MaxStoredPower * 1000000.0;
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

                var terminals = logic.Blocks.TerminalBlocks;
                for (int t = 0; t < terminals.Count; t++)
                {
                    var terminal = terminals[t];
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
                        // ignored
                    }

                    if (!TryRegisterElectricSink(sink))
                        continue;

                    if (sink != null)
                    {
                        double current = MegaWattsToWatts(sink.CurrentInputByType(ElectricityId));
                        double required = MegaWattsToWatts(sink.RequiredInputByType(ElectricityId));
                        double maxRequired = MegaWattsToWatts(sink.MaxRequiredInputByType(ElectricityId));

                        if (terminal is IMyThrust)
                        {
                            snapshot.ElectricThrusterCurrentInputW += current;
                            snapshot.ElectricThrusterRequiredInputW += required;
                            snapshot.ElectricThrusterMaxRequiredInputW += maxRequired;
                            AddSyntheticSubtypeValue(snapshot.ConsumerSubtypes, terminal, ELECTRIC_THRUSTER_SUBTYPE_KEY,
                                "PowerConsumer", "ElectricThruster", LocHelper.GetLoc("HudEnergyGroupThrust"), current, required,
                                maxRequired, 0);
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
        }

        // Gyros have no sink of their own; MyGridGyroSystem draws for all of them through one grid-level sink that a
        // block scan cannot see.
        void CollectGyros(IReadOnlyList<IMyCubeGrid> grids, ref PowerSnapshot snapshot)
        {
            string name = LocHelper.GetLoc("DisplayName_BlockGroup_Gyroscopes");
            for (int i = 0; i < grids.Count; i++)
            {
                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grids[i]);
                if (logic == null)
                    continue;

                var gyros = logic.Blocks.Gyros;
                for (int g = 0; g < gyros.Count; g++)
                {
                    var gyro = gyros[g];
                    if (gyro == null || !_gyroIds.Add(gyro.EntityId))
                        continue;

                    double required = MegaWattsToWatts(PowerBlockMath.GyroRequiredInput(gyro));
                    if (required <= 0)
                        continue;

                    double current = gyro.IsWorking ? required : 0;
                    snapshot.Consumers.OtherCurrentInputW += current;
                    snapshot.Consumers.OtherRequiredInputW += required;
                    snapshot.Consumers.OtherMaxRequiredInputW += required;
                    AddSyntheticSubtypeValue(snapshot.ConsumerSubtypes, gyro, GYROSCOPE_SUBTYPE_KEY, "PowerConsumer",
                        "Gyroscope", name, current, required, required);
                }
            }
        }

        // A grid's electric thrusters share one sink, attached to a single thruster, so the thruster row counts the
        // electric thrusters themselves instead of the sinks found.
        void CountElectricThrusters(IReadOnlyList<IMyCubeGrid> grids, List<PowerSubtypeSnapshot> consumers)
        {
            var entry = FindSubtypeEntry(consumers, ELECTRIC_THRUSTER_SUBTYPE_KEY);
            if (entry == null)
                return;

            int count = 0;
            for (int i = 0; i < grids.Count; i++)
            {
                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grids[i]);
                if (logic == null)
                    continue;

                var thrusts = logic.Blocks.Thrusts;
                for (int t = 0; t < thrusts.Count; t++)
                {
                    var thrust = thrusts[t];
                    if (thrust != null && _thrustIds.Add(thrust.EntityId) && PowerBlockMath.IsElectricThruster(thrust))
                        count++;
                }
            }

            entry.BlockCount = count;
        }

        void AddSubtypeValue(List<PowerSubtypeSnapshot> entries, IMyTerminalBlock block, double currentW, double requiredW, double maxW)
        {
            if (entries == null || block == null)
                return;

            string typeId;
            string subtypeId;
            TryGetBlockDefinitionIds(block, out typeId, out subtypeId);
            var key = typeId + "/" + subtypeId;
            var displayName = GetSubtypeDisplayName(block, subtypeId);
            AddSyntheticSubtypeValue(entries, block, key, typeId, subtypeId, displayName, currentW, requiredW, maxW);
        }

        void AddSyntheticSubtypeValue(List<PowerSubtypeSnapshot> entries, IMyTerminalBlock block, string key,
            string typeId, string subtypeId, string displayName, double currentW, double requiredW, double maxW,
            int blockCount = 1)
        {
            if (entries == null)
                return;

            string spriteKey = block == null ? string.Empty : GetSpriteKey(block);
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
            entry.BlockCount += blockCount;
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

        static void TryGetBlockDefinitionIds(IMyTerminalBlock block, out string typeId, out string subtypeId)
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
                typeId = string.Empty;
                subtypeId = string.Empty;
            }
        }

        static string GetSpriteKey(IMyTerminalBlock block)
        {
            string typeId;
            string subtypeId;
            TryGetBlockDefinitionIds(block, out typeId, out subtypeId);
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
                return "Error";
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
