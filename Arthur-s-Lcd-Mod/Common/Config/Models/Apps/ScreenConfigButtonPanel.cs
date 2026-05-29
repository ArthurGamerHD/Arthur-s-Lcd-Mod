using LcdMod.Common.Config.Interfaces;
using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigButtonPanel : ScreenConfigInteractive, IHideEmpty
    {
        public override int Id => 18;
        [ProtoMember(1001)]
        public bool HideEmpty { get; set; }
    }
}
