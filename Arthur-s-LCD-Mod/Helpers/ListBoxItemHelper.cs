using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.ModAPI;
using VRage.Utils;

namespace Graph.Helpers
{
    public static class ListBoxItemHelper
    {
        /// <summary>
        /// Per type Cache, can be <see cref="long"/> for Blocks, <see cref="string"/>. For Groups or Item Category, <see cref="MyDefinitionId"/> for Items, or others
        /// </summary>
        public static readonly Dictionary<Type, Dictionary<object, MyTerminalControlListBoxItem>> PerTypeCache = new Dictionary<Type, Dictionary<object, MyTerminalControlListBoxItem>>();
        
        public static MyTerminalControlListBoxItem GetOrComputeListBoxItem(string text, string tooltip, object item)
        {
            MyTerminalControlListBoxItem listBoxItem;
            var cache = GetCacheForObject(item);

            if (cache.TryGetValue(item, out listBoxItem))
            {
                listBoxItem.Text = MyStringId.GetOrCompute(text);
                listBoxItem.Tooltip = MyStringId.GetOrCompute(tooltip);
                return listBoxItem;
            }

            listBoxItem = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(text), MyStringId.GetOrCompute(tooltip), item);
            cache[item] = listBoxItem;
            return listBoxItem;
        }
        
        public static bool TryGetListBoxItem(object item, out MyTerminalControlListBoxItem listBoxItem) => GetCacheForObject(item).TryGetValue(item, out listBoxItem);

        public static Dictionary<object, MyTerminalControlListBoxItem> GetCacheForObject(object item)
        {
            Dictionary<object, MyTerminalControlListBoxItem> cache;
            if (PerTypeCache.TryGetValue(item.GetType(), out cache)) 
                return cache;

            cache = new Dictionary<object, MyTerminalControlListBoxItem>();
            PerTypeCache[item.GetType()] = cache;
            return cache;
        }
    }
}