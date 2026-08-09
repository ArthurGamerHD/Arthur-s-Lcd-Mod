using System;

namespace LcdMod.Common.Mvvm
{
    /// <summary>Generates an observable property for a field on a partial ObservableObject.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class ObservablePropertyAttribute : Attribute
    {
    }
}
