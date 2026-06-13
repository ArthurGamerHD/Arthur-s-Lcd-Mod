using System.Xml.Serialization;
using LcdMod.Common.Config.Interfaces;
using ProtoBuf;
using VRage.Game.ModAPI;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigPower : ScreenConfigGeneral, IGridGroupReference, IHideEmpty
    {
        [ProtoMember(13)] public bool HideEmpty { get; set; } = true;
        [ProtoMember(18)] public int GraphWindowIndex { get; set; } = 2;
        [ProtoMember(21)] public int PowerHistoryTier { get; set; } = -1;
        
        [XmlIgnore]
        public GridLinkTypeEnum GridLinkType => (GridLinkTypeEnum)GridLinkTypeInternal;

        [ProtoMember(20)] public int GridLinkTypeInternal { get; set; } = (int)GridLinkTypeEnum.Mechanical;

        public override int Id => 10;
    }
}
