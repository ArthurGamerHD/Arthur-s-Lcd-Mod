using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Templates
{
    public sealed class Repeater<TItem> : Panel
    {
        readonly List<ControlTemplate> _realized = new List<ControlTemplate>();
        IList<TItem> _items = new List<TItem>();
        IDataTemplate<TItem> _itemTemplate;
        Action<ControlTemplate, TItem, int> _bindItem;
        Action<ControlTemplate, RectangleF, TItem, int> _arrangeItem;

        public Repeater<TItem> Items(IList<TItem> items)
        {
            _items = items ?? new List<TItem>();
            InvalidateLayout();
            return this;
        }

        public Repeater<TItem> ItemTemplate(IDataTemplate<TItem> template)
        {
            if (ReferenceEquals(_itemTemplate, template))
                return this;

            _itemTemplate = template;
            ClearRealized();
            InvalidateLayout();
            return this;
        }

        public Repeater<TItem> ItemTemplate(Func<TItem, int, ControlTemplate> template)
        {
            return ItemTemplate(template == null ? null : Template.For(template));
        }

        public Repeater<TItem> Bind(Action<ControlTemplate, TItem, int> bindItem)
        {
            _bindItem = bindItem;
            InvalidateLayout();
            return this;
        }

        public Repeater<TItem> ArrangeItem(Action<ControlTemplate, RectangleF, TItem, int> arrangeItem)
        {
            _arrangeItem = arrangeItem;
            InvalidateLayout();
            return this;
        }

        protected override void ArrangeChildren()
        {
            RealizeItems();
        }

        void RealizeItems()
        {
            if (_itemTemplate == null)
            {
                HideAll();
                return;
            }

            int count = _items == null ? 0 : _items.Count;
            EnsureRealized(count);

            for (int i = 0; i < _realized.Count; i++)
            {
                ControlTemplate control = _realized[i];
                if (control == null)
                    continue;

                if (i >= count)
                {
                    control.SetVisible(false);
                    continue;
                }

                TItem item = _items[i];
                if (_bindItem != null)
                    _bindItem(control, item, i);

                if (_arrangeItem != null)
                    _arrangeItem(control, Bounds, item, i);
                else
                    control.Arrange(Bounds);

                control.SetVisible(true);
            }
        }

        void EnsureRealized(int count)
        {
            while (_realized.Count < count)
            {
                int index = _realized.Count;
                TItem item = _items[index];
                ControlTemplate control = _itemTemplate.Build(item, index);
                if (control == null)
                    throw new InvalidOperationException("Repeater item template returned null.");

                _realized.Add(control);
                AddChild(control);
            }
        }

        void HideAll()
        {
            for (int i = 0; i < _realized.Count; i++)
            {
                if (_realized[i] != null)
                    _realized[i].SetVisible(false);
            }
        }

        void ClearRealized()
        {
            for (int i = 0; i < _realized.Count; i++)
                RemoveChild(_realized[i]);

            _realized.Clear();
        }
    }
}
