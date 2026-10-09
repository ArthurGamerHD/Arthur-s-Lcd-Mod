using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Power;
using Sandbox.Game.Entities;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using IMyBatteryBlock = Sandbox.ModAPI.IMyBatteryBlock;
namespace LcdMod.Client.Gui.UserControls.Power
{
    internal sealed class BatteryPowerCollector : PowerCollector
    {
        const float FULL_THRESHOLD = 0.995f;
        const float CHARGE_TREND_EPSILON_MWH = 0.000001f;

        readonly List<IMyBatteryBlock> _visible = new List<IMyBatteryBlock>();
        readonly LinkedTypedBlockSourceSet<IMyBatteryBlock> _batteries =
            new LinkedTypedBlockSourceSet<IMyBatteryBlock>(delegate(TypedBlockCollection blocks)
            {
                return blocks.BatteryBlocks;
            });

        Dictionary<long, PowerEntry> _activeEntriesBuffer = new Dictionary<long, PowerEntry>();
        Dictionary<long, PowerEntry> _standbyEntriesBuffer = new Dictionary<long, PowerEntry>();

        float _averageCharge;
        string _statusText = string.Empty;
        string _rightSideText = string.Empty;
        Color _rightSideColor = Color.White;
        PowerStatusKind _statusKind = PowerStatusKind.None;
        Color _statusColor = Color.White;
        string _timeLabel = "--:--";

        public BatteryPowerCollector(
            IAppHost host,
            Func<PowerConfigComponent> getPower,
            Func<ColorConfigComponent> getColors)
            : base(host, getPower, getColors)
        {
        }

        public IReadOnlyList<IMyBatteryBlock> VisibleBatteries => _visible;

        public override string FooterPrefix => LocHelper.GetLoc("HudEnergyGroupBatteries");
        public override string FillClass => "Battery";
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
            _rightSideColor = HeaderColor;
            _statusKind = PowerStatusKind.None;
            _statusColor = Color.White;
            _timeLabel = "--:--";

            _standbyEntriesBuffer.Clear();

            if (grid == null)
            {
                _activeEntriesBuffer.Clear();
                return;
            }

            _batteries.Bind(grid, GridLinkType);
            const float eps = 0.001f;
            float totalStoredRate = 0f;
            float sumRatio = 0f;
            float totalStored = 0f;
            float totalMax = 0f;
            int fullCount = 0;
            int batteriesRecharging = 0;

            var batterySources = _batteries.Sources;
            for (int sourceIndex = 0; sourceIndex < batterySources.Count; sourceIndex++)
            {
                var batteries = batterySources[sourceIndex];
                for (int i = 0; i < batteries.Count; i++)
                {
                    var battery = batteries[i];
                    if (!HideEmpty || battery.MaxStoredPower > 0f)
                    {
                        _visible.Add(battery);
                        totalStored += battery.CurrentStoredPower;
                        totalMax += battery.MaxStoredPower;

                        var isRecharging = battery.ChargeMode == ChargeMode.Recharge;
                        if (isRecharging)
                            batteriesRecharging++;

                        var storedRate = PowerBlockMath.BatteryStoredPowerRate(battery);
                        totalStoredRate += storedRate;
                        var drawChargingIcon = isRecharging || storedRate > CHARGE_TREND_EPSILON_MWH;

                        var ratio = GetRatio(battery);
                        sumRatio += ratio;
                        bool full = ratio >= 1;
                    
                        if (full)
                            fullCount++;

                        var entry = GetOrUpdateBatteryEntry(
                            battery,
                            ratio,
                            FormatingHelper.PercentageToString(ratio),
                            GetBatteryIconColor(ratio),
                            drawChargingIcon || full);

                        entries.Add(entry);
                    }
                }
            }
            
            SwapEntryBuffers();

            bool hasRechargingBattery = batteriesRecharging > 0;
            if (_visible.Count > 0 && fullCount == _visible.Count)
            {
                _statusKind = PowerStatusKind.Full;
                _statusColor = HeaderColor;
            }
            else if (totalStoredRate > CHARGE_TREND_EPSILON_MWH ||
                     hasRechargingBattery && totalStoredRate >= -CHARGE_TREND_EPSILON_MWH)
            {
                _statusKind = PowerStatusKind.Charging;
                _statusColor = WarningColor;
            }
            else if (totalStoredRate < -CHARGE_TREND_EPSILON_MWH)
            {
                _statusKind = PowerStatusKind.Discharging;
                _statusColor = ErrorColor;
            }
            else
            {
                _statusKind = PowerStatusKind.None;
                _statusColor = HeaderColor;
            }

            if (_visible.Count == 0)
                return;

            _averageCharge = sumRatio / _visible.Count;
            _statusText = GetStatusText();

            if (_statusKind == PowerStatusKind.Full)
            {
                _timeLabel = "00:00";
                SetRightSideText(_timeLabel, HeaderColor);
            }
            else if (Math.Abs(totalStoredRate) < eps)
            {
                _timeLabel = "--:--";
                SetRightSideText(_timeLabel, HeaderColor);
            }
            else if (totalStoredRate > 0f)
            {
                _timeLabel = FormatingHelper.FormatTimeHours(PowerBlockMath.HoursAtRate(totalMax - totalStored, totalStoredRate));
                SetRightSideText(_timeLabel, WarningColor);
            }
            else
            {
                float hours = PowerBlockMath.HoursAtRate(totalStored, totalStoredRate);
                _timeLabel = FormatingHelper.FormatTimeHours(hours);
                Color timeColor = hours <= 5f / 60f ? ErrorColor : WarningColor;
                SetRightSideText(_timeLabel, timeColor);
            }
        }

