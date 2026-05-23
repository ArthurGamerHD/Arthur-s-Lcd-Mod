using System;
using System.Collections.Generic;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using ScreenConfigPower = LcdMod.Common.Config.Models.Apps.ScreenConfigPower;

namespace LcdMod.Client.Gui.UserControls.Power
{
    internal sealed class JumpDrivePowerCollector : PowerCollector
    {
        const float FULL_THRESHOLD = 0.999f;
        const float EPS = 0.001f;
        static readonly FillableTexture Texture = new FillableTexture(
            "JumpDrive",
            1f,
            21f,
            21f,
            32f,
            10f,
            "JumpDriveCore",
            true);
        static readonly MyDefinitionId ElectricityId =
            new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");

        readonly List<IMyJumpDrive> _visible = new List<IMyJumpDrive>();

        float _averageCharge;
        string _statusText = string.Empty;
        string _rightSideText = string.Empty;
        Color _rightSideColor = Color.White;
        PowerStatusKind _statusKind = PowerStatusKind.None;
        Color _statusColor = Color.White;

        public JumpDrivePowerCollector(ScreenConfigPower screenConfig) : base(screenConfig)
        {
        }

        public IReadOnlyList<IMyJumpDrive> VisibleJumpDrives => _visible;

        public string TextureName => "JumpDrive";
        public override string FooterPrefix => LocHelper.GetLoc("DisplayName_BlockGroup_JumpDrives");
        public override FillableTexture FillableTexture => Texture;
        public override string StatusText => _statusText;
        public override Color StatusColor => _statusColor;
        public override PowerStatusKind StatusKind => _statusKind;
        public override float AverageCharge => _averageCharge;
        public override bool HasVisibleItems => _visible.Count > 0;
        public override string RightSideText => _rightSideText;
        public override Color RightSideColor => _rightSideColor;
        public override float CenterIconScale => 0.65f;

        public override void Collect(GridLogic grid, List<PowerEntry> entries)
        {
            _visible.Clear();
            _averageCharge = 0f;
            _statusText = string.Empty;
            _rightSideText = string.Empty;
            _rightSideColor = ScreenConfigPower.HeaderColor;
            _statusKind = PowerStatusKind.None;
            _statusColor = Color.White;

            if (grid == null)
                return;

            var jumpDrives = grid.GetJumpDrives();
            int fullCount = 0;
            int notFullCount = 0;
            BeginCenterIconSpinFrame();

            for (int i = 0; i < jumpDrives.Count; i++)
            {
                var jumpDrive = jumpDrives[i];
                if (!HideEmpty || jumpDrive.MaxStoredPower > 0f)
                {
                    _visible.Add(jumpDrive);
                    if (GetRatio(jumpDrive) >= FULL_THRESHOLD)
                        fullCount++;
                    else
                        notFullCount++;
                }
            }

            if (_visible.Count == 0)
            {
                EndCenterIconSpinFrame();
                return;
            }

            float sumRatio = 0f;
            bool showFirstReady = fullCount == 0;
            float timeToFullHours = showFirstReady ? float.MaxValue : 0f;
            bool hasChargingTime = false;

            for (int i = 0; i < _visible.Count; i++)
            {
                var jumpDrive = _visible[i];
                float ratio = GetRatio(jumpDrive);
                bool isFull = ratio >= FULL_THRESHOLD;
                sumRatio += ratio;
                bool isChargingThisDrive = false;
                float hours;
                if (ratio < FULL_THRESHOLD && TryGetTimeToFull(jumpDrive, out hours))
                {
                    isChargingThisDrive = true;
                    hasChargingTime = true;
                    if (showFirstReady)
                    {
                        if (hours < timeToFullHours) timeToFullHours = hours;
                    }
                    else if (hours > timeToFullHours)
                    {
                        timeToFullHours = hours;
                    }
                }

                float centerRotation = GetCenterIconRotation(
                    jumpDrive.EntityId,
                    isChargingThisDrive || isFull,
                    ratio,
                    isFull ? 0.25f : -1f);

                entries.Add(new PowerEntry(
                    jumpDrive.EntityId,
                    Texture,
                    ratio,
                    FormatingHelper.PercentageToString(ratio),
                    GetJumpDriveIconColor(ratio),
                    true,
                    centerRotation, 
                    CenterIconScale,
                    BlockIconHelper.GetOrAddTextureForBlock(((MyCubeBlock)jumpDrive).BlockDefinition),
                    jumpDrive,
                    () => BuildJumpDriveDetails(jumpDrive)));
            }
            EndCenterIconSpinFrame();

            _averageCharge = sumRatio / _visible.Count;

            if (notFullCount == 0)
            {
                _statusKind = PowerStatusKind.Ready;
                _statusText = ReadyLabel;
                _statusColor = ScreenConfigPower.HeaderColor;
            }
            else if (fullCount == 0)
            {
                _statusKind = PowerStatusKind.NotReady;
                _statusText = NotReadyLabel;
                _statusColor = ScreenConfigPower.ErrorColor;
            }
            else
            {
                _statusKind = PowerStatusKind.Charging;
                _statusText = ChargingLabel;
                _statusColor = ScreenConfigPower.WarningColor;
            }

            if (notFullCount == 0)
            {
                SetRightSideText("00:00", ScreenConfigPower.HeaderColor);
            }
            else if (hasChargingTime)
            {
                Color timeColor = fullCount == 0 ? ScreenConfigPower.ErrorColor : ScreenConfigPower.WarningColor;
                SetRightSideText(FormatingHelper.FormatTimeHours(timeToFullHours), timeColor);
            }
            else
            {
                Color timeColor = fullCount == 0 ? ScreenConfigPower.ErrorColor : ScreenConfigPower.WarningColor;
                SetRightSideText("--:--", timeColor);
            }
        }

