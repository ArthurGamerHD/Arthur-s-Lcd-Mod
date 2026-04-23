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
    public partial class RenewablePowerSurfaceScript : PowerSurfaceScriptBase,
        IUsesTerminalControlGroup<BaseTerminalControlGroup>,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<CheckboxHideEmpty>
    {
        public const string ID = "RenewableGraph";
        public const string TITLE = "DisplayName_BlockGroup_EnergyRenewableGroup";

        static readonly PowerEntryDefinition[] Definitions =
        {
            new PowerEntryDefinition("solar", "DisplayName_BlockGroup_SolarPanels", "Solar Panels"),
            new PowerEntryDefinition("wind", "DisplayName_BlockGroup_WindTurbines", "Wind Turbines")
        };

        protected override PowerEntryDefinition[] EntryDefinitions => Definitions;
        protected override string DefaultTitle => TITLE;

        public RenewablePowerSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
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

            entryKey = null;
            return false;
        }
    }
}