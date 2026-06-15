using LcdMod.Client.Market;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Market
{
    internal sealed class NpcMarketRowHitSlot
    {
        public NpcMarketRow Row;
        public NpcMarketMode Mode;
        public int PageIndex;
        public int RowIndex;
        public int VisibleIndex;
        public RectangleF Bounds;
        public NpcMarketRowClickTarget Target;
    }
}
