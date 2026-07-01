using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Market;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Market
{
    internal sealed class NpcMarketListPageContext
    {
        public IAppHost Host;
        public IList<NpcMarketRow> Rows;
        public int RowsRevision;
        public NpcMarketListPage Page;
        public NpcMarketMode Mode;
        public NpcMarketSortColumn SortColumn;
        public bool SortDescending;
        public float HeaderHeight;
        public float RowHeight;
        public float TextScale;
        public float LayoutScale;
        public Color MutedColor;
        public Action<NpcMarketSortColumn> SortClicked;
        public Action SearchClicked;
        public Action<NpcMarketRowClickTarget> RowClicked;
    }
}
