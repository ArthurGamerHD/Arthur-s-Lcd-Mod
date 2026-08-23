using LcdMod.Client.Gui.ControlsTemplates.Basic;

namespace LcdMod.Client.Apps
{
    public sealed partial class ButtonPadApp
    {
        sealed class PadButtonModel : ButtonModel
        {
            public int Index { get; set; }

            public int Row { get; set; }

            public int Column { get; set; }

            public bool Configured { get; set; }

            public string SpriteName { get; set; }

            public string Title { get; set; }

            public string BackgroundColor { get; set; }

            public string Status { get; set; }
        }
    }
}
