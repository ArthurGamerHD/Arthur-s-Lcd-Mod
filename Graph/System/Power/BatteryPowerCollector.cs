using System;
using System.Collections.Generic;
using Graph.Apps.Utility;
using Graph.Helpers;
using Graph.System.Config.Models.Apps;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRageMath;

namespace Graph.System.Power
{
    internal sealed class BatteryPowerCollector : PowerCollector
    {
        const float FullThreshold = 0.995f;
        const float ChargeTrendEpsilonMwh = 0.000001f;
        static readonly FillableTexture Texture = new FillableTexture("Battery", 1f, 55f, 55f, 32f, 10f, "IconEnergy");

        readonly List<IMyBatteryBlock> _visible = new List<IMyBatteryBlock>();
        readonly HashSet<long> _seenBatteryIds = new HashSet<long>();
        readonly List<long> _staleBatteryIds = new List<long>();

        float _averageCharge;
        string _statusText = string.Empty;
        string _rightSideText = string.Empty;
        Color _rightSideColor = Color.White;
        PowerStatusKind _statusKind = PowerStatusKind.None;
        Color _statusColor = Color.White;
        string _timeLabel = "--:--";
        bool _isCharging;

        public BatteryPowerCollector(ScreenConfigPower screenConfig) : base(screenConfig)
        {
        }

        public IReadOnlyList<IMyBatteryBlock> VisibleBatteries => _visible;

        public string TextureName => "Battery";
        public override string FooterPrefix => LocHelper.GetLoc("HudEnergyGroupBatteries");
        public override FillableTexture FillableTexture => Texture;
        public override string StatusText => _statusText;
        public override Color StatusColor => _statusColor;
        public override PowerStatusKind StatusKind => _statusKind;
        public override float AverageCharge => _averageCharge;
        public override bool HasVisibleItems => _visible.Count > 0;
        public override string RightSideText => _rightSideText;
        public override Color RightSideColor => _rightSideColor;
        public override bool DrawCenterIcon => StatusKind <= PowerStatusKind.Charging;

        public override void Collect(GridLogic grid, List<PowerEntry> entries)
        {
            _visible.Clear();
            _averageCharge = 0f;
            _statusText = string.Empty;
            _rightSideText = string.Empty;
            _rightSideColor = ScreenConfigPower.HeaderColor;
            _statusKind = PowerStatusKind.None;
            _statusColor = Color.White;
            _timeLabel = "--:--";
            _isCharging = false;

            if (grid == null)
                return;

            var batteries = grid.GetBatteries();
            const float eps = 0.001f;
            float totalIn = 0f;
            float totalOut = 0f;
            float totalStoredDelta = 0f;
            float sumRatio = 0f;
            float totalStored = 0f;
            float totalMax = 0f;
            int fullCount = 0;
            int batteriesIncreasing = 0;
            int batteriesDecreasing = 0;
            int batteriesRecharging = 0;
            _seenBatteryIds.Clear();

            for (int i = 0; i < batteries.Count; i++)
            {
                var battery = batteries[i];
                _seenBatteryIds.Add(battery.EntityId);
                if (!HideEmpty || battery.MaxStoredPower > 0f)
                {
                    _visible.Add(battery);
                    totalIn += battery.CurrentInput;
                    totalOut += battery.CurrentOutput;
                    totalStored += battery.CurrentStoredPower;
                    totalMax += battery.MaxStoredPower;

                    var isRecharging = battery.ChargeMode == Sandbox.ModAPI.Ingame.ChargeMode.Recharge;
                    if (isRecharging)
                        batteriesRecharging++;

                    var drawChargingIcon = isRecharging;
                    
                    var storedDelta = battery.CurrentInput - battery.CurrentOutput;
                    if (storedDelta > ChargeTrendEpsilonMwh)
                    {
                        totalStoredDelta += storedDelta;
                        batteriesIncreasing++;
                        drawChargingIcon = true;
                    }
                    else if (storedDelta < -ChargeTrendEpsilonMwh)
                    {
                        totalStoredDelta += storedDelta;
                        batteriesDecreasing++;
                        drawChargingIcon = isRecharging;
                    }

                    var ratio = GetRatio(battery);
                    sumRatio += ratio;
                    bool full = ratio >= 1;
                    
                    if (full)
                        fullCount++;

                    entries.Add(new PowerEntry(
                        battery.EntityId,
                        Texture,
                        ratio,
                        FormatingHelper.PercentageToString(ratio),
                        GetBatteryIconColor(ratio),
                        drawChargingIcon || full,
                        blockIcon: BlockIconHelper.GetOrAddTextureForBlock(((MyCubeBlock)battery).BlockDefinition),
                        entity: battery,
                        getDetails: () => BuildBatteryDetails(battery)));
                }
            }
            
            bool hasRechargingBattery = batteriesRecharging > 0;
            bool isTrendingCharging = batteriesIncreasing > 0 && totalStoredDelta > ChargeTrendEpsilonMwh;
            bool isTrendingDischarging = batteriesDecreasing > 0 && totalStoredDelta < -ChargeTrendEpsilonMwh;
            if (_visible.Count > 0 && fullCount == _visible.Count)
            {
                _statusKind = PowerStatusKind.Full;
                _statusColor = ScreenConfigPower.HeaderColor;
                _isCharging = true;
            }
            else if (hasRechargingBattery || totalIn > totalOut + eps)
            {
                _statusKind = PowerStatusKind.Charging;
                _statusColor = ScreenConfigPower.WarningColor;
                _isCharging = true;
            }
            else if (isTrendingCharging)
            {
                _statusKind = PowerStatusKind.Charging;
                _statusColor = ScreenConfigPower.WarningColor;
                _isCharging = true;
            }
            else if (totalOut > totalIn + eps)
            {
                _statusKind = PowerStatusKind.Discharging;
                _statusColor = ScreenConfigPower.ErrorColor;
                _isCharging = false;
            }
            else if (isTrendingDischarging)
            {
                _statusKind = PowerStatusKind.Discharging;
                _statusColor = ScreenConfigPower.ErrorColor;
                _isCharging = false;
            }
            else
            {
                _statusKind = PowerStatusKind.None;
                _statusColor = ScreenConfigPower.HeaderColor;
            }

            if (_visible.Count == 0)
                return;

            _averageCharge = sumRatio / _visible.Count;
            _statusText = GetStatusText();

            float netRate = Math.Abs(totalIn - totalOut);
            if (netRate < eps)
            {
                _timeLabel = "--:--";
                SetRightSideText(_timeLabel, ScreenConfigPower.HeaderColor);
            }
            else if (_isCharging)
            {
                _timeLabel = _statusKind == PowerStatusKind.Full ? "00:00" : FormatingHelper.FormatTimeHours((totalMax - totalStored) / netRate);
                SetRightSideText(_timeLabel, _statusKind == PowerStatusKind.Full ? ScreenConfigPower.HeaderColor : ScreenConfigPower.WarningColor);
            }
            else
            {
                float hours = totalStored / netRate;
                _timeLabel = FormatingHelper.FormatTimeHours(hours);
                Color timeColor = hours <= 5f / 60f ? ScreenConfigPower.ErrorColor : ScreenConfigPower.WarningColor;
                SetRightSideText(_timeLabel, timeColor);
            }
        }

