using System;
using System.Collections.Generic;

namespace LcdMod.Common.Mvvm
{
    public interface IObservableList<T> : IObservableCollection<T>, IList<T>
    {
        event Action<IObservableCollection<T>, T> ItemChanged;
    }
}