        PowerEntry GetOrUpdateBatteryEntry(
            IMyBatteryBlock battery,
            float ratio,
            string percentText,
            Color fillColor,
            bool drawCenterIcon)
        {
            var entryId = battery.EntityId;
            var capturedBattery = battery;
            var blockIcon = TextureHelper.GetOrAddTextureForBlock(((MyCubeBlock)battery).BlockDefinition);

            PowerEntry entry;
            if (!_activeEntriesBuffer.TryGetValue(entryId, out entry) || entry == null)
            {
                var detail = BuildBatteryDetails(capturedBattery);
                entry = new PowerEntry(
                    entryId,
                    FillClass,
                    ratio,
                    percentText,
                    fillColor,
                    drawCenterIcon,
                    blockIcon: blockIcon,
                    entity: capturedBattery,
                    getDetails: () => detail);
            }
            else
            {
                entry.Update(
                    entryId,
                    FillClass,
                    ratio,
                    percentText,
                    fillColor,
                    drawCenterIcon,
                    blockIcon: blockIcon,
                    entity: capturedBattery,
                    getDetails: () => BuildBatteryDetails(capturedBattery));
            }

            _standbyEntriesBuffer[entryId] = entry;
            return entry;
        }

        void SwapEntryBuffers()
        {
            var tmp = _activeEntriesBuffer;
            _activeEntriesBuffer = _standbyEntriesBuffer;
            _standbyEntriesBuffer = tmp;
            _standbyEntriesBuffer.Clear();
        }

        static float GetRatio(IMyBatteryBlock battery)
        {
            if (battery.MaxStoredPower <= 0f) return 0f;
            var ratio = Math.Max(0f, Math.Min(1f, battery.CurrentStoredPower / battery.MaxStoredPower));
            if (ratio > FULL_THRESHOLD)
                return 1;
            return ratio;
        }
        
        Color GetBatteryIconColor(float ratio)
        {
            if (ratio < 0.15f) return ErrorColor;
            if (ratio < 0.35f) return WarningColor;
            return HeaderColor;
        }

        static IList<PowerDetailLine> BuildBatteryDetails(IMyBatteryBlock battery)
        {
            var lines = new List<PowerDetailLine>();
            if (battery == null)
                return lines;

            float ratio = GetRatio(battery);
            lines.Add(new PowerDetailLine(FormatLabelWithColon(LocHelper.GetLoc("RadialMenuGroupTitle_Power")) + " " + FormatingHelper.PercentageToString(ratio)));
            lines.Add(new PowerDetailLine(LocHelper.GetLoc("BlockPropertiesText_StoredPower") + FormatingHelper.MegaWattHoursToString(battery.CurrentStoredPower) + " / " + FormatingHelper.MegaWattHoursToString(battery.MaxStoredPower)));
            lines.Add(new PowerDetailLine(LocHelper.GetLoc("BlockPropertyProperties_CurrentInput") + FormatingHelper.MegaWattsToString((battery.CurrentInput - battery.CurrentOutput)) 
                                                      // display I/O if is both charging and discharging at the same time
                                                      + (battery.CurrentInput  != 0 && battery.CurrentOutput != 0 ?  $" (+{FormatingHelper.MegaWattsToString(battery.CurrentInput)},-{FormatingHelper.MegaWattsToString(battery.CurrentOutput)})" : "")));
            lines.Add(new PowerDetailLine(
                () => FormatLabelWithColon(LocHelper.GetLoc("BlockPropertyTitle_ChargeMode")) + " " + battery.ChargeMode,
                () => CycleBatteryMode(battery),
                battery.HasLocalPlayerAccess));
            
            float storedRate = PowerBlockMath.BatteryStoredPowerRate(battery);

            if (storedRate > 0)
            {
                lines.Add(new PowerDetailLine(LocHelper.GetLoc("BlockPropertiesText_RechargedIn") +
                                                FormatingHelper.FormatTimeHours(PowerBlockMath.HoursAtRate(
                                                    battery.MaxStoredPower - battery.CurrentStoredPower, storedRate))));
            }
            else
            {
                lines.Add(new PowerDetailLine(LocHelper.GetLoc("BlockPropertiesText_DepletedIn") +
                                                FormatingHelper.FormatTimeHours(PowerBlockMath.HoursAtRate(
                                                    battery.CurrentStoredPower, storedRate))));
            }

            return lines;
        }

        static void CycleBatteryMode(IMyBatteryBlock battery)
        {
            if(!battery.HasLocalPlayerAccess())
                return;

            switch (battery.ChargeMode)
            {
                case ChargeMode.Discharge:
                    battery.ChargeMode = ChargeMode.Auto;
                    return;
                case ChargeMode.Recharge:
                    battery.ChargeMode = ChargeMode.Discharge;
                    return;
                default:
                    battery.ChargeMode = ChargeMode.Recharge;
                    break;
            }
        }

        static string FormatLabelWithColon(string label)
        {
            return string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "Common_Label_WithColon"), label);
        }

        public override void Dispose()
        {
            _batteries.Dispose();
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
