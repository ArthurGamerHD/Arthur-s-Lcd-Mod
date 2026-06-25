using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Filter.Buttons
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
            var surface = settings == null ? null : settings.GetSurfaceConfig(index);
            if (settings == null || !settings.CanWriteConfig(surface))
                return;
            var config = surface == null ? null : surface.TryGet<BlockSelectionConfigComponent>(Constants.BLOCKS);
            if (config == null)
                return;

            RemoveGroups(config);
            RemoveItems(config);
            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
            ConfigManager.Sync(block, settings);
        }

        void RemoveGroups(BlockSelectionConfigComponent config)
        {
            var groups = TargetList.Selection
                .Where(a => a.UserData is string)
                .Select(a => (string)a.UserData);

            if (config.SelectedGroups != null && config.SelectedGroups.Length > 0)
                config.SelectedGroups = config.SelectedGroups.Where(a => !groups.Contains(a)).ToArray();
        }

        void RemoveItems(BlockSelectionConfigComponent config)
        {
            var ids = TargetList.Selection
                .Where(a => a.UserData is long)
                .Select(a => (long)a.UserData);

            if (config.SelectedBlocks != null && config.SelectedBlocks.Length > 0)
                config.SelectedBlocks = config.SelectedBlocks.Where(a => !ids.Contains(a)).ToArray();
        }
    }
}
