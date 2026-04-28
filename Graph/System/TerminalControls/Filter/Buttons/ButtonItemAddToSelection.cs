using System.Linq;
using Graph.System.Config;
using Graph.System.Config.Models.Apps;
using Graph.System.TerminalControls.Filter.Listbox;
using Sandbox.ModAPI;
using VRage.Game;

namespace Graph.System.TerminalControls.Filter.Buttons
{
    public sealed partial class ButtonItemAddToSelection : TerminalControlFilterButton
    {
        public ButtonItemAddToSelection(TerminalControlsListbox sourceList,
            TerminalControlsListbox targetList) : base(sourceList, targetList)
        {
            CreateButton("ItemChartAddItemToSelection", "BlockPropertyTitle_ConveyorSorterAdd");
        }


        protected override void Action(IMyTerminalBlock block)
        {
            if (SourceList.Selection == null || SourceList.Selection.Count <= 0)
                return;

            var index = GetThisSurfaceIndex(block);
            var settings = ConfigManager.GetConfigForBlock(block);

            if (settings == null || settings.Screens.Count <= index)
                return;

            AddBlocks(settings.Screens[index] as ScreenConfigWithItems);
            AddGroups(settings.Screens[index] as ScreenConfigWithItems);

            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
            ConfigManager.Sync(block, settings);
        }

        void AddGroups(ScreenConfigWithItems config)
        {
            var groups = SourceList.Selection.Where(a => a.UserData is string)
                .Select(a => (string)a.UserData);

            if (config.SelectedCategories.Length > 0)
            {
                var list = config.SelectedCategories.ToList();
                list.AddRange(groups);
                config.SelectedCategories = list.ToArray();
            }
            else
            {
                config.SelectedCategories = groups.ToArray();
            }
        }

        void AddBlocks(ScreenConfigWithItems config)
        {
            var ids = SourceList.Selection
                .Where(a => a.UserData is MyDefinitionId)
                .Select(a => ((MyDefinitionId)a.UserData));

            if (config.SelectedItems.Length > 0)
            {
                var list = config.SelectedItems.ToList();
                list.AddRange(ids);
                config.SelectedItems = list.ToArray();
            }
            else
            {
                config.SelectedItems = ids.ToArray();
            }
        }
    }
}
