using System.Collections.Generic;
using ProtoBuf;

namespace LcdMod.Common.Images
{
    public enum GridImageEncoding
    {
        DeflatedDds = 1,
        Png = 2
    }

    [ProtoContract]
    public sealed class GridImagePayload
    {
        [ProtoMember(1)] public GridImageEncoding Encoding { get; set; }
        [ProtoMember(2)] public byte[] Data { get; set; }
        [ProtoMember(3)] public int DdsLength { get; set; }
        [ProtoMember(4)] public int Profile { get; set; }
    }

    [ProtoContract]
    public sealed class GridImageManifestEntry
    {
        [ProtoMember(1)] public string Id { get; set; }
        [ProtoMember(2)] public ulong OwnerSteamId { get; set; }
        [ProtoMember(3)] public int Width { get; set; }
        [ProtoMember(4)] public int Height { get; set; }
        [ProtoMember(5)] public int PayloadBytes { get; set; }

        public GridImageManifestEntry Clone()
        {
            return new GridImageManifestEntry
            {
                Id = Id,
                OwnerSteamId = OwnerSteamId,
                Width = Width,
                Height = Height,
                PayloadBytes = PayloadBytes
            };
        }
    }

    [ProtoContract]
    public sealed class GridImageManifest
    {
        [ProtoMember(1)] public List<GridImageManifestEntry> Entries { get; set; } = new List<GridImageManifestEntry>();
    }

    [ProtoContract]
    public sealed class GridImagePayloadRecord
    {
        [ProtoMember(1)] public string Id { get; set; }
        [ProtoMember(2)] public GridImagePayload Payload { get; set; }
    }

    [ProtoContract]
    public sealed class GridImagePayloadTable
    {
        [ProtoMember(1)] public List<GridImagePayloadRecord> Records { get; set; } = new List<GridImagePayloadRecord>();
    }

    [ProtoContract]
    public sealed class EncodedGridImage
    {
        [ProtoMember(1)] public string Id { get; set; }
        [ProtoMember(2)] public int Width { get; set; }
        [ProtoMember(3)] public int Height { get; set; }
        [ProtoMember(4)] public GridImagePayload Payload { get; set; }
    }
}
