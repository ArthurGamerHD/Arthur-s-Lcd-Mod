using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.ViewModel;
using Sandbox.Definitions;
using VRage;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Gui.Styling.DataTemplates
{
    public static class ItemDataTemplate
    {
        const int ITEM_CACHE_LIMIT = 256;

        static readonly Dictionary<MyItemType, string> ItemNames = new Dictionary<MyItemType, string>();

        public static void InvalidateItemAssets()
        {
            lock (ItemNames)
                ItemNames.Clear();
        }

        public static string GetItemDisplayName(ItemEntry item)
        {
            if (item == null)
                return string.Empty;

            return string.IsNullOrEmpty(item.DisplayName)
                ? ResolveItemName(item.ItemType)
                : item.DisplayName;
        }

        static string ResolveItemName(MyItemType itemType)
        {
            lock (ItemNames)
                return ResolveItemNameLocked(itemType);
        }

        static string ResolveItemNameLocked(MyItemType itemType)
        {
            string displayName;
            if (ItemNames.TryGetValue(itemType, out displayName))
                return displayName;

            var definition = MyDefinitionManager.Static != null
                ? MyDefinitionManager.Static.TryGetPhysicalItemDefinition(itemType)
                : null;
            var localizationKey = definition != null && definition.DisplayNameEnum.HasValue
                ? definition.DisplayNameEnum.Value.ToString()
                : itemType.SubtypeId;
            displayName = MyTexts.GetString(localizationKey);
            if (string.IsNullOrEmpty(displayName))
                displayName = itemType.SubtypeId;

            ItemNames[itemType] = displayName;
            if (ItemNames.Count > ITEM_CACHE_LIMIT)
                ItemNames.Remove(ItemNames.Keys.First());
            return displayName;
        }
    }
}
