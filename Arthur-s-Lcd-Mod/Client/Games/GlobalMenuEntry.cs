using System;
using System.Collections.Generic;
using LcdMod.Client.Helpers;

namespace LcdMod.Client.Games
{
    /// <summary>One entry of a game's menu bar: a command, or a submenu when it has children.</summary>
    public sealed class GlobalMenuEntry
    {
        public string MenuItem { get; set; }
        public List<GlobalMenuEntry> Children { get; set; }
        public Action<object, object> OnClick { get; set; }
        public object DataContext { get; set; }
        public string Icon { get; set; }

        public GlobalMenuEntry()
        {
        }

        public GlobalMenuEntry(string menuItem, Action<object, object> onClick = null)
        {
            MenuItem = LocHelper.GetLoc(menuItem);
            OnClick = onClick;
        }

        public GlobalMenuEntry(string menuItem, List<GlobalMenuEntry> children)
        {
            MenuItem = LocHelper.GetLoc(menuItem);
            Children = children;
        }

        public bool HasChildren => Children != null && Children.Count > 0;
    }
}
