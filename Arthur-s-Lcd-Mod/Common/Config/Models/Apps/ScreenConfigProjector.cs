using LcdMod.Common.Config.Interfaces;
using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigProjector : ScreenConfigWithItems, IProjectorReference
    {
        public override int Id => 2;

        [ProtoMember(8)] public long ReferenceBlock { get; set; }
    }
}
