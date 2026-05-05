using System.Collections.Generic;
using Generated;
using LcdMod.Client.TerminalControls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using CheckboxHideEmpty = LcdMod.Client.TerminalControls.Generic.CheckboxHideEmpty;
using ComboboxSorting = LcdMod.Client.TerminalControls.Generic.ComboboxSorting;
using ItemsSurfaceScriptBase = LcdMod.Client.Apps.Abstract.ItemsSurfaceScriptBase;
using LabelSeparator = LcdMod.Client.TerminalControls.Filter.LabelSeparator;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using SeparatorFilter = LcdMod.Client.TerminalControls.Filter.SeparatorFilter;
using SwitchToggleLines = LcdMod.Client.TerminalControls.Generic.SwitchToggleLines;

namespace LcdMod.Client.Apps
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
