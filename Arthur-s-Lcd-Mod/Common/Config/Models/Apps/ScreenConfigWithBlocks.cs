using System;
using System.Xml.Serialization;
using LcdMod.Common.Config.Interfaces;
using ProtoBuf;
using GridLinkTypeEnum = VRage.Game.ModAPI.GridLinkTypeEnum;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    [ProtoInclude(104, typeof(ScreenConfigWithItems))]
    public partial class ScreenConfigWithBlocks : ScreenConfigWithFilters, IGridGroupReference
    {
        public override int Id => 1;
        
        [ProtoMember(3)] public long[] SelectedBlocks { get; set; } = Array.Empty<long>();
        [ProtoMember(4)] public string[] SelectedGroups { get; set; } = Array.Empty<string>();

        [XmlIgnore]
        public GridLinkTypeEnum GridLinkType => (GridLinkTypeEnum)GridLinkTypeInternal;

        [ProtoMember(20)] public int GridLinkTypeInternal { get; set; } = (int)GridLinkTypeEnum.Mechanical;
    }
}
