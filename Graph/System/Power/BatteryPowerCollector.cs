using System;
using System.Collections.Generic;
using Graph.Helpers;
using Graph.System.Config;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRageMath;

namespace Graph.System.Power
{
    internal sealed class BatteryPowerCollector : PowerCollector
    {
        const float FullThreshold = 0.995f;
        static readonly FillableTexture Texture = new FillableTexture("Battery", 1f, 55f, 55f, 32f, 10f, "IconEnergy");

        readonly List<IMyBatteryBlock> _visible = new List<IMyBatteryBlock>();

        float _averageCharge;
        string _statusText = string.Empty;
        string _rightSideText = string.Empty;
        Color _rightSideColor = Color.White;
        PowerStatusKind _statusKind = PowerStatusKind.None;
        Color _statusColor = Color.White;
        string _timeLabel = "--:--";
        bool _isCharging;

        public BatteryPowerCollector(ScreenConfig screenConfig) : base(screenConfig)
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

        public override void Collect(GridLogic grid, List<PowerEntry> entries)
        {
            _visible.Clear();
            _averageCharge = 0f;
            _statusText = string.Empty;
            _rightSideText = string.Empty;
            _rightSideColor = ScreenConfig.HeaderColor;
            _statusKind = PowerStatusKind.None;
            _statusColor = Color.White;
            _timeLabel = "--:--";
            _isCharging = false;

            if (grid == null)
                return;

            var batteries = grid.GetBatteries();
            float totalIn = 0f;
            float totalOut = 0f;
            int fullCount = 0;

            for (int i = 0; i < batteries.Count; i++)
            {
                var battery = batteries[i];
                if (!HideEmpty || battery.MaxStoredPower > 0f)
                {
                    _visible.Add(battery);
                    totalIn += battery.CurrentInput;
                    totalOut += battery.CurrentOutput;
                    if (GetRatio(battery) > FullThreshold)
                        fullCount++;
                }
            }

            const float eps = 0.001f;
            bool isActivelyCharging = totalIn > totalOut + eps;
            if (_visible.Count > 0 && fullCount == _visible.Count)
            {
                _statusKind = PowerStatusKind.Full;
                _statusColor = ScreenConfig.HeaderColor;
                _isCharging = true;
            }
            else if (totalIn > totalOut + eps)
            {
                _statusKind = PowerStatusKind.Charging;
                _statusColor = ScreenConfig.WarningColor;
                _isCharging = true;
            }
            else if (totalOut > totalIn + eps)
            {
                _statusKind = PowerStatusKind.Discharging;
                _statusColor = ScreenConfig.ErrorColor;
                _isCharging = false;
            }
            else
            {
                _statusKind = PowerStatusKind.None;
                _statusColor = ScreenConfig.HeaderColor;
            }

            if (_visible.Count == 0)
                return;

            float sumRatio = 0f;
            float totalStored = 0f;
            float totalMax = 0f;
            for (int i = 0; i < _visible.Count; i++)
            {
                var battery = _visible[i];
                float ratio = GetRatio(battery);
                sumRatio += ratio;
                totalStored += battery.CurrentStoredPower;
                totalMax += battery.MaxStoredPower;

                entries.Add(new PowerEntry(
                    battery.EntityId,
                    Texture,
                    ratio,
                    FormatingHelper.PercentageToString(ratio),
                    GetBatteryIconColor(ratio),
                    isActivelyCharging,
                    0f));
            }

            _averageCharge = sumRatio / _visible.Count;
            _statusText = GetStatusText();

            float netRate = Math.Abs(totalIn - totalOut);
            if (netRate < eps)
            {
                _timeLabel = "--:--";
                SetRightSideText(_timeLabel, ScreenConfig.HeaderColor);
            }
            else if (_isCharging)
            {
                _timeLabel = _statusKind == PowerStatusKind.Full ? "00:00" : FormatTimeHours((totalMax - totalStored) / netRate);
                SetRightSideText("+ " + _timeLabel, ScreenConfig.HeaderColor);
            }
            else
            {
                float hours = totalStored / netRate;
                _timeLabel = FormatTimeHours(hours);
                Color timeColor = hours <= 5f / 60f ? ScreenConfig.ErrorColor : ScreenConfig.WarningColor;
                SetRightSideText("- " + _timeLabel, timeColor);
            }
        }

        static float GetRatio(IMyBatteryBlock battery)
        {
            if (battery.MaxStoredPower <= 0f) return 0f;
            return Math.Max(0f, Math.Min(1f, battery.CurrentStoredPower / battery.MaxStoredPower));
        }

        Color GetBatteryIconColor(float ratio)
        {
            if (ratio < 0.15f) return ScreenConfig.ErrorColor;
            if (ratio < 0.35f) return ScreenConfig.WarningColor;
            return ScreenConfig.HeaderColor;
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
