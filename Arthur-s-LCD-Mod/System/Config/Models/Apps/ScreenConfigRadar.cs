using ProtoBuf;

namespace Graph.System.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigRadar : ScreenConfigColorable
    {
        public override int Id => 8;
    }
}
