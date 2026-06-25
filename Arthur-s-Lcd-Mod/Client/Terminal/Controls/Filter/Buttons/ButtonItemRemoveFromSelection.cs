using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game;

namespace LcdMod.Client.Terminal.Controls.Filter.Buttons
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
            var surface = settings == null ? null : settings.GetSurfaceConfig(index);
            if (settings == null || !settings.CanWriteConfig(surface))
                return;
            var config = surface == null ? null : surface.TryGet<ItemSelectionConfigComponent>(Constants.ITEMS);
            if (config == null)
                return;

            RemoveGroups(config);
            RemoveBlocks(config);
            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
            ConfigManager.Sync(block, settings);
        }

        void RemoveGroups(ItemSelectionConfigComponent config)
        {
            var groups = TargetList.Selection
                .Where(a => a.UserData is string)
                .Select(a => (string)a.UserData);

            if (config.SelectedCategories != null && config.SelectedCategories.Length > 0)
                config.SelectedCategories = config.SelectedCategories.Where(a => !groups.Contains(a)).ToArray();
        }

        void RemoveBlocks(ItemSelectionConfigComponent config)
        {
            var ids = TargetList.Selection
                .Where(a => a.UserData is MyDefinitionId)
                .Select(a => ((MyDefinitionId)a.UserData).ToString());

            if (config.SelectedDefinition != null && config.SelectedDefinition.Length > 0)
                config.SelectedDefinition = config.SelectedDefinition.Where(a => !ids.Contains(a)).ToArray();
        }
    }
}