        Color GetJumpDriveIconColor(float ratio)
        {
            if (ratio < 0.15f) return ScreenConfigPower.ErrorColor;
            if (ratio < FULL_THRESHOLD) return ScreenConfigPower.WarningColor;
            return ScreenConfigPower.HeaderColor;
        }

        static IList<ITooltipLine> BuildJumpDriveDetails(IMyJumpDrive jumpDrive)
        {
            var lines = new List<ITooltipLine>();
            if (jumpDrive == null)
                return lines;

            float ratio = GetRatio(jumpDrive);
            lines.Add(new StaticTooltipLine("Charge: " + FormatingHelper.PercentageToString(ratio)));
            lines.Add(new StaticTooltipLine("Stored: " + FormatMegawattHours(jumpDrive.CurrentStoredPower) + " / " + FormatMegawattHours(jumpDrive.MaxStoredPower)));

            float hours;
            if (ratio >= FULL_THRESHOLD)
                lines.Add(new StaticTooltipLine("Ready"));
            else if (TryGetTimeToFull(jumpDrive, out hours))
                lines.Add(new StaticTooltipLine("Ready in: " + FormatingHelper.FormatTimeHours(hours)));
            else
                lines.Add(new StaticTooltipLine("Ready in: --:--"));

            return lines;
        }

        static string FormatMegawattHours(float value)
        {
            return value.ToString("0.##", FormatingHelper.Culture) + " MWh";
        }

        static float GetRatio(IMyJumpDrive jumpDrive)
        {
            if (jumpDrive.MaxStoredPower <= 0f) return 0f;
            return Math.Max(0f, Math.Min(1f, jumpDrive.CurrentStoredPower / jumpDrive.MaxStoredPower));
        }

        static bool TryGetTimeToFull(IMyJumpDrive jumpDrive, out float hours)
        {
            hours = 0f;
            float remaining = jumpDrive.MaxStoredPower - jumpDrive.CurrentStoredPower;
            if (remaining <= 0f)
                return true;

            var terminal = jumpDrive as IMyTerminalBlock;

            MyResourceSinkComponent sink;
            try
            {
                terminal.Components.TryGet(out sink);
            }
            catch
            {
                return false;
            }

            if (sink == null)
                return false;

            float input;
            try
            {
                input = sink.CurrentInputByType(ElectricityId);
            }
            catch
            {
                return false;
            }

            if (input <= EPS)
                return false;

            hours = remaining / input;
            return true;
        }

        void SetRightSideText(string text, Color color)
        {
            _rightSideText = text;
            _rightSideColor = color;
        }
    }
}
