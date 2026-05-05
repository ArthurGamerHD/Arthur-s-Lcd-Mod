using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.TerminalControls.Filter.Listbox;

using Sandbox.ModAPI;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;

namespace LcdMod.Client.TerminalControls.Filter.Buttons
{
    public sealed partial class ButtonBlockRemoveFromSelection : TerminalControlFilterButton
    {
        public ButtonBlockRemoveFromSelection(TerminalControlsListbox sourceList,
            TerminalControlsListbox targetList) : base(sourceList,  targetList)
        {
            CreateButton("ItemChartRemoveBlockFromSelection","EventControllerBlock_RemoveBlocks_Title" );
        }

        protected override void Action(IMyTerminalBlock block)
        {
            if (TargetList.Selection == null || TargetList.Selection.Count <= 0) 
                return;
            
            var index = GetThisSurfaceIndex(block);
            var settings = ConfigManager.GetConfigForBlock(block);

            if (settings == null || settings.Screens.Count <= index)
                return;

            var config = settings.Screens[index] as ScreenConfigWithBlocks;
            RemoveGroups(config);
            RemoveItems(config);
            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
            ConfigManager.Sync(block, settings);
        }

        void RemoveGroups(ScreenConfigWithBlocks config)
        {
            var groups = TargetList.Selection
                .Where(a => a.UserData is string)
                .Select(a => (string)a.UserData);

            if (config.SelectedGroups.Length > 0)
                config.SelectedGroups = config.SelectedGroups.Where(a => !groups.Contains(a)).ToArray();
        }

        void RemoveItems(ScreenConfigWithBlocks config)
        {
            var ids = TargetList.Selection
                .Where(a => a.UserData is long)
                .Select(a => (long)a.UserData);

            if (config.SelectedBlocks.Length > 0)
                config.SelectedBlocks = config.SelectedBlocks.Where(a => !ids.Contains(a)).ToArray();
        }
    }
}
