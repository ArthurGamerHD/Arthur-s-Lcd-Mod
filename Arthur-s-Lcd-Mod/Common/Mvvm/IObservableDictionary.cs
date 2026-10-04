using Adk.Gui.Collections.Reactive;
using System.Collections.Generic;

namespace LcdMod.Common.Mvvm
{
    public interface IObservableDictionary<TKey, TValue> : INotifyCollectionChanged, IDictionary<TKey, TValue>
    {
    }
}
