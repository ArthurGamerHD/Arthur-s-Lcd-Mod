using System;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public sealed class ResourceKeyNotFoundException : Exception
    {
        public ResourceKeyNotFoundException(string key)
            : base("Resource key not found: " + (key ?? "<null>"))
        {
            Key = key;
        }

        public ResourceKeyNotFoundException(string key, string resourceName)
            : base("Resource key not found: " + (key ?? "<null>") + " in " + (resourceName ?? "<unknown>"))
        {
            Key = key;
            ResourceName = resourceName;
        }

        public string Key { get; private set; }
        public string ResourceName { get; private set; }
    }
}
