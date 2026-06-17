using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates;

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
        static readonly Dictionary<string, ResourceKeyBase> Resources = new Dictionary<string, ResourceKeyBase>();
        static int _nextId;

        public static ResourceKey<TValue> Register<TValue>(string name)
        {
            var key = new ResourceKey<TValue>(_nextId++, name);
            Resources.Add(name, key);
            return key;
        }

        public static ResourceKey<TValue> Get<TValue>(string name)
        {
            ResourceKeyBase key;
            if (!Resources.TryGetValue(name, out key))
                throw new ResourceKeyNotFoundException(name);

            var resourceKey = key as ResourceKey<TValue>;
            if (resourceKey == null)
                throw new InvalidCastException($"Key {name} was found but does not have the correct type");
            
            return resourceKey;
        }
    }
}
