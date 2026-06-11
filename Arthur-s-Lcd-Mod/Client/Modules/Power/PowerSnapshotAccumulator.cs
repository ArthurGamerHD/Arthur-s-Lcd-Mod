using System;
using System.Collections.Generic;

namespace LcdMod.Client.Modules.Power
{
    public sealed class PowerSnapshotAccumulator
    {
        PowerSnapshot _sum;
        int _weight;
        long _lastFrame;

        public int Count { get { return _weight; } }

        public void Add(PowerSnapshot snapshot)
        {
            int weight = snapshot.AveragedSampleCount > 0 ? snapshot.AveragedSampleCount : 1;
            _lastFrame = snapshot.GameplayFrame;
            _weight += weight;
            AddWeighted(ref _sum, snapshot, weight);
        }

        public PowerSnapshot DrainAverage()
        {
            if (_weight <= 0)
                return PowerSnapshot.Empty(_lastFrame);

            var avg = Divide(_sum, _weight);
            avg.GameplayFrame = _lastFrame;
            avg.AveragedSampleCount = _weight;
            Clear();
            return avg;
        }

        public void Clear()
        {
            _sum = new PowerSnapshot();
            _weight = 0;
            _lastFrame = 0;
        }

        static void AddWeighted(ref PowerSnapshot sum, PowerSnapshot value, int weight)
        {
            sum.TotalRequiredInputW += value.TotalRequiredInputW * weight;
            sum.MaxAvailableW += value.MaxAvailableW * weight;
            sum.KnownCurrentInputW += value.KnownCurrentInputW * weight;
            sum.ClassifiedRequiredInputW += value.ClassifiedRequiredInputW * weight;
            sum.UnclassifiedRequiredInputW += value.UnclassifiedRequiredInputW * weight;
            sum.ElectricThrusterCurrentInputW += value.ElectricThrusterCurrentInputW * weight;
            sum.ElectricThrusterRequiredInputW += value.ElectricThrusterRequiredInputW * weight;
            sum.ElectricThrusterMaxRequiredInputW += value.ElectricThrusterMaxRequiredInputW * weight;
            sum.BatteryChargeInputW += value.BatteryChargeInputW * weight;
            sum.BatteryDischargeOutputW += value.BatteryDischargeOutputW * weight;
            sum.StoredEnergyWh += value.StoredEnergyWh * weight;
            sum.MaxStoredEnergyWh += value.MaxStoredEnergyWh * weight;
            sum.Producers.SolarCurrentOutputW += value.Producers.SolarCurrentOutputW * weight;
            sum.Producers.SolarMaxOutputW += value.Producers.SolarMaxOutputW * weight;
            sum.Producers.WindCurrentOutputW += value.Producers.WindCurrentOutputW * weight;
            sum.Producers.WindMaxOutputW += value.Producers.WindMaxOutputW * weight;
            sum.Producers.ReactorCurrentOutputW += value.Producers.ReactorCurrentOutputW * weight;
            sum.Producers.ReactorMaxOutputW += value.Producers.ReactorMaxOutputW * weight;
            sum.Producers.HydrogenEngineCurrentOutputW += value.Producers.HydrogenEngineCurrentOutputW * weight;
            sum.Producers.HydrogenEngineMaxOutputW += value.Producers.HydrogenEngineMaxOutputW * weight;
            sum.Producers.BatteryDischargeOutputW += value.Producers.BatteryDischargeOutputW * weight;
            sum.Producers.BatteryMaxOutputW += value.Producers.BatteryMaxOutputW * weight;
            sum.Producers.OtherCurrentOutputW += value.Producers.OtherCurrentOutputW * weight;
            sum.Producers.OtherMaxOutputW += value.Producers.OtherMaxOutputW * weight;
            sum.Consumers.OtherCurrentInputW += value.Consumers.OtherCurrentInputW * weight;
            sum.Consumers.OtherRequiredInputW += value.Consumers.OtherRequiredInputW * weight;
            sum.Consumers.OtherMaxRequiredInputW += value.Consumers.OtherMaxRequiredInputW * weight;
            PowerSnapshot.EnsureSubtypeLists(ref sum);
            AddWeightedSubtypes(sum.ProducerSubtypes, value.ProducerSubtypes, weight);
            AddWeightedSubtypes(sum.ConsumerSubtypes, value.ConsumerSubtypes, weight);
            AddWeightedSubtypes(sum.ChargeSubtypes, value.ChargeSubtypes, weight);
        }

        static PowerSnapshot Divide(PowerSnapshot sum, int weight)
        {
            double d = Math.Max(1, weight);
            sum.TotalRequiredInputW /= d;
            sum.MaxAvailableW /= d;
            sum.KnownCurrentInputW /= d;
            sum.ClassifiedRequiredInputW /= d;
            sum.UnclassifiedRequiredInputW /= d;
            sum.ElectricThrusterCurrentInputW /= d;
            sum.ElectricThrusterRequiredInputW /= d;
            sum.ElectricThrusterMaxRequiredInputW /= d;
            sum.BatteryChargeInputW /= d;
            sum.BatteryDischargeOutputW /= d;
            sum.StoredEnergyWh /= d;
            sum.MaxStoredEnergyWh /= d;
            sum.Producers.SolarCurrentOutputW /= d;
            sum.Producers.SolarMaxOutputW /= d;
            sum.Producers.WindCurrentOutputW /= d;
            sum.Producers.WindMaxOutputW /= d;
            sum.Producers.ReactorCurrentOutputW /= d;
            sum.Producers.ReactorMaxOutputW /= d;
            sum.Producers.HydrogenEngineCurrentOutputW /= d;
            sum.Producers.HydrogenEngineMaxOutputW /= d;
            sum.Producers.BatteryDischargeOutputW /= d;
            sum.Producers.BatteryMaxOutputW /= d;
            sum.Producers.OtherCurrentOutputW /= d;
            sum.Producers.OtherMaxOutputW /= d;
            sum.Consumers.OtherCurrentInputW /= d;
            sum.Consumers.OtherRequiredInputW /= d;
            sum.Consumers.OtherMaxRequiredInputW /= d;
            DivideSubtypes(sum.ProducerSubtypes, d);
            DivideSubtypes(sum.ConsumerSubtypes, d);
            DivideSubtypes(sum.ChargeSubtypes, d);
            return sum;
        }

        static void AddWeightedSubtypes(List<PowerSubtypeSnapshot> sum, List<PowerSubtypeSnapshot> value, int weight)
        {
            if (sum == null || value == null)
                return;

            for (int i = 0; i < value.Count; i++)
            {
                var source = value[i];
                if (source == null || string.IsNullOrEmpty(source.Key))
                    continue;

                var target = FindSubtypeEntry(sum, source.Key);
                if (target == null)
                {
                    target = source.Clone();
                    target.CurrentW = 0;
                    target.RequiredW = 0;
                    target.MaxW = 0;
                    sum.Add(target);
                }

                target.CurrentW += source.CurrentW * weight;
                target.RequiredW += source.RequiredW * weight;
                target.MaxW += source.MaxW * weight;
                if (source.BlockCount > target.BlockCount)
                    target.BlockCount = source.BlockCount;
            }
        }

        static void DivideSubtypes(List<PowerSubtypeSnapshot> entries, double divisor)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                    continue;

                entry.CurrentW /= divisor;
                entry.RequiredW /= divisor;
                entry.MaxW /= divisor;
            }
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
    }
}