        static float GetRatio(IMyBatteryBlock battery)
        {
            if (battery.MaxStoredPower <= 0f) return 0f;
            var ratio = Math.Max(0f, Math.Min(1f, battery.CurrentStoredPower / battery.MaxStoredPower));
            if (ratio > FullThreshold)
                return 1;
            return ratio;
        }
        
        Color GetBatteryIconColor(float ratio)
        {
            if (ratio < 0.15f) return ScreenConfigPower.ErrorColor;
            if (ratio < 0.35f) return ScreenConfigPower.WarningColor;
            return ScreenConfigPower.HeaderColor;
        }

        static IList<ITooltipLine> BuildBatteryDetails(IMyBatteryBlock battery)
        {
            var lines = new List<ITooltipLine>();
            if (battery == null)
                return lines;

            float ratio = GetRatio(battery);
            lines.Add(new StaticTooltipLine($"{LocHelper.GetLoc("RadialMenuGroupTitle_Power")}: " + FormatingHelper.PercentageToString(ratio)));
            lines.Add(new StaticTooltipLine(LocHelper.GetLoc("BlockPropertiesText_StoredPower") + FormatingHelper.MegaWattHoursToString(battery.CurrentStoredPower) + " / " + FormatingHelper.WattHoursToString(battery.MaxStoredPower)));
            lines.Add(new StaticTooltipLine(LocHelper.GetLoc("BlockPropertyProperties_CurrentInput") + FormatingHelper.MegaWattsToString((battery.CurrentInput - battery.CurrentOutput)) 
                                                      // display I/O if is both charging and discharging at the same time
                                                      + (battery.CurrentInput  != 0 && battery.CurrentOutput != 0 ?  $" (+{FormatingHelper.MegaWattsToString(battery.CurrentInput)},-{FormatingHelper.MegaWattsToString(battery.CurrentOutput)})" : "")));
            lines.Add(new StaticTooltipLine(LocHelper.GetLoc("BlockPropertyTitle_ChargeMode")+": " + battery.ChargeMode));
            
            float netRate = battery.CurrentInput - battery.CurrentOutput;

            if (netRate > 0)
            {
                lines.Add(new StaticTooltipLine(LocHelper.GetLoc("BlockPropertiesText_RechargedIn") +
                                                FormatingHelper.FormatTimeHours(
                                                    (battery.MaxStoredPower - battery.CurrentStoredPower) / netRate)));
            }
            else
            {
                lines.Add(new StaticTooltipLine(LocHelper.GetLoc("BlockPropertiesText_DepletedIn") +
                                                FormatingHelper.FormatTimeHours(battery.CurrentStoredPower /
                                                    -netRate)));
            }

            return lines;
        }

        string GetStatusText()
        {
            switch (_statusKind)
            {
                case PowerStatusKind.Full:
                    return FullLabel;
                case PowerStatusKind.Charging:
                    return ChargingLabel;
                case PowerStatusKind.Discharging:
                    return DischargingLabel;
                default:
                    return string.Empty;
            }
        }

        void SetRightSideText(string text, Color color)
        {
            _rightSideText = text;
            _rightSideColor = color;
        }
    }
}
