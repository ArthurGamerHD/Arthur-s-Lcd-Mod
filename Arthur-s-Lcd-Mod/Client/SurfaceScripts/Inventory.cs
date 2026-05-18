using System.Collections.Generic;
using Generated;
using LcdMod.Client.Terminal.Controls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using CheckboxHideEmpty = LcdMod.Client.Terminal.Controls.Generic.CheckboxHideEmpty;
using ComboboxSorting = LcdMod.Client.Terminal.Controls.Generic.ComboboxSorting;
using ItemsSurfaceScriptBase = LcdMod.Client.SurfaceScripts.Abstract.ItemsSurfaceScriptBase;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;

namespace LcdMod.Client.SurfaceScripts
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
