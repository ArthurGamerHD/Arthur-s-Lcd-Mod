using System;
using System.Collections.Generic;

namespace LcdMod.Common.Mvvm
{
    public abstract class ObservableObject
    {
        public event Action<ObservableObject, string> PropertyChanged;

        protected bool SetObservableProperty<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, propertyName);
            return true;
        }
    }
}
