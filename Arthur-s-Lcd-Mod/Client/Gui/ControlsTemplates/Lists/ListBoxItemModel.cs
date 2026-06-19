namespace LcdMod.Client.Gui.ControlsTemplates.Lists
{
    public sealed class ListBoxItemModel<T> : ControlModelBase
    {
        public ListBoxItemModel(ListBoxModel<T> owner, T item, int index)
        {
            Cursor = CursorType.Hand;
            Update(owner, item, index);
        }

        public ListBoxModel<T> Owner { get; private set; }
        public T Item { get; private set; }
        public int Index { get; private set; }

        public void Update(ListBoxModel<T> owner, T item, int index)
        {
            Owner = owner;
            Item = item;
            Index = index;
        }

        public bool Selected => Owner != null && Owner.IsSelected(Item);

        public override bool CanClick => Owner != null;

        public override bool Click(object sender)
        {
            if (Owner == null)
                return false;

            Owner.SelectClicked(Item, Index);
            return true;
        }

        public override string ToString()
        {
            return Owner == null ? string.Empty : Owner.GetText(Item);
        }
    }
}
