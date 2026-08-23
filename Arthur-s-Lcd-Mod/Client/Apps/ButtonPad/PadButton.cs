using LcdMod.Client.Gui.ControlsTemplates.Basic;
using VRageMath;

namespace LcdMod.Client.Apps
{
    public sealed partial class ButtonPadApp
    {
        sealed class PadButton : Button
        {
            public PadButton(RectangleF bounds, ButtonModel model)
                : base(bounds, model)
            {
                TitleText = new FitTextControl();
                StatusText = new FitTextControl();
                AddChild(TitleText);
                AddChild(StatusText);
            }

            public FitTextControl TitleText { get; private set; }

            public FitTextControl StatusText { get; private set; }

            protected override bool ShouldRenderStyleBorder()
            {
                return false;
            }
        }
    }
}
