using LcdMod.Common.Config.Interfaces;
using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    [ProtoInclude(100, typeof(ScreenConfigWithBlocks))]
    public partial class ScreenConfigWithFilters : ScreenConfigInteractive, IHideEmpty
    {
        [ProtoMember(10)] public int SortMethod { get; set; }
        [ProtoMember(13)] public bool HideEmpty { get; set; } = true;

        public override int Id => 7;
    }
}
