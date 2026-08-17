using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Gui.ControlsTemplates.Lists
{
    public sealed class ListBoxItemModel<T> : ControlModelBase
    {
        ObservableObject _observableItem;

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
            var observableItem = (object)item as ObservableObject;
            if (!ReferenceEquals(_observableItem, observableItem))
            {
                UnbindItem();
                _observableItem = observableItem;
                if (_observableItem != null)
                    _observableItem.PropertyChanged += OnItemPropertyChanged;
            }

            Owner = owner;
            Item = item;
            Index = index;
        }

        public void UnbindItem()
        {
            if (_observableItem == null)
                return;

            _observableItem.PropertyChanged -= OnItemPropertyChanged;
            _observableItem = null;
        }

        void OnItemPropertyChanged(ObservableObject sender, string propertyName)
        {
            if (ReferenceEquals(sender, _observableItem))
                RaisePropertyChanged<T>(nameof(Item));
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
