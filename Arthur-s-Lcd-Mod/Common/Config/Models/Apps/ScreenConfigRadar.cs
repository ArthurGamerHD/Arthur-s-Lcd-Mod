using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigRadar : ScreenConfigInteractive
    {
        [ProtoMember(19)] public float RangeScale { get; set; } = 1f;

        public override int Id => 8;
    }
}
