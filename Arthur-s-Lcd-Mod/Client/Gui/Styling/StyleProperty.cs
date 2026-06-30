using System;

namespace LcdMod.Client.Gui.Styling
{
    public abstract class StylePropertyBase
    {
        protected StylePropertyBase(
            int id,
            Type ownerType,
            string name,
            bool hasDefaultValue,
            bool inherits)
        {
            Id = id;
            OwnerType = ownerType;
            Name = name;
            HasDefaultValue = hasDefaultValue;
            Inherits = inherits;
        }

        public int Id { get; private set; }
        public Type OwnerType { get; private set; }
        public string Name { get; private set; }
        public bool HasDefaultValue { get; private set; }
        public bool Inherits { get; private set; }
    }

    public sealed class StyleProperty<TValue> : StylePropertyBase
    {
        internal StyleProperty(
            int id,
            Type ownerType,
            string name,
            bool hasDefaultValue,
            TValue defaultValue,
            bool inherits)
            : base(id, ownerType, name, hasDefaultValue, inherits)
        {
            DefaultValue = defaultValue;
        }

        public TValue DefaultValue { get; private set; }
    }

    public static class StyleProperty
    {
        static int _nextId;

        public static StyleProperty<TValue> Register<TControl, TValue>(
            string name,
            TValue? defaultValue,
            bool inherits = true)
            where TValue : struct
        {
            return new StyleProperty<TValue>(
                _nextId++,
                typeof(TControl),
                name,
                defaultValue.HasValue,
                defaultValue.GetValueOrDefault(),
                inherits);
        }

        public static StyleProperty<TValue> Register<TControl, TValue>(
            string name,
            TValue defaultValue,
            bool inherits = true)
        {
            return new StyleProperty<TValue>(
                _nextId++,
                typeof(TControl),
                name,
                defaultValue != null,
                defaultValue,
                inherits);
        }
    }
}
