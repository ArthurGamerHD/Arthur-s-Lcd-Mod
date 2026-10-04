using Adk.Gui.Collections.Reactive;
using System.Collections.Generic;

namespace LcdMod.Common.Mvvm
{
    public interface IObservableList<T> : INotifyCollectionChanged, IList<T>
    {
    }
}
