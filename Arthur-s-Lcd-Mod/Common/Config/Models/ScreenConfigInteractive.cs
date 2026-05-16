using System.Xml.Serialization;
using LcdMod.Common.Config.Models.Apps;
using ProtoBuf;

namespace LcdMod.Common.Config.Models
{
    [ProtoContract]
    [ProtoInclude(113, typeof(ScreenConfigRaycast))]
    [XmlInclude(typeof(ScreenConfigRaycast))]
    public partial class ScreenConfigInteractive : ScreenConfigColorable
    {
        public override int Id => 14;
        
        [ProtoMember(22)] public float CursorScale { get; set; } = 1f;
        [ProtoMember(23)] public bool RequiresAlt { get; set; } = true;
        [ProtoMember(27)] public int ReferenceMode { get; set; } = 0;
    }
}
