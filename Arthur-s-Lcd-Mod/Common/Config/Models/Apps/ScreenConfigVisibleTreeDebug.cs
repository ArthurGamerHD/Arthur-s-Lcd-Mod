#if DEBUG
using LcdMod.Common.Config.Interfaces;
using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigVisibleTreeDebug : ScreenConfigInteractive, IConfigWithReferenceBlock
    {
        public override int Id => 22;

        [ProtoMember(8)] public long ReferenceBlock { get; set; }
        [ProtoMember(28)] public int ReferenceScreenIndex { get; set; }
    }
}
#endif
