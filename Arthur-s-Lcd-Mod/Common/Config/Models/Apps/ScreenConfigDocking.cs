using LcdMod.Common.Config.Interfaces;
using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigDocking : ScreenConfigColorable, IDockableBlockReference
    {
        public override int Id => 13;

        [ProtoMember(8)] public long ReferenceBlock { get; set; }
    }
}
