using System.Collections.Generic;

namespace LcdMod.Client.Gui.Styling
{
    interface IStyleValueEntry
    {
        StylePropertyBase Property { get; }
    }

    sealed class StyleValueEntry<TValue> : IStyleValueEntry
    {
        public StyleValueEntry(
            StyleProperty<TValue> property,
            StyleValue<TValue> value)
        {
            Property = property;
            Value = value;
        }

        public StyleProperty<TValue> Property { get; private set; }
        public StyleValue<TValue> Value { get; private set; }

        StylePropertyBase IStyleValueEntry.Property
        {
            get { return Property; }
        }
    }

    public sealed class ResourceSet
    {
        readonly Dictionary<int, IStyleValueEntry> _values =
            new Dictionary<int, IStyleValueEntry>();

        public ResourceSet Set<TValue>(
            StyleProperty<TValue> property,
            TValue value)
        {
            _values[property.Id] = new StyleValueEntry<TValue>(
                property,
                new LiteralStyleValue<TValue>(value));

            return this;
        }

        public ResourceSet Set<TValue>(
            StyleProperty<TValue> property,
            ResourceKey<TValue> key)
        {
            _values[property.Id] = new StyleValueEntry<TValue>(
                property,
                new ResourceStyleValue<TValue>(key));

            return this;
        }

        public bool TryResolve<TValue>(
            IVisualStyleScope target,
            StyleProperty<TValue> property,
            out TValue value)
        {
            IStyleValueEntry raw;
            if (_values.TryGetValue(property.Id, out raw))
            {
                StyleValueEntry<TValue> typed = raw as StyleValueEntry<TValue>;
                if (typed != null)
                    return typed.Value.TryResolve(target, out value);
            }

            value = default(TValue);
            return false;
        }
    }
}
