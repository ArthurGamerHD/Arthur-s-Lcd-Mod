using Graph.System.Config.Interfaces;
using Graph.System.TerminalControls.Generic;
using ProtoBuf;

namespace Graph.System.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigDocking : ScreenConfigColorable, IDockableBlockReference
    {
        public override int Id => 13;

        [ProtoMember(8)] public long ReferenceBlock { get; set; }
    }
}
