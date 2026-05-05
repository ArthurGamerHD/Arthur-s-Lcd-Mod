using ProtoBuf;

namespace LcdMod.Common.Config.Models
{
    [ProtoContract]
    public partial class ScreenConfigInteractive : ScreenConfigColorable
    {
        public override int Id => 14;
        
        [ProtoMember(22)] public float CursorScale { get; set; } = 1f;
        [ProtoMember(23)] public bool RequiresAlt { get; set; } = true;
    }
}
