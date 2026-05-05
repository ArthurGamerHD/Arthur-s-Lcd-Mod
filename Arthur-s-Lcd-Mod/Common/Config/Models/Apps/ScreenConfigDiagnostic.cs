using LcdMod.Common.Config.Interfaces;
using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigDiagnostic : ScreenConfigColorable, IProjectorReference
    {
        public override int Id => 3;
                
        [ProtoMember(8)] public long ReferenceBlock { get; set; }
        [ProtoMember(16)] public float Rotation { get; set; }
    }
}
