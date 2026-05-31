using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigDigitalPictureFrames : ScreenConfigInteractive
    {
        public override int Id => 19;

        [ProtoMember(30)]
        public string BackgroundSprite { get; set; } = string.Empty;
    }
}
