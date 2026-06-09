using System.Xml.Serialization;
using LcdMod.Common.Config.Models.Apps;
using ProtoBuf;

namespace LcdMod.Common.Config.Models
{
    [ProtoContract]
    [ProtoInclude(113, typeof(ScreenConfigRaycast))]
    [XmlInclude(typeof(ScreenConfigRaycast))]
    [ProtoInclude(114, typeof(ScreenConfigRenderProxy))]
    [XmlInclude(typeof(ScreenConfigRenderProxy))]
    [ProtoInclude(116, typeof(ScreenConfigButtonPanel))]
    [XmlInclude(typeof(ScreenConfigButtonPanel))]
    [ProtoInclude(117, typeof(ScreenConfigDigitalPictureFrames))]
    [XmlInclude(typeof(ScreenConfigDigitalPictureFrames))]
    [ProtoInclude(118, typeof(ScreenConfigCargoActions))]
    [XmlInclude(typeof(ScreenConfigCargoActions))]
    [ProtoInclude(119, typeof(ScreenConfigNpcMarket))]
    [XmlInclude(typeof(ScreenConfigNpcMarket))]
    public partial class ScreenConfigInteractive : ScreenConfigColorable
    {
        public override int Id => 14;
        
        [ProtoMember(22)] public float CursorScale { get; set; } = 1f;
        [ProtoMember(23)] public bool RequiresAlt { get; set; } = true;
        [ProtoMember(27)] public int ReferenceMode { get; set; } = 0;
    }
}
