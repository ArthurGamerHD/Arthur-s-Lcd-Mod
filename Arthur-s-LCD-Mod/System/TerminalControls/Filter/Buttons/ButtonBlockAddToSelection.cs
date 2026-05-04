using System.Linq;
using Graph.System.Config;
using Graph.System.Config.Models.Apps;
using Graph.System.TerminalControls.Filter.Listbox;
using Sandbox.ModAPI;

namespace Graph.System.TerminalControls.Filter.Buttons
{
    public sealed partial class ButtonBlockAddToSelection : TerminalControlFilterButton
    {
        public ButtonBlockAddToSelection(TerminalControlsListbox sourceList,
            TerminalControlsListbox targetList) : base(sourceList, targetList)
        {
            CreateButton("ItemChartAddBlockToSelection", "EventControllerBlock_AddBlocks_Title");
        }


        protected override void Action(IMyTerminalBlock block)
        {
            if (SourceList.Selection == null || SourceList.Selection.Count <= 0)
                return;

            var index = GetThisSurfaceIndex(block);
            var settings = ConfigManager.GetConfigForBlock(block);

            if (settings == null || settings.Screens.Count <= index) 
                return;

            AddBlocks(settings.Screens[index] as ScreenConfigWithBlocks);
            AddGroups(settings.Screens[index] as ScreenConfigWithBlocks);
                
            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
            ConfigManager.Sync(block, settings);
        }

        void AddGroups(ScreenConfigWithBlocks config)
        {
            var groups = SourceList.Selection.Where(a => a.UserData is string)
                .Select(a => (string)a.UserData);

            if (config.SelectedGroups.Length > 0)
            {
                var list = config.SelectedGroups.ToList();
                list.AddRange(groups);
                config.SelectedGroups = list.ToArray();
            }
            else
            {
                config.SelectedGroups = groups.ToArray();
            }
        }

        void AddBlocks(ScreenConfigWithBlocks config)
        {
            var ids = SourceList.Selection
                .Where(a => a.UserData is long)
                .Select(a => (long)a.UserData);

            if (config.SelectedBlocks.Length > 0)
            {
                var list = config.SelectedBlocks.ToList();
                list.AddRange(ids);
                config.SelectedBlocks = list.ToArray();
            }
            else
            {
                config.SelectedBlocks = ids.ToArray();
            }
        }
    }
}
