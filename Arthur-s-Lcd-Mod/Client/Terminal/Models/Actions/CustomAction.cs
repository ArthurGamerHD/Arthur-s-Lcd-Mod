using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Interfaces;
using IMyTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace LcdMod.Client.Terminal.Models.Actions
{
    public class CustomAction : ICustomAction, ICustomActionValueWriter
    {
        public string Name { get; set; }
        public ITerminalAction Action { get; set; }
        public string BaseId { get; set; }
        public HashSet<Type> Types { get; } = new HashSet<Type>();
        public bool Enabled(IMyTerminalBlock block) => Action.IsEnabled(block);

        public void WriteValue(IMyTerminalBlock block, StringBuilder appendTo)
        {
            if (Action != null && block != null && appendTo != null)
                Action.WriteValue(block, appendTo);
        }
    }
}
