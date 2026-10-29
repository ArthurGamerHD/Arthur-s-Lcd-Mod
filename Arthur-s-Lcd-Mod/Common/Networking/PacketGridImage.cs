using Generated;
using LcdMod.Common.Images;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    [NetworkPayload(20)]
    public partial class PacketUploadGridImage
    {
        [ProtoMember(1)] public long BlockId { get; set; }
        [ProtoMember(2)] public string ImageId { get; set; }
        [ProtoMember(3)] public GridImagePayload Payload { get; set; }

        // ReSharper disable once UnusedMember.Global
        public PacketUploadGridImage()
        {
        }

        public PacketUploadGridImage(long blockId, string id, GridImagePayload payload)
        {
            BlockId = blockId;
            ImageId = id;
            Payload = payload;
        }
    }

    [ProtoContract]
    [NetworkPayload(21)]
    public partial class PacketRequestGridImage
    {
        [ProtoMember(1)] public long GridId { get; set; }
        [ProtoMember(2)] public string ImageId { get; set; }

        // ReSharper disable once UnusedMember.Global
        public PacketRequestGridImage()
        {
        }

        public PacketRequestGridImage(long gridId, string id)
        {
            GridId = gridId;
            ImageId = id;
        }
    }

    [ProtoContract]
    [NetworkPayload(22)]
    public partial class PacketGridImageData
    {
        [ProtoMember(1)] public GridImageManifestEntry Entry { get; set; }
        [ProtoMember(2)] public GridImagePayload Payload { get; set; }
        [ProtoMember(3)] public long GridId { get; set; }

        // ReSharper disable once UnusedMember.Global
        public PacketGridImageData()
        {
        }

        public PacketGridImageData(long gridId, GridImageManifestEntry entry, GridImagePayload payload)
        {
            GridId = gridId;
            Entry = entry;
            Payload = payload;
        }
    }
}
