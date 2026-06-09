using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized
{
    public interface IScrollContent2D
    {
        Vector2 MeasureContent(Vector2 availableSize);

        void ArrangeViewport(RectangleF viewport, Vector2 scrollOffsetPixels);
    }
}
