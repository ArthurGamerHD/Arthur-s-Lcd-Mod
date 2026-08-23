using LcdMod.Client.Gui.ControlsTemplates.Basic;

namespace LcdMod.Client.Apps
{
    public sealed partial class ButtonPadApp
    {
        sealed partial class ButtonPadEntryDialog
        {
            sealed class SelectedSpriteButtonModel : ButtonModel
            {
                public string SpriteName { get; set; }
            }
        }
    }
}
