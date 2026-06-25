using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game;

namespace LcdMod.Client.Terminal.Controls.Filter.Buttons
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
            var surface = settings == null ? null : settings.GetSurfaceConfig(index);
            if (settings == null || !settings.CanWriteConfig(surface))
                return;
            var config = surface == null ? null : surface.TryGet<ItemSelectionConfigComponent>(Constants.ITEMS);
            if (config == null)
                return;

            AddBlocks(config);
            AddGroups(config);

            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
            ConfigManager.Sync(block, settings);
        }

        void AddGroups(ItemSelectionConfigComponent config)
        {
            var groups = SourceList.Selection.Where(a => a.UserData is string)
                .Select(a => (string)a.UserData);

            if (config.SelectedCategories != null && config.SelectedCategories.Length > 0)
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

        void AddBlocks(ItemSelectionConfigComponent config)
        {
            var ids = SourceList.Selection
                .Where(a => a.UserData is MyDefinitionId)
                .Select(a => ((MyDefinitionId)a.UserData).ToString());

            if (config.SelectedDefinition != null && config.SelectedDefinition.Length > 0)
            {
                var list = config.SelectedDefinition.ToList();
                list.AddRange(ids);
                config.SelectedDefinition = list.ToArray();
            }
            else
            {
                config.SelectedDefinition = ids.ToArray();
            }
        }
    }
}
