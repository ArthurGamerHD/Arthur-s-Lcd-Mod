using System.Xml.Serialization;
using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    [ProtoInclude(106, typeof(ScreenConfigOreScanner))]
    [XmlInclude(typeof(ScreenConfigOreScanner))]
    public partial class ScreenConfigWithReferenceBlock : ScreenConfigColorable
    {
        [ProtoMember(8)] public virtual long ReferenceBlock { get; set; }

        public override int Id => 6;
    }
}
