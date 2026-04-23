using System;
using Generated;
using Graph.Apps.Abstract;
using Graph.System.TerminalControls.Color;
using Graph.System.TerminalControls.Generic;
using Graph.System.TerminalControls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;

namespace Graph.Apps.Power
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class GeneratorsSurfaceScript : PowerSurfaceScriptBase,
        IUsesTerminalControlGroup<BaseTerminalControlGroup>,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<CheckboxHideEmpty>
    {
        public const string ID = "GeneratorsGraph";
        public const string TITLE = "RadialMenuGroupTitle_Power";

        static readonly PowerEntryDefinition[] Definitions =
        {
            new PowerEntryDefinition("solar", "DisplayName_BlockGroup_SolarPanels", "Solar Panels"),
            new PowerEntryDefinition("wind", "DisplayName_BlockGroup_WindTurbines", "Wind Turbines"),
            new PowerEntryDefinition("reactor", "DisplayName_BlockGroup_Reactors", "Reactors"),
            new PowerEntryDefinition("engine", "DisplayName_BlockGroup_HydrogenEngines", "Engines"),
            new PowerEntryDefinition("batteries", "DisplayName_BlockGroup_Batteries", "Batteries")
        };

        protected override PowerEntryDefinition[] EntryDefinitions => Definitions;
        protected override string DefaultTitle => TITLE;

        public GeneratorsSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            InitializeEntries();
        }

        protected override bool TryMapProducerType(string typeId, IMyPowerProducer producer, out string entryKey)
        {
            if (producer is IMyBatteryBlock)
            {
                entryKey = "battery";
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

            // dam you hydrogen engine
            if (typeId.EndsWith("HydrogenEngine", StringComparison.OrdinalIgnoreCase))
            {
                entryKey = "engine";
                return true;
            }

            entryKey = null;
            return false;
        }
    }
}