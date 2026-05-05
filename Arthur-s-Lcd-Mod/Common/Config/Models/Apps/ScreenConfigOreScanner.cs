using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigOreScanner : ScreenConfigWithReferenceBlock
    {
        public override int Id => 9;
    }
}
