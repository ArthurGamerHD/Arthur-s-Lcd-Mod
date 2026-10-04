using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Adk.Gui.Collections.Reactive;

namespace LcdMod.Common.Mvvm
{
    public class ObservableDictionary<TKey, TValue> : ObservableObject, IObservableDictionary<TKey, TValue>
    { public Dictionary<TKey, TValue> Items { get; } = new Dictionary<TKey, TValue>();

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)Items).CopyTo(array, arrayIndex);
        }

        public int Count => Items.Count;

        public void Add(TKey key, TValue value)
        {
            Items.Add(key, value);
            RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new KeyValuePair<TKey, TValue>(key, value)));
        }

        public bool ContainsKey(TKey key)
        {
            return Items.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            TValue value;
            if (!Items.TryGetValue(key, out value))
                return false;

            Items.Remove(key);
            RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, new KeyValuePair<TKey, TValue>(key, value)));
            return true;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return Items.TryGetValue(key, out value);
        }

        public TValue this[TKey key]
        {
            get { return Items[key]; }
            set
            {
                TValue removedItem;
                var replacedItem = Items.TryGetValue(key, out removedItem);
                Items[key] = value;

                if (replacedItem && EqualityComparer<TValue>.Default.Equals(removedItem, value))
                    return;

                if (replacedItem)
                    RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace,
                        new KeyValuePair<TKey, TValue>(key, value), new KeyValuePair<TKey, TValue>(key, removedItem)));
                else
                    RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new KeyValuePair<TKey, TValue>(key, value)));
            }
        }

        public ICollection<TKey> Keys => Items.Keys;
        public ICollection<TValue> Values => Items.Values;

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            if (Items.Count == 0)
                return;

            var removedItems = Items.ToList();
            Items.Clear();
            RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removedItems));
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)Items).Contains(item);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (!((ICollection<KeyValuePair<TKey, TValue>>)Items).Remove(item))
                return false;

            RaiseMutation(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
            return true;
        }

        public bool IsReadOnly => false;

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public bool NotifyItemChanged(TKey key)
        {
            TValue value;
            if (!Items.TryGetValue(key, out value))
                return false;

            var item = new KeyValuePair<TKey, TValue>(key, value);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, item));
            return true;
        }

        public void NotifyItemRemoved(TKey key, TValue previousValue)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove,
                new KeyValuePair<TKey, TValue>(key, previousValue)));
        }

        void RaiseMutation(NotifyCollectionChangedEventArgs args)
        {
            Exception firstError = null;
            try
            {
                RaisePropertyChanged<Dictionary<TKey, TValue>>(nameof(Items));
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
