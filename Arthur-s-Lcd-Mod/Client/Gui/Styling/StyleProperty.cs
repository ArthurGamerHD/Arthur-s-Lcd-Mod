using System;

namespace LcdMod.Client.Gui.Styling
{
    public abstract class StylePropertyBase
    {
        protected StylePropertyBase(
            int id,
            Type ownerType,
            string name,
            bool hasDefaultValue)
        {
            Id = id;
            OwnerType = ownerType;
            Name = name;
            HasDefaultValue = hasDefaultValue;
        }

        public int Id { get; private set; }
        public Type OwnerType { get; private set; }
        public string Name { get; private set; }
        public bool HasDefaultValue { get; private set; }
    }

    public sealed class StyleProperty<TValue> : StylePropertyBase
    {
        internal StyleProperty(
            int id,
            Type ownerType,
            string name,
            bool hasDefaultValue,
            TValue defaultValue)
            : base(id, ownerType, name, hasDefaultValue)
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
            TValue? defaultValue)
            where TValue : struct
        {
            return new StyleProperty<TValue>(
                _nextId++,
                typeof(TControl),
                name,
                defaultValue.HasValue,
                defaultValue.GetValueOrDefault());
        }

        public static StyleProperty<TValue> Register<TControl, TValue>(
            string name,
            TValue defaultValue)
        {
            return new StyleProperty<TValue>(
                _nextId++,
                typeof(TControl),
                name,
                defaultValue != null,
                defaultValue);
        }
    }
}
