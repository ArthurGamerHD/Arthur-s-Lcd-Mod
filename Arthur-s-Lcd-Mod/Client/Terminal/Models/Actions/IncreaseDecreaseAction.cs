using System;
using System.Collections.Generic;
using LcdMod.Client.Terminal.Actions.Models;
using Sandbox.ModAPI.Interfaces;

namespace LcdMod.Client.Terminal.Models.Actions
{
    public class IncreaseDecreaseAction : ICustomAction
    {
        public ITerminalAction Increase { get; set; }
        public ITerminalAction Decrease { get; set; }
        public string Name { get; set; }
        public string BaseId { get; set; }
        public HashSet<Type> Types { get; } = new HashSet<Type>();
    }
}