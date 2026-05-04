using Graph.Apps.Diagnostic;
using Graph.Apps.Inventory;
using Graph.System.TerminalControls.Generic;
using Sandbox.ModAPI;

namespace Graph.System.TerminalControls.Blueprint
{
    public partial class ListboxProjectorSelection : ListboxSingleBlockSelection<IMyProjector>
    {
        public ListboxProjectorSelection()
        {
            CreateListbox("ProjectorSelection", "DisplayName_BlockGroup_Projectors");
        }
    }
}
