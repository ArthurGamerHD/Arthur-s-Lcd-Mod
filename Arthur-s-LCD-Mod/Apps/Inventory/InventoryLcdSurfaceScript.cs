using System.Collections.Generic;
using Generated;
using Graph.Apps.Abstract;
using Graph.System.TerminalControls.Color;
using Graph.System.TerminalControls.Filter;
using Graph.System.TerminalControls.Filter.Buttons;
using Graph.System.TerminalControls.Filter.Listbox;
using Graph.System.TerminalControls.Generic;
using Graph.System.TerminalControls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace Graph.Apps.Inventory
{
    [MyTextSurfaceScript(ID, "Inventory")]
    public partial class InventoryLcdSurfaceScript : ItemsSurfaceScriptBase,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<CheckboxHideEmpty>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>,
        IUsesTerminalControlGroup<ItemsFilterTerminalControlGroup>,
        IUsesTerminalControl<ComboboxSorting>
    {
        public const string ID = "InventoryCharts";
        public const string NAME = "Inventory";

        public override Dictionary<MyItemType, double> ItemSource =>
            AppConfig == null ? null : GridLogic?.GetItems(AppConfig, Block as IMyTerminalBlock);

        protected override string DefaultTitle => NAME;

        public InventoryLcdSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface,
            block, size)
        {
        }
    }
}
