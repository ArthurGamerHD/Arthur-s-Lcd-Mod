using System;
using System.Collections.Generic;

namespace LcdMod.Common.Mvvm
{
    public interface IObservableCollection<T> : IEnumerable<T>
    {
        event Action<IObservableCollection<T>, T> ItemAdded;
        event Action<IObservableCollection<T>, T> ItemRemoved;
    }
}