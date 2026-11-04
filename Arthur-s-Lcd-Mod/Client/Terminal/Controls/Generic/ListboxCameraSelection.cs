using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public partial class ListboxCameraSelection : ListboxSingleBlockSelection<IMyCameraBlock>
    {
        public ListboxCameraSelection()
        {
            CreateListbox("CameraSelection", MOD_PREFIX + "ExternalSensors_Camera", Constants.CAMERA_REFERENCE);
        }
    }
}
