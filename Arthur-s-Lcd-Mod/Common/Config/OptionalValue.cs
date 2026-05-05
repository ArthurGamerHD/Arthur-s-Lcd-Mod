using System;
using ProtoBuf;

namespace LcdMod.Common.Config
{
    [ProtoContract]
    public class OptionalValue<T>
    {
        [ProtoMember(1)] public T Value { get; set; }
        [ProtoMember(2)] public bool HasValue { get; set; }

        public T Get(bool getDefault, Func<T> getDefaultValue) => getDefault ? getDefaultValue() : Get(getDefaultValue);

        public T Get(Func<T> defaultGetter) => !HasValue ? defaultGetter() : Value;

        public void Set(T value)
        {
            Value = value;
            HasValue = true;
        }

        public void Clear()
        {
            HasValue = false;
        }
    }
}