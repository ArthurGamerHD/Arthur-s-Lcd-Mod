using System;
using System.Collections.Generic;
using LcdMod.Client.Terminal.Actions.Models;
using Sandbox.ModAPI.Interfaces;

namespace LcdMod.Client.Terminal.Models.Actions
{
    public class CustomAction : ICustomAction
    {
        public string Name { get; set; }
        public ITerminalAction Action { get; set; }
        public string BaseId { get; set; }
        public HashSet<Type> Types { get; } = new HashSet<Type>();
    }
}