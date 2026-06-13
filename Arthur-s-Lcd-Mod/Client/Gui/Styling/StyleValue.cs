namespace LcdMod.Client.Gui.Styling
{
    public abstract class StyleValue<TValue>
    {
        public abstract bool TryResolve(IVisualStyleScope target, out TValue value);
    }

    public sealed class LiteralStyleValue<TValue> : StyleValue<TValue>
    {
        readonly TValue _value;

        public LiteralStyleValue(TValue value)
        {
            _value = value;
        }

        public override bool TryResolve(IVisualStyleScope target, out TValue value)
        {
            value = _value;
            return true;
        }
    }

    public sealed class ResourceStyleValue<TValue> : StyleValue<TValue>
    {
        readonly ResourceKey<TValue> _key;

        public ResourceStyleValue(ResourceKey<TValue> key)
        {
            _key = key;
        }

        public override bool TryResolve(IVisualStyleScope target, out TValue value)
        {
            return ScopedResourceResolver.TryResolve(target, _key, out value);
        }
    }
}
