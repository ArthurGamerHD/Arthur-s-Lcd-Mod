using LcdMod.Client.Terminal.Controls.Generic;
using Sandbox.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Blueprint
{
    public partial class ListboxProjectorSelection : ListboxSingleBlockSelection<IMyProjector>
    {
        public ListboxProjectorSelection()
        {
            CreateListbox("ProjectorSelection", "DisplayName_BlockGroup_Projectors");
        }
    }
}
