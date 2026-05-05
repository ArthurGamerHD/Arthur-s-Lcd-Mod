using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigPower : ScreenConfigInteractive
    {
        [ProtoMember(13)] public bool HideEmpty { get; set; } = true;
        [ProtoMember(18)] public int GraphWindowIndex { get; set; } = 2;

        public override int Id => 10;
    }
}
