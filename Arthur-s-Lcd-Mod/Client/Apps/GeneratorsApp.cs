using System;
using System.Collections.Generic;
using VRageMath;
using LcdMod.Client.Apps.Abstract;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(6)]
    [MultiFunction(SurfaceScripts.GeneratorsSurfaceScript.TITLE, "MyObjectBuilder_Reactor/LargeBlockLargeGenerator")]
    // ReSharper disable once PartialTypeWithSinglePart
    internal sealed partial class GeneratorsApp : PowerApp, Generators.IGeneratorsHost
    {
        bool Generators.IGeneratorsHost.IsGridDisplay => IsGridDisplay;
        IReadOnlyList<PowerEntry> Generators.IGeneratorsHost.NativeEntries => NativeEntries;
        string Generators.IGeneratorsHost.NativeMaxLabel => NativeMaxLabel;
        string Generators.IGeneratorsHost.NativeCurrentLabel => NativeCurrentLabel;
        Color Generators.IGeneratorsHost.NativeAccentFill => NativeAccentFill;
        Color Generators.IGeneratorsHost.NativeHeaderColor => NativeHeaderColor;
        Color Generators.IGeneratorsHost.NativeErrorColor => NativeErrorColor;
        Color Generators.IGeneratorsHost.NativeForeground => NativeForeground;
        bool Generators.IGeneratorsHost.NativeDrawLines => NativeDrawLines;
        int Generators.IGeneratorsHost.RangeTier => RangeTier;
        void Generators.IGeneratorsHost.SetRangeTier(int tier) { SetRangeTier(tier); }

        readonly Generators.GeneratorsViewModel _viewModel;
        static readonly PowerEntryDefinition[] Definitions =
        {
            new PowerEntryDefinition("solar", "DisplayName_BlockGroup_SolarPanels", "Solar Panels"),
            new PowerEntryDefinition("wind", "DisplayName_BlockGroup_WindTurbines", "Wind Turbines"),
            new PowerEntryDefinition("reactor", "DisplayName_BlockGroup_Reactors", "Reactors"),
            new PowerEntryDefinition("engine", "DisplayName_BlockGroup_HydrogenEngines", "Engines"),
            new PowerEntryDefinition("batteries", "DisplayName_BlockGroup_Batteries", "Batteries"),
            new PowerEntryDefinition("other", "TssTargetingInfo_Other", "Other")
        };

        protected override PowerEntryDefinition[] EntryDefinitions => Definitions;
        internal override float WindowRenderScaling => CardScale;

        public GeneratorsApp(IAppHost host) : base(host)
        {
            _viewModel = new Generators.GeneratorsViewModel(this);
            SetContent(new Generators.GeneratorsView { DataContext = _viewModel });
            SetFooter(new Generators.GeneratorsFooterView { DataContext = _viewModel });
            InitializeEntries();
        }

        public override void Update()
        {
            base.Update();
            _viewModel.Refresh();
        }

        protected override bool TryMapProducerType(string typeId, IMyPowerProducer producer, out string entryKey)
        {
            if (producer is IMyBatteryBlock)
            {
                entryKey = "batteries";
                return true;
            }

            if (producer is IMySolarPanel)
            {
                entryKey = "solar";
                return true;
            }

            if (producer is IMyWindTurbine)
            {
                entryKey = "wind";
                return true;
            }

            if (producer is IMyReactor)
            {
                entryKey = "reactor";
                return true;
            }

            if (typeId.EndsWith("HydrogenEngine", StringComparison.OrdinalIgnoreCase))
            {
                entryKey = "engine";
                return true;
            }

            entryKey = "other";
            return true;
        }
    }
}
