using System;
using System.Collections;
using System.Collections.Generic;
using Adk.Gui.Collections.Reactive;

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
            RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value, Items.Count - 1));
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
            // Keep the removed items available to consumers that detach item subscriptions.
            RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removedItems, 0));
        }

        public int IndexOf(T value)
        {
            return Items.IndexOf(value);
        }

        public void Insert(int index, T value)
        {
            Items.Insert(index, value);
            RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value, index));
        }

        public bool Remove(T value)
        {
            var index = Items.IndexOf(value);
            if (index < 0)
                return false;

            Items.RemoveAt(index);
            RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, value, index));
            return true;
        }

        public void RemoveAt(int index)
        {
            var removedItem = Items[index];
            Items.RemoveAt(index);
            RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removedItem, index));
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
            RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, newIndex, oldIndex));
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

                RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, removedItem, index));
            }
        }

        public bool IsReadOnly => false;

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        void RaiseMutation(NotifyCollectionChangedEventArgs args)
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
                OnCollectionChanged(args);
            }
            catch (Exception e)
            {
                if (firstError == null)
                    firstError = e;
            }

            if (firstError != null)
                throw firstError;
        }

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs args)
        {
            var handlers = CollectionChanged;
            if (handlers == null)
                return;

            Exception firstError = null;
            foreach (NotifyCollectionChangedEventHandler handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
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
