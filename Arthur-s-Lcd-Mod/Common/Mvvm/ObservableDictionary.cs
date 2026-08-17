using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
            RaiseMutation(delegate { RaiseItemAdded(key, value); });
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
            RaiseMutation(delegate { RaiseItemRemoved(key, value); });
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
                    RaiseMutation(delegate { RaiseItemChanged(key, value); });
                else
                    RaiseMutation(delegate { RaiseItemAdded(key, value); });
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
            RaiseMutation(delegate
            {
                Exception firstError = null;
                foreach (var item in removedItems)
                {
                    try
                    {
                        RaiseItemRemoved(item);
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

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)Items).Contains(item);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (!((ICollection<KeyValuePair<TKey, TValue>>)Items).Remove(item))
                return false;

            RaiseMutation(delegate { RaiseItemRemoved(item); });
            return true;
        }

        public bool IsReadOnly => false;

        public event Action<IObservableCollection<KeyValuePair<TKey, TValue>>, KeyValuePair<TKey, TValue>> ItemAdded;
        public event Action<IObservableCollection<KeyValuePair<TKey, TValue>>, KeyValuePair<TKey, TValue>> ItemRemoved;
        public event Action<IObservableCollection<KeyValuePair<TKey, TValue>>, KeyValuePair<TKey, TValue>> ItemChanged;

        public bool NotifyItemChanged(TKey key)
        {
            TValue value;
            if (!Items.TryGetValue(key, out value))
                return false;

            RaiseItemChanged(key, value);
            return true;
        }

        public void NotifyItemRemoved(TKey key, TValue previousValue)
        {
            RaiseItemRemoved(key, previousValue);
        }

        void RaiseItemAdded(KeyValuePair<TKey, TValue> item) => InvokeHandlers(ItemAdded, item);
        void RaiseItemAdded(TKey key, TValue value) => RaiseItemAdded(new KeyValuePair<TKey, TValue>(key, value));
        void RaiseItemRemoved(KeyValuePair<TKey, TValue> item) => InvokeHandlers(ItemRemoved, item);
        void RaiseItemRemoved(TKey key, TValue value) => RaiseItemRemoved(new KeyValuePair<TKey, TValue>(key, value));
        void RaiseItemChanged(KeyValuePair<TKey, TValue> item) => InvokeHandlers(ItemChanged, item);
        void RaiseItemChanged(TKey key, TValue value) => RaiseItemChanged(new KeyValuePair<TKey, TValue>(key, value));

        void RaiseMutation(Action itemNotification)
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

        void InvokeHandlers(
            Action<IObservableCollection<KeyValuePair<TKey, TValue>>, KeyValuePair<TKey, TValue>> handlers,
            KeyValuePair<TKey, TValue> item)
        {
            if (handlers == null)
                return;

            Exception firstError = null;
            foreach (Action<IObservableCollection<KeyValuePair<TKey, TValue>>, KeyValuePair<TKey, TValue>> handler
                     in handlers.GetInvocationList())
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
