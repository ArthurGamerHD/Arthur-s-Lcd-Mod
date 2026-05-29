using System;
using System.Collections.Generic;

namespace LcdMod.Client.Terminal.Actions.Models
{
    public interface ICustomAction
    {
        string Name { get; set; }
        string BaseId { get; set; }
        HashSet<Type> Types { get; }
    }
}