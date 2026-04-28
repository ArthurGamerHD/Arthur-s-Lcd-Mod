using ProtoBuf;

namespace Graph.System.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigOreScanner : ScreenConfigWithReferenceBlock
    {
        public override int Id => 9;
    }
}
