using System;
using System.Collections.Generic;

namespace LcdMod.Client.Modules.Power
{
    public sealed class PowerSubtypeSnapshot
    {
        public string Key;
        public string SpriteKey;
        public string TypeId;
        public string SubtypeId;
        public string DisplayName;
        public long RepresentativeBlockEntityId;
        public double CurrentW;
        public double RequiredW;
        public double MaxW;
        public int BlockCount;

        public PowerSubtypeSnapshot Clone()
        {
            return new PowerSubtypeSnapshot
            {
                Key = Key,
                SpriteKey = SpriteKey,
                TypeId = TypeId,
                SubtypeId = SubtypeId,
                DisplayName = DisplayName,
                RepresentativeBlockEntityId = RepresentativeBlockEntityId,
                CurrentW = CurrentW,
                RequiredW = RequiredW,
                MaxW = MaxW,
                BlockCount = BlockCount
            };
        }
    }

    public struct ProducerBreakdown
    {
        public double SolarCurrentOutputW;
        public double SolarMaxOutputW;
        public double WindCurrentOutputW;
        public double WindMaxOutputW;
        public double ReactorCurrentOutputW;
        public double ReactorMaxOutputW;
        public double HydrogenEngineCurrentOutputW;
        public double HydrogenEngineMaxOutputW;
        public double BatteryDischargeOutputW;
        public double BatteryMaxOutputW;
        public double OtherCurrentOutputW;
        public double OtherMaxOutputW;

        public double KnownCurrentOutputW
        {
            get
            {
                return SolarCurrentOutputW + WindCurrentOutputW + ReactorCurrentOutputW +
                       HydrogenEngineCurrentOutputW + BatteryDischargeOutputW + OtherCurrentOutputW;
            }
        }

        public double KnownMaxOutputW
        {
            get
            {
                return SolarMaxOutputW + WindMaxOutputW + ReactorMaxOutputW +
                       HydrogenEngineMaxOutputW + BatteryMaxOutputW + OtherMaxOutputW;
            }
        }
    }

    public struct ConsumerBreakdown
    {
        public double OtherCurrentInputW;
        public double OtherRequiredInputW;
        public double OtherMaxRequiredInputW;
    }

    public struct PowerSnapshot
    {
        public long GameplayFrame;
        public double TotalRequiredInputW;
        public double MaxAvailableW;
        public double KnownCurrentInputW;
        public double ClassifiedRequiredInputW;
        public double UnclassifiedRequiredInputW;
        public double ElectricThrusterCurrentInputW;
        public double ElectricThrusterRequiredInputW;
        public double ElectricThrusterMaxRequiredInputW;
        public double BatteryChargeInputW;
        public double BatteryDischargeOutputW;
        public double StoredEnergyWh;
        public double MaxStoredEnergyWh;
        public ProducerBreakdown Producers;
        public ConsumerBreakdown Consumers;
        public List<PowerSubtypeSnapshot> ProducerSubtypes;
        public List<PowerSubtypeSnapshot> ConsumerSubtypes;
        public List<PowerSubtypeSnapshot> ChargeSubtypes;
        public int AveragedSampleCount;

        public static PowerSnapshot Empty(long gameplayFrame)
        {
            return new PowerSnapshot
            {
                GameplayFrame = gameplayFrame,
                ProducerSubtypes = new List<PowerSubtypeSnapshot>(),
                ConsumerSubtypes = new List<PowerSubtypeSnapshot>(),
                ChargeSubtypes = new List<PowerSubtypeSnapshot>(),
                AveragedSampleCount = 1
            };
        }

        public static void EnsureSubtypeLists(ref PowerSnapshot snapshot)
        {
            if (snapshot.ProducerSubtypes == null)
                snapshot.ProducerSubtypes = new List<PowerSubtypeSnapshot>();
            if (snapshot.ConsumerSubtypes == null)
                snapshot.ConsumerSubtypes = new List<PowerSubtypeSnapshot>();
            if (snapshot.ChargeSubtypes == null)
                snapshot.ChargeSubtypes = new List<PowerSubtypeSnapshot>();
        }
    }
}
