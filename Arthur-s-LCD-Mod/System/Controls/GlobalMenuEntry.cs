using System;
using System.Collections.Generic;
using Graph.Helpers;

namespace Graph.System.Controls
{
    public sealed class GlobalMenuEntry
    {
        public string MenuItem { get; set; }
        public List<GlobalMenuEntry> Children { get; set; }
        public Action<object, object> OnClick { get; set; }
        public object DataContext { get; set; }
        public string Icon { get; set; }
        public CursorType Cursor { get; set; }

        public GlobalMenuEntry()
        {
            Cursor = CursorType.Hand;
        }

        public GlobalMenuEntry(string menuItem, Action<object, object> onClick = null)
            : this()
        {
            MenuItem = LocHelper.GetLoc(menuItem);
            OnClick = onClick;
        }

        public GlobalMenuEntry(string menuItem, List<GlobalMenuEntry> children)
            : this()
        {
            MenuItem = LocHelper.GetLoc(menuItem);;
            Children = children;
        }

        public bool HasChildren
        {
            get { return Children != null && Children.Count > 0; }
        }
    }
}