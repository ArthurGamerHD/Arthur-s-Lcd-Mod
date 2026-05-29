using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Interfaces;
using IMyTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace LcdMod.Client.Terminal.Models.Actions
{
    public class IncreaseDecreaseAction : ICustomAction
    {
        public ITerminalAction Increase { get; set; }
        public ITerminalAction Decrease { get; set; }
        public string Name { get; set; }
        public string BaseId { get; set; }
        public HashSet<Type> Types { get; } = new HashSet<Type>();
        public bool Enabled(IMyTerminalBlock block) => Increase.IsEnabled(block) || Decrease.IsEnabled(block);
    }
}