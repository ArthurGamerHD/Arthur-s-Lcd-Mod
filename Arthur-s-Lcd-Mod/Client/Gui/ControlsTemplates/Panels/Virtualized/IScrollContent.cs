using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized
{
    public interface IScrollContent
    {
        Vector2 MeasureContent(Vector2 availableSize);

        void ArrangeViewport(RectangleF viewport, float scrollOffset);
    }
}
