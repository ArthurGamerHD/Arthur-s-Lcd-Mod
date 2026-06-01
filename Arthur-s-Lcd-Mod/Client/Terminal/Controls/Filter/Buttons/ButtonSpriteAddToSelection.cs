using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Filter.Buttons
{
    public sealed partial class ButtonSpriteAddToSelection : TerminalControlFilterButton
    {
        public ButtonSpriteAddToSelection(TerminalControlsListbox sourceList, TerminalControlsListbox targetList)
            : base(sourceList, targetList)
        {
            CreateButton("SpriteSelectorAddToSelection", "BlockPropertyTitle_ConveyorSorterAdd");
        }

        protected override void Action(IMyTerminalBlock block)
        {
            if (SourceList.Selection == null || SourceList.Selection.Count <= 0)
                return;

            var index = GetThisSurfaceIndex(block);
            var settings = ConfigManager.GetConfigForBlock(block);
            if (settings == null || settings.Screens.Count <= index)
                return;

            var config = settings.Screens[index] as ScreenConfigDigitalPictureFrames;
            if (config == null)
                return;

            var sprites = SourceList.Selection
                .Select(i => i.UserData as string)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
            if (sprites.Length == 0)
                return;

            var selected = GetSelectedSprites(config).ToList();
            for (int i = 0; i < sprites.Length; i++)
            {
                if (!selected.Contains(sprites[i], System.StringComparer.OrdinalIgnoreCase))
                    selected.Add(sprites[i]);
            }

            config.SelectedSprites = selected.ToArray();
            config.BackgroundSprite = config.SelectedSprites.Length > 0 ? config.SelectedSprites[0] : string.Empty;

            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
            ConfigManager.Sync(block, settings);
        }

        static string[] GetSelectedSprites(ScreenConfigDigitalPictureFrames config)
        {
            if (config.SelectedSprites != null && config.SelectedSprites.Length > 0)
                return config.SelectedSprites.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            return string.IsNullOrWhiteSpace(config.BackgroundSprite)
                ? new string[0]
                : new[] { config.BackgroundSprite };
        }
    }
}
