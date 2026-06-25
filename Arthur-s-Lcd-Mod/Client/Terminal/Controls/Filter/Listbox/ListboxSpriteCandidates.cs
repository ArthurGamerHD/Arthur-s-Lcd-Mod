using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Filter.Listbox
{
    public sealed partial class ListboxSpriteCandidates : TerminalControlsListbox
    {
        public ListboxSpriteCandidates()
        {
            CreateListbox("SpriteSelectorCandidates", "Sprite Selector");
        }

        protected override void Getter(IMyTerminalBlock b, List<MyTerminalControlListBoxItem> itemList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var screenSettings = ConfigManager.GetComponentForTerminalApp<DigitalPictureFramesConfigComponent>(b);
            if (screenSettings == null)
                return;

            var selectedSprites = GetSelectedSprites(screenSettings);
            var sprites = GetAvailableSprites(b)
                .Where(s => !selectedSprites.Contains(s, StringComparer.OrdinalIgnoreCase))
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

            itemList.AddRange(sprites.Select(CreateListBoxItem));
            base.Getter(b, itemList, selected);
        }

        List<string> GetAvailableSprites(IMyTerminalBlock block)
        {
            var sprites = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var registeredSprites = new List<string>();
            TextureHelper.GetRegisteredSpriteNames(registeredSprites);
            AddUniqueSprites(registeredSprites, sprites, seen);

            var provider = block as IMyTextSurfaceProvider;
            if (provider != null && provider.SurfaceCount > 0)
            {
                var index = GetThisSurfaceIndex(block);
                if (index >= 0 && index < provider.SurfaceCount)
                {
                    var surfaceSprites = new List<string>();
                    provider.GetSurface(index)?.GetSprites(surfaceSprites);
                    AddUniqueSprites(surfaceSprites, sprites, seen);
                }
            }

            return sprites;
        }

        static void AddUniqueSprites(List<string> source, List<string> target, HashSet<string> seen)
        {
            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                var sprite = source[i];
                if (string.IsNullOrWhiteSpace(sprite) || !seen.Add(sprite))
                    continue;

                target.Add(sprite);
            }
        }

        static MyTerminalControlListBoxItem CreateListBoxItem(string spriteName)
        {
            return ListBoxItemHelper.GetOrComputeListBoxItem(spriteName, spriteName, spriteName);
        }

        static string[] GetSelectedSprites(DigitalPictureFramesConfigComponent config)
        {
            if (config.SelectedSprites != null && config.SelectedSprites.Length > 0)
                return config.SelectedSprites.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            return string.IsNullOrWhiteSpace(config.BackgroundSprite)
                ? new string[0]
                : new[] { config.BackgroundSprite };
        }
    }
}
