using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using MyObjectBuilder_GasProperties = VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties;

namespace LcdMod.Client.Modules.Power
{
    /// <summary>Power figures computed the way the vanilla resource distributor and blocks compute them.</summary>
    internal static class PowerBlockMath
    {
        public static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");

        const float DEFAULT_BATTERY_RECHARGE_MULTIPLIER = 0.8f;
        const float DEFAULT_JUMP_DRIVE_EFFICIENCY = 0.8f;

        static readonly Dictionary<MyDefinitionId, float> BatteryRechargeMultipliers = new Dictionary<MyDefinitionId, float>();
        static readonly Dictionary<MyDefinitionId, float> JumpDriveEfficiencies = new Dictionary<MyDefinitionId, float>();
        static readonly Dictionary<MyDefinitionId, float> GyroRequiredInputs = new Dictionary<MyDefinitionId, float>();
        static readonly Dictionary<MyDefinitionId, bool> ElectricThrusters = new Dictionary<MyDefinitionId, bool>();

        // Output the distributor can draw from the producer right now, in MW. Unlike MaxOutput, which is the nameplate
        // value for reactors and hydrogen engines, this is 0 while the block is off, unfueled or damaged.
        public static float AvailableOutput(IMyPowerProducer producer)
        {
            if (producer == null)
                return 0f;

            MyResourceSourceComponent source = null;
            try
            {
                producer.Components.TryGet(out source);
            }
            catch
            {
                // ignored
            }

            if (source == null)
                return producer.MaxOutput;
            if (!ProducesElectricity(source) || !source.Enabled || !source.HasCapacityRemainingByType(ElectricityId))
                return 0f;
            return source.MaxOutputByType(ElectricityId);
        }

        // Net change of the stored energy in MW, as MyBatteryBlock applies it.
        public static float BatteryStoredPowerRate(IMyBatteryBlock battery)
        {
            if (battery == null || !battery.IsFunctional || !battery.Enabled)
                return 0f;

            float input = battery.CurrentInput;
            float output = battery.CurrentOutput;
            float multiplier = BatteryRechargeMultiplier(battery);
            switch (battery.ChargeMode)
            {
                case Sandbox.ModAPI.Ingame.ChargeMode.Recharge:
                    return input * multiplier;
                case Sandbox.ModAPI.Ingame.ChargeMode.Discharge:
                    return -output;
                default:
                    float net = input - output;
                    return net > 0f ? net * multiplier : net;
            }
        }

        // Hours until remaining MWh are stored or drained at rate MW.
        public static float HoursAtRate(float remaining, float rate)
        {
            if (Math.Abs(rate) < 1e-9f)
                return float.PositiveInfinity;
            return Math.Max(0f, remaining / Math.Abs(rate));
        }

        public static float BatteryRechargeMultiplier(IMyBatteryBlock battery)
        {
            return GetCached(BatteryRechargeMultipliers, battery, DEFAULT_BATTERY_RECHARGE_MULTIPLIER,
                definition => (definition as MyBatteryBlockDefinition)?.RechargeMultiplier);
        }

        public static float JumpDriveEfficiency(IMyJumpDrive jumpDrive)
        {
            return GetCached(JumpDriveEfficiencies, jumpDrive, DEFAULT_JUMP_DRIVE_EFFICIENCY,
                definition => (definition as MyJumpDriveDefinition)?.PowerEfficiency);
        }

        // Constant draw of a gyroscope in MW. Gyros have no sink of their own: MyGridGyroSystem sums this for every
        // enabled, functional gyro into one grid-level sink, whether or not the gyro is turning.
        public static float GyroRequiredInput(IMyGyro gyro)
        {
            if (gyro == null || !gyro.Enabled || !gyro.IsFunctional)
                return 0f;

            float baseInput = GetCached(GyroRequiredInputs, gyro, 0f,
                definition => (definition as MyGyroDefinition)?.RequiredPowerInput);
            return baseInput * gyro.GyroPower * gyro.PowerConsumptionMultiplier;
        }

        // Whether the thruster burns electricity (ion and atmospheric) rather than a gas such as hydrogen.
        public static bool IsElectricThruster(IMyThrust thrust)
        {
            if (thrust == null)
                return false;

            MyDefinitionId id = thrust.BlockDefinition;
            bool electric;
            if (ElectricThrusters.TryGetValue(id, out electric))
                return electric;

            var definition = GetDefinition(thrust) as MyThrustDefinition;
            var fuel = definition?.FuelConverter;
            electric = fuel == null || fuel.FuelId.IsNull() || ((MyDefinitionId)fuel.FuelId) == ElectricityId;
            ElectricThrusters[id] = electric;
            return electric;
        }

        static bool ProducesElectricity(MyResourceSourceComponent source)
        {
            foreach (var type in source.ResourceTypes)
            {
                if (type == ElectricityId)
                    return true;
            }

            return false;
        }

        static float GetCached(Dictionary<MyDefinitionId, float> cache, IMyTerminalBlock block, float fallback,
            Func<MyCubeBlockDefinition, float?> read)
        {
            if (block == null)
                return fallback;

            MyDefinitionId id = block.BlockDefinition;
            float value;
            if (cache.TryGetValue(id, out value))
                return value;

            value = read(GetDefinition(block)) ?? fallback;
            cache[id] = value;
            return value;
        }

        static MyCubeBlockDefinition GetDefinition(IMyTerminalBlock block)
        {
            try
            {
                return MyDefinitionManager.Static?.GetCubeBlockDefinition(block.BlockDefinition);
            }
            catch
            {
                return null;
            }
        }
    }
}
