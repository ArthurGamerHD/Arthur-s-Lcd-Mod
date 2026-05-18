using System.Xml.Serialization;
using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    [XmlInclude(typeof(ScreenConfigOreScanner))]
    public partial class ScreenConfigRenderProxy : ScreenConfigInteractive
    {
        [ProtoMember(16)] public virtual byte XAxisOffset { get; set; }
        [ProtoMember(17)] public virtual byte YAxisOffset { get; set; }

        public override int Id => 16;
    }
}
