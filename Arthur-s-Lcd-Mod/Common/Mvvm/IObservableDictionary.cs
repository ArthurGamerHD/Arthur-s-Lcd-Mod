using System;
using System.Collections.Generic;

namespace LcdMod.Common.Mvvm
{
    public interface IObservableDictionary<TKey, TValue> : IObservableCollection<KeyValuePair<TKey,TValue>>, IDictionary<TKey, TValue>
    {
        event Action<IObservableCollection<KeyValuePair<TKey,TValue>>, KeyValuePair<TKey,TValue>> ItemChanged;
    }
}