using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigNpcMarket : ScreenConfigInteractive
    {
        public const float DEFAULT_PAGE_SWITCH_SECONDS = 5f;

        public override int Id => 21;

        [ProtoMember(39)] public int SelectedMode { get; set; }
        [ProtoMember(40)] public float ScrollOffsetPixels { get; set; }
        [ProtoMember(41)] public int BuySortColumn { get; set; } = 1;
        [ProtoMember(42)] public bool BuySortDescending { get; set; }
        [ProtoMember(43)] public int SellSortColumn { get; set; } = 1;
        [ProtoMember(44)] public bool SellSortDescending { get; set; } = true;
        [ProtoMember(45)] public int BothSortColumn { get; set; }
        [ProtoMember(46)] public bool BothSortDescending { get; set; }
        [ProtoMember(47)] public float HorizontalScrollOffsetPixels { get; set; }
        [ProtoMember(48)] public float VerticalScrollOffsetPixels { get; set; }
        [ProtoMember(49)] public float MaxDistanceMeters { get; set; } = 10000001f;
        [ProtoMember(50)] public float PageSwitchSeconds { get; set; } = DEFAULT_PAGE_SWITCH_SECONDS;
        [ProtoMember(51)] public string SearchQuery { get; set; } = string.Empty;
    }
}
