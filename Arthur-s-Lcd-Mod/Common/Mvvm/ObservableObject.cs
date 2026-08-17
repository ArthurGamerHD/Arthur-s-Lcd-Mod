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
            RaisePropertyChangedCore(propertyName);
            return true;
        }

        public bool RaisePropertyChanged<T>(string propertyName)
        {
            RaisePropertyChangedCore(propertyName);
            return true;
        }

        void RaisePropertyChangedCore(string propertyName)
        {
            var handlers = PropertyChanged;
            if (handlers == null)
                return;

            Exception firstError = null;
            foreach (Action<ObservableObject, string> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, propertyName);
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
