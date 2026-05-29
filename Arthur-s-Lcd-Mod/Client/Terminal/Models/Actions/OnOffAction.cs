using Sandbox.ModAPI.Interfaces;

namespace LcdMod.Client.Terminal.Models.Actions
{
    public class OnOffAction : CustomAction
    {
        public ITerminalAction On { get; set; }
        public ITerminalAction Off { get; set; }
    }
}