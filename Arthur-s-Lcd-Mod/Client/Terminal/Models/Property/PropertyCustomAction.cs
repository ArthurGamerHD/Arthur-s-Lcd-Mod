using System;
using System.Collections.Generic;
using LcdMod.Client.Terminal.Actions.Models;
using Sandbox.ModAPI.Interfaces;

namespace LcdMod.Client.Terminal.Models.Property
{
    public class PropertyCustomAction<T> : ICustomAction
    {
        public string Name { get; set; }
        public string BaseId { get; set; }
        public HashSet<Type> Types { get; } = new HashSet<Type>();

        public ITerminalProperty<T> Property { get; set; }
    }
}