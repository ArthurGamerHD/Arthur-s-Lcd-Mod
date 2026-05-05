using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.TerminalControls.Filter.Listbox;
using Sandbox.ModAPI;
using VRage.Game;
using ScreenConfigWithItems = LcdMod.Common.Config.Models.Apps.ScreenConfigWithItems;

namespace LcdMod.Client.TerminalControls.Filter.Buttons
{
    public sealed partial class ButtonItemRemoveFromSelection : TerminalControlFilterButton
    {
        public ButtonItemRemoveFromSelection(TerminalControlsListbox sourceList, TerminalControlsListbox targetList) :
            base(sourceList, targetList)
        {
            CreateButton("ItemChartRemoveItemFromSelection", "BlockPropertyTitle_ConveyorSorterRemove");
        }

        protected override void Action(IMyTerminalBlock block)
        {

            if (TargetList.Selection == null || TargetList.Selection.Count <= 0)
                return;

            var index = GetThisSurfaceIndex(block);
            var settings = ConfigManager.GetConfigForBlock(block);

            if (settings == null || settings.Screens.Count <= index)
                return;

            var config = settings.Screens[index] as ScreenConfigWithItems;
            RemoveGroups(config);
            RemoveBlocks(config);
            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
            ConfigManager.Sync(block, settings);
        }

        void RemoveGroups(ScreenConfigWithItems config)
        {
            var groups = TargetList.Selection
                .Where(a => a.UserData is string)
                .Select(a => (string)a.UserData);

            if (config.SelectedCategories.Length > 0)
                config.SelectedCategories = config.SelectedCategories.Where(a => !groups.Contains(a)).ToArray();
        }

        void RemoveBlocks(ScreenConfigWithItems config)
        {
            var ids = TargetList.Selection
                .Where(a => a.UserData is MyDefinitionId)
                .Select(a => (MyDefinitionId)a.UserData);

            if (config.SelectedItems.Length > 0)
                config.SelectedItems = config.SelectedItems.Where(a => !ids.Contains(a)).ToArray();
        }
    }
}
