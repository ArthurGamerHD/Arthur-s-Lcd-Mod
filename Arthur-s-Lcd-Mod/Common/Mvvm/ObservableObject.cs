using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LcdMod.Common.Mvvm
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event Action<ObservableObject, string> PropertyChanged;
        event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
        {
            add { StandardPropertyChanged += value; }
            remove { StandardPropertyChanged -= value; }
        }
        event PropertyChangedEventHandler StandardPropertyChanged;

        protected bool RaiseAndSetIfChanged<T>(ref T field, T value, string propertyName)
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
            Exception firstError = null;
            if (handlers != null)
            {
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
            }

            var standardHandlers = StandardPropertyChanged;
            if (standardHandlers != null)
            {
                var args = new PropertyChangedEventArgs(propertyName);
                foreach (PropertyChangedEventHandler handler in standardHandlers.GetInvocationList())
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
            }

            if (firstError != null)
                throw firstError;
        }
    }
}
