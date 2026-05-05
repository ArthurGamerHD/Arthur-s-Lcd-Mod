using LcdMod.Client.TerminalControls.Generic;
using Sandbox.ModAPI;

namespace LcdMod.Client.TerminalControls.Blueprint
{
    public partial class ListboxProjectorSelection : ListboxSingleBlockSelection<IMyProjector>
    {
        public ListboxProjectorSelection()
        {
            CreateListbox("ProjectorSelection", "DisplayName_BlockGroup_Projectors");
        }
    }
}
