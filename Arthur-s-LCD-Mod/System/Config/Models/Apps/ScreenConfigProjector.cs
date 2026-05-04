using Graph.System.Config.Interfaces;
using Graph.System.TerminalControls.Generic;
using ProtoBuf;

namespace Graph.System.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigProjector : ScreenConfigWithItems, IProjectorReference
    {
        public override int Id => 2;

        [ProtoMember(8)] public long ReferenceBlock { get; set; }
    }
}
