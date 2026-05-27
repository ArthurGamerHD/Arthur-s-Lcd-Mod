using System.Xml.Serialization;
using LcdMod.Common.Config.Interfaces;
using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    [XmlInclude(typeof(ScreenConfigOreScanner))]
    public partial class ScreenConfigRenderProxy : ScreenConfigInteractive, IConfigWithReferenceBlock
    {
        [ProtoMember(8)] public long ReferenceBlock { get; set; }
        [ProtoMember(16)] public virtual sbyte XAxisOffset { get; set; }
        [ProtoMember(17)] public virtual sbyte YAxisOffset { get; set; }
        [ProtoMember(18)] public bool EnableAutoAdjust { get; set; } = true;

        public override int Id => 16;
    }
}
