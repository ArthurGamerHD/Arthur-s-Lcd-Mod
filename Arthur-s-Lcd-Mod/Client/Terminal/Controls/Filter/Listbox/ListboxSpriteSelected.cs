using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Filter.Listbox
{
    public sealed partial class ListboxSpriteSelected : TerminalControlsListbox
    {
        public ListboxSpriteSelected()
        {
            CreateListbox("SpriteSelectorSelected", "Selected Sprite");
        }

        protected override void Getter(IMyTerminalBlock b, List<MyTerminalControlListBoxItem> itemList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var screenSettings = ConfigManager.GetComponentForTerminalApp<DigitalPictureFramesConfigComponent>(b);
            if (screenSettings == null)
                return;

            itemList.AddRange(GetSelectedSprites(screenSettings).Select(CreateListBoxItem));
            base.Getter(b, itemList, selected);
        }

        static string[] GetSelectedSprites(DigitalPictureFramesConfigComponent config)
        {
            if (config.SelectedSprites != null && config.SelectedSprites.Length > 0)
                return config.SelectedSprites.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            return string.IsNullOrWhiteSpace(config.BackgroundSprite)
                ? new string[0]
                : new[] { config.BackgroundSprite };
        }

        static MyTerminalControlListBoxItem CreateListBoxItem(string spriteName)
        {
            var displayName = spriteName;
            MyLCDTextureDefinition definition;
            var id = new MyDefinitionId(typeof(MyObjectBuilder_LCDTextureDefinition), spriteName);
            if (MyDefinitionManager.Static.TryGetDefinition(id, out definition) &&
                definition != null &&
                !string.IsNullOrWhiteSpace(definition.LocalizationId))
            {
                displayName = MyTexts.GetString(definition.LocalizationId);
            }

            return ListBoxItemHelper.GetOrComputeListBoxItem(displayName, spriteName, spriteName);
        }
    }
}
