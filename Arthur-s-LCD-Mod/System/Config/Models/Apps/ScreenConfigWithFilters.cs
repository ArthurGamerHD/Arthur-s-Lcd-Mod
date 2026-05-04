using ProtoBuf;

namespace Graph.System.Config.Models.Apps
{
    [ProtoContract]
    [ProtoInclude(100, typeof(ScreenConfigWithBlocks))]
    public partial class ScreenConfigWithFilters : ScreenConfigColorable
    {
        [ProtoMember(10)] public int SortInternal { get; set; }
        [ProtoMember(13)] public bool HideEmpty { get; set; } = true;

        public override int Id => 7;

        public SortMethod SortMethod
        {
            get { return (SortMethod)SortInternal; }
            set { SortInternal = (int)value; }
        }
    }
}
