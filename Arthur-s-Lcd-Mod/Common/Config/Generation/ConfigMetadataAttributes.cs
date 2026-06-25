using System;

namespace LcdMod.Common.Config.Generation
{
    /// <summary>
    /// Compile-time metadata consumed by AppConfigGenerator. MDK removes this declaration and all
    /// usages from exported Space Engineers source; runtime code must never inspect it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class LcdAppAttribute : Attribute
    {
        public LcdAppAttribute(int id)
        {
            Id = id;
        }

        public int Id { get; private set; }
        public string Name { get; set; }
    }

    /// <summary>Declares one required component in an app-owned configuration schema.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    internal sealed class ConfigComponentAttribute : Attribute
    {
        public ConfigComponentAttribute(string slot, Type componentType)
        {
            Slot = slot;
            ComponentType = componentType;
        }

        public string Slot { get; private set; }
        public Type ComponentType { get; private set; }
        public string PropertyName { get; set; }
    }

    /// <summary>Maps a persisted surface host to its concrete app identity.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class LcdSurfaceAttribute : Attribute
    {
        public LcdSurfaceAttribute(Type appClass)
        {
            AppClass = appClass;
        }

        public Type AppClass { get; private set; }
    }
}
