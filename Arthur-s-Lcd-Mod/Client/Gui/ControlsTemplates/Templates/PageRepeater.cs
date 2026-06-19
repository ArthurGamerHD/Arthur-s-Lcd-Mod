using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;

namespace LcdMod.Client.Gui.ControlsTemplates.Templates
{
    public sealed class PageRepeater<TItem, TControl>
        where TControl : ControlTemplate
    {
        readonly List<TControl> _realized = new List<TControl>();
        IDataTemplate<TItem> _itemTemplate;
        Action<TControl, TItem, int> _bindControl;

        public IDataTemplate<TItem> ItemTemplate
        {
            get { return _itemTemplate; }
            set
            {
                if (ReferenceEquals(_itemTemplate, value))
                    return;

                _itemTemplate = value;
                _realized.Clear();
            }
        }

        public Action<TControl, TItem, int> BindControl
        {
            get { return _bindControl; }
            set { _bindControl = value; }
        }

        public int BindTo(PagesPanel host, IList<TItem> items)
        {
            if (host == null)
                return 0;

            int count = items?.Count ?? 0;
            EnsureRealized(count, items);

            for (int i = 0; i < _realized.Count; i++)
            {
                TControl control = _realized[i];
                if (control == null)
                    continue;

                if (i >= count)
                {
                    control.SetVisible(false);
                    if (ReferenceEquals(control.Parent, host))
                        host.RemoveChild(control);
                    continue;
                }

                if (!ReferenceEquals(control.Parent, host))
                    host.AddChild(control);

                if (_bindControl != null)
                    _bindControl(control, items[i], i);

                control.SetVisible(true);
            }

            return count;
        }

        void EnsureRealized(int count, IList<TItem> items)
        {
            if (_itemTemplate == null && count > 0)
                throw new InvalidOperationException("PageRepeater.ItemTemplate is required.");

            while (_realized.Count < count)
            {
                int index = _realized.Count;
                ControlTemplate control = _itemTemplate.Build(items[index], index);
                TControl typed = control as TControl;
                if (typed == null)
                    throw new InvalidOperationException("PageRepeater item template returned the wrong control type.");

                _realized.Add(typed);
            }
        }
    }
}
