using System;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;

using Sandbox.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Filter.Buttons
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
            var surface = settings == null ? null : settings.GetSurfaceConfig(index);
            if (settings == null || !settings.CanWriteConfig(surface))
                return;
            var config = surface == null ? null : surface.TryGet<BlockSelectionConfigComponent>(Constants.BLOCKS);
            if (config == null)
                return;

            AddBlocks(config);
            AddGroups(config);
                
            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
            ConfigManager.Sync(block, settings);
        }

        void AddGroups(BlockSelectionConfigComponent config)
        {
            var groups = SourceList.Selection.Where(a => a.UserData is string)
                .Select(a => (string)a.UserData);

            if (config.SelectedGroups != null && config.SelectedGroups.Length > 0)
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

        void AddBlocks(BlockSelectionConfigComponent config)
        {
            var ids = SourceList.Selection
                .Where(a => a.UserData is long)
                .Select(a => (long)a.UserData)
                .ToArray();

            RemapHelper.PinBlocks(ids);

            if (config.SelectedBlocks != null && config.SelectedBlocks.Length > 0)
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
