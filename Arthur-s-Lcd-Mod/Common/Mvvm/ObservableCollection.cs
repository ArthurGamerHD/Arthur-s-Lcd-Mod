using System;
using System.Collections;
using System.Collections.Generic;

namespace LcdMod.Common.Mvvm
{
    public class ObservableList<T> : ObservableObject, IObservableList<T>
    {
        public List<T> Items { get; } = new List<T>();

        public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void CopyTo(T[] array, int arrayIndex)
        {
            Items.CopyTo(array, arrayIndex);
        }

        public int Count => Items.Count;

        public void Add(T value)
        {
            Items.Add(value);
            RaiseMutation(delegate { RaiseItemAdded(value); });
        }

        public bool Contains(T value)
        {
            return Items.Contains(value);
        }

        public void Clear()
        {
            if (Items.Count == 0)
                return;

            var removedItems = Items.ToArray();
            Items.Clear();
            RaiseMutation(delegate
            {
                Exception firstError = null;
                for (var i = 0; i < removedItems.Length; i++)
                {
                    try
                    {
                        RaiseItemRemoved(removedItems[i]);
                    }
                    catch (Exception e)
                    {
                        if (firstError == null)
                            firstError = e;
                    }
                }

                if (firstError != null)
                    throw firstError;
            });
        }

        public int IndexOf(T value)
        {
            return Items.IndexOf(value);
        }

        public void Insert(int index, T value)
        {
            Items.Insert(index, value);
            RaiseMutation(delegate { RaiseItemAdded(value); });
        }

        public bool Remove(T value)
        {
            var index = Items.IndexOf(value);
            if (index < 0)
                return false;

            Items.RemoveAt(index);
            RaiseMutation(delegate { RaiseItemRemoved(value); });
            return true;
        }

        public void RemoveAt(int index)
        {
            var removedItem = Items[index];
            Items.RemoveAt(index);
            RaiseMutation(delegate { RaiseItemRemoved(removedItem); });
        }

        public void Move(int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex)
                return;
            if (oldIndex < 0 || oldIndex >= Items.Count)
                return;
            if (newIndex < 0 || newIndex >= Items.Count)
                return;

            var item = Items[oldIndex];
            Items.RemoveAt(oldIndex);
            Items.Insert(newIndex, item);
            RaiseMutation(delegate { RaiseItemChanged(item); });
        }

        public T this[int index]
        {
            get { return Items[index]; }
            set
            {
                var removedItem = Items[index];
                Items[index] = value;

                if (EqualityComparer<T>.Default.Equals(removedItem, value))
                    return;

                RaiseMutation(delegate { RaiseItemChanged(value); });
            }
        }

        public bool IsReadOnly => false;

        public event Action<IObservableCollection<T>, T> ItemAdded;
        public event Action<IObservableCollection<T>, T> ItemRemoved;
        public event Action<IObservableCollection<T>, T> ItemChanged;

        void RaiseItemAdded(T item) => InvokeHandlers(ItemAdded, item);
        void RaiseItemRemoved(T item) => InvokeHandlers(ItemRemoved, item);
        void RaiseItemChanged(T item) => InvokeHandlers(ItemChanged, item);

        void RaiseMutation(Action itemNotification)
        {
            Exception firstError = null;
            try
            {
                RaisePropertyChanged<List<T>>(nameof(Items));
            }
            catch (Exception e)
            {
                firstError = e;
            }

            try
            {
                itemNotification();
            }
            catch (Exception e)
            {
                if (firstError == null)
                    firstError = e;
            }

            if (firstError != null)
                throw firstError;
        }

        void InvokeHandlers(Action<IObservableCollection<T>, T> handlers, T item)
        {
            if (handlers == null)
                return;

            Exception firstError = null;
            foreach (Action<IObservableCollection<T>, T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, item);
                }
                catch (Exception e)
                {
                    if (firstError == null)
                        firstError = e;
                }
            }

            if (firstError != null)
                throw firstError;
        }
    }
}
