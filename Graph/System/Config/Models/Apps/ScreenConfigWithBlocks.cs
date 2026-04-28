using System;
using System.Linq;
using Graph.Helpers;
using ProtoBuf;
using VRage.Game;

namespace Graph.System.Config.Models.Apps
{
    [ProtoContract]
    [ProtoInclude(104, typeof(ScreenConfigWithItems))]
    public partial class ScreenConfigWithBlocks : ScreenConfigWithFilters
    {
        public override int Id => 1;
        
        [ProtoMember(3)] public long[] SelectedBlocks { get; set; } = Array.Empty<long>();
        [ProtoMember(4)] public string[] SelectedGroups { get; set; } = Array.Empty<string>();
    }
}
