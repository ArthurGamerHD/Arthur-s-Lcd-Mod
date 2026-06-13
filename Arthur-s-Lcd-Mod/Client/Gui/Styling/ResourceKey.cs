namespace LcdMod.Client.Gui.Styling
{
    public abstract class ResourceKeyBase
    {
        protected ResourceKeyBase(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; private set; }
        public string Name { get; private set; }
    }

    public sealed class ResourceKey<TValue> : ResourceKeyBase
    {
        internal ResourceKey(int id, string name)
            : base(id, name)
        {
        }
    }

    public static class ResourceKey
    {
        static int _nextId;

        public static ResourceKey<TValue> Register<TValue>(string name)
        {
            return new ResourceKey<TValue>(_nextId++, name);
        }
    }
}
