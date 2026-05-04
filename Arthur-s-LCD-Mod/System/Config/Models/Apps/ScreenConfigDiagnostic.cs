using Graph.System.Config.Interfaces;
using Graph.System.TerminalControls.Generic;
using ProtoBuf;

namespace Graph.System.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigDiagnostic : ScreenConfigColorable, IProjectorReference
    {
        public override int Id => 3;
                
        [ProtoMember(8)] public long ReferenceBlock { get; set; }
        [ProtoMember(16)] public float Rotation { get; set; }
    }
}
