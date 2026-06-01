using ProtoBuf;
using System;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigDigitalPictureFrames : ScreenConfigInteractive
    {
        public override int Id => 19;

        [ProtoMember(30)]
        public string BackgroundSprite { get; set; } = string.Empty;

        [ProtoMember(31)]
        public string[] SelectedSprites { get; set; } = Array.Empty<string>();

        [ProtoMember(32)]
        public float ImageChangeInterval { get; set; }
    }
}
