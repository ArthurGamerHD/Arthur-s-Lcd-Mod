using System;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Filter.Buttons
{
    public sealed partial class ButtonSpriteRemoveFromSelection : TerminalControlFilterButton
    {
        public ButtonSpriteRemoveFromSelection(TerminalControlsListbox sourceList, TerminalControlsListbox targetList)
            : base(sourceList, targetList)
        {
            CreateButton("SpriteSelectorRemoveFromSelection", "BlockPropertyTitle_ConveyorSorterRemove");
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
            var config = ConfigManager.GetComponentForTerminalApp<DigitalPictureFramesConfigComponent>(block);
            if (config == null)
                return;

            var remove = TargetList.Selection
                .Select(i => i.UserData as string)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
            if (remove.Length == 0)
                return;

            config.SelectedSprites = GetSelectedSprites(config)
                .Where(s => !remove.Contains(s, System.StringComparer.OrdinalIgnoreCase))
                .ToArray();
            config.BackgroundSprite = config.SelectedSprites.Length > 0 ? config.SelectedSprites[0] : string.Empty;

            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
            ConfigManager.Sync(block, settings);
        }

        static string[] GetSelectedSprites(DigitalPictureFramesConfigComponent config)
        {
            if (config.SelectedSprites != null && config.SelectedSprites.Length > 0)
                return config.SelectedSprites.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            return string.IsNullOrWhiteSpace(config.BackgroundSprite)
                ? Array.Empty<string>()
                : new[] { config.BackgroundSprite };
        }
    }
}
