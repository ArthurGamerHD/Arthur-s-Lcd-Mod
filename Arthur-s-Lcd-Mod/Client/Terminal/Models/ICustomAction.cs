using System;
using System.Collections.Generic;
using IMyTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace LcdMod.Client.Terminal.Models
{
    public interface ICustomAction
    {
        string Name { get; set; }
        string BaseId { get; set; }
        HashSet<Type> Types { get; }
        bool Enabled(IMyTerminalBlock block);
    }
}