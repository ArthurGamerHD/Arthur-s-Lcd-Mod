using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigStarMap : ScreenConfigGeneral
    {
        [ProtoMember(19)] public float FoV { get; set; } = 70;

        public override int Id => 11;
    }
}
