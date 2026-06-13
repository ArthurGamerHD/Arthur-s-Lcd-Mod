using System.Collections.Generic;

namespace LcdMod.Client.Gui.Styling
{
    interface IResourceValue
    {
        ResourceKeyBase Key { get; }
    }

    sealed class ResourceValue<TValue> : IResourceValue
    {
        public ResourceValue(ResourceKey<TValue> key, TValue value)
        {
            Key = key;
            Value = value;
        }

        public ResourceKey<TValue> Key { get; private set; }
        public TValue Value { get; private set; }

        ResourceKeyBase IResourceValue.Key
        {
            get { return Key; }
        }
    }

    public sealed class ResourceTree
    {
        readonly Dictionary<int, IResourceValue> _values =
            new Dictionary<int, IResourceValue>();

        public ResourceTree Set<TValue>(ResourceKey<TValue> key, TValue value)
        {
            _values[key.Id] = new ResourceValue<TValue>(key, value);
            return this;
        }

        public bool TryGet<TValue>(ResourceKey<TValue> key, out TValue value)
        {
            IResourceValue raw;
            if (_values.TryGetValue(key.Id, out raw))
            {
                ResourceValue<TValue> typed = raw as ResourceValue<TValue>;
                if (typed != null)
                {
                    value = typed.Value;
                    return true;
                }
            }

            value = default(TValue);
            return false;
        }
    }
}
