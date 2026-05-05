using ProtoBuf;
using VRageMath;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    public class PacketEditFaction : NetworkPackage
    {
        [ProtoMember(1)]
        public long FactionId { get; set; }
        [ProtoMember(2)]
        public string Tag { get; set; }
        [ProtoMember(3)]
        public string Name { get; set; }
        [ProtoMember(4)]
        public string Description { get; set; }
        [ProtoMember(5)]
        public string PrivateInfo { get; set; }
        [ProtoMember(6)]
        public string Icon { get; set; }
        [ProtoMember(7)]
        public Vector3 Color { get; set; }
        [ProtoMember(8)]
        public Vector3 IconColor { get; set; }
        public override PackageCode Code => PackageCode.EditFaction;

        // ReSharper disable once UnusedMember.Global
        public PacketEditFaction()// Needed for Protobuf
        {
        }

        public PacketEditFaction(long factionId, string tag, string name, string description, string privateInfo, string icon, Vector3 color, Vector3 iconColor)
        {
            FactionId = factionId;
            Tag = tag;
            Name = name;
            Description = description;
            PrivateInfo = privateInfo;
            Icon = icon;
            Color = color;
            IconColor = iconColor;
        }
    }
}