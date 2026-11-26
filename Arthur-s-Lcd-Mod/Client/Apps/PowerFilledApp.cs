using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.UserControls.Power;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;

namespace LcdMod.Client.Apps
{
    [LcdApp(8)]
    [MultiFunction(SurfaceScripts.PowerFilledSurfaceScript.TITLE, "Battery")]
    [ConfigComponent(Constants.APP, typeof(PowerConfigComponent), PropertyName = "PowerComponent")]
    internal sealed partial class PowerFilledApp : App, IApp, PowerFilled.IPowerFilledHost
    {
        readonly IAppHost _surfaceHost;
        readonly List<PowerCollector> _collectors = new List<PowerCollector>();
        readonly List<PowerEntry> _entries = new List<PowerEntry>();
        readonly PowerFilled.PowerFilledViewModel _viewModel;

        internal override float WindowRenderScaling => CardScale;

        internal IReadOnlyList<PowerEntry> NativeEntries => _entries;
        internal IReadOnlyList<PowerCollector> NativeCollectors => _collectors;
        internal Color NativeForeground => _surfaceHost.Surface.ScriptForegroundColor;
        bool PowerFilled.IPowerFilledHost.IsGridDisplay => IsGridDisplay;
        Color PowerFilled.IPowerFilledHost.NativeForeground => NativeForeground;
        IReadOnlyList<PowerEntry> PowerFilled.IPowerFilledHost.NativeEntries => NativeEntries;
        IReadOnlyList<PowerCollector> PowerFilled.IPowerFilledHost.NativeCollectors => NativeCollectors;
        void PowerFilled.IPowerFilledHost.NotifyEntryDetailsOpened() { NotifyEntryDetailsOpened(); }

        public event Action EntryDetailsOpened;

        internal void NotifyEntryDetailsOpened()
        {
            var handler = EntryDetailsOpened;
            if (handler != null)
                handler();
        }

        public PowerFilledApp(IAppHost surfaceHost) : base(surfaceHost)
        {
            _surfaceHost = surfaceHost;
            _viewModel = new PowerFilled.PowerFilledViewModel(this);
            SetContent(new PowerFilled.PowerFilledView { DataContext = _viewModel });
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            CloseCollectors();
            _entries.Clear();
        }

        public override void Update()
        {
            if (_collectors.Count == 0)
                BuildCollectors();

            _entries.Clear();
            var gridLogic = _surfaceHost.GridLogic;
            if (gridLogic != null)
                for (int i = 0; i < _collectors.Count; i++)
                    _collectors[i].Collect(gridLogic, _entries);
            _viewModel.Refresh();
        }

        public override bool HasVisibleItems()
        {
            for (int i = 0; i < _collectors.Count; i++)
            {
                if (_collectors[i] != null && _collectors[i].HasVisibleItems)
                    return true;
            }

            return false;
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public override void Close()
        {
            CloseCollectors();
            base.Close();
        }

        void CloseCollectors()
        {
            for (int i = 0; i < _collectors.Count; i++)
                _collectors[i].Dispose();

            _collectors.Clear();
        }

        void BuildCollectors()
        {
            _collectors.Clear();

            var labelCharging = MyTexts.GetString("HudEnergyGroupCharging");
            var labelDischarging = MyTexts.GetString("BlockActionTitle_Discharge");
            var labelJumpNotReady = MyTexts.GetString("ScreenMedicals_RespawnShipNotReady");
            var labelJumpReady = MyTexts.GetString("ScreenMedicals_RespawnShipReady");
            var labelFull = MyTexts.GetString("RadialMenuAction_Signal_Full");

            _collectors.Add(new BatteryPowerCollector(Host, () => PowerComponent, () => ColorComponent)
            {
                ChargingLabel = labelCharging,
                DischargingLabel = labelDischarging,
                FullLabel = labelFull
            });

            _collectors.Add(new JumpDrivePowerCollector(Host, () => PowerComponent, () => ColorComponent)
            {
                ChargingLabel = labelCharging,
                ReadyLabel = labelJumpReady,
                NotReadyLabel = labelJumpNotReady
            });
        }
    }
}
