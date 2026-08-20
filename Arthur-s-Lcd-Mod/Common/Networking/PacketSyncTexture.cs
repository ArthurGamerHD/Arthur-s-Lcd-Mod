using System;
using Generated;
using ProtoBuf;
using LcdMod.Common.Helpers;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    [NetworkPayload(7)]
    public partial class PacketSyncTexture
    {
        [ProtoMember(1)] public ulong OwnerSteamId { get; set; }
        [ProtoMember(2)] public string TextureName { get; set; }
        [ProtoMember(3)] public ulong RequesterSteamId { get; set; }
        [ProtoMember(4)] public byte[] Data { get; set; }
        [ProtoMember(5)] public TextureTransferHelper.TextureMetadata Metadata { get; set; }

        // ReSharper disable once UnusedMember.Global
        public PacketSyncTexture()
        {
            Data = Array.Empty<byte>();
            Metadata = new TextureTransferHelper.TextureMetadata();
        }

        public PacketSyncTexture(ulong ownerSteamId, string textureName, ulong requesterSteamId, byte[] data, TextureTransferHelper.TextureMetadata metadata = null)
        {
            OwnerSteamId = ownerSteamId;
            TextureName = textureName;
            RequesterSteamId = requesterSteamId;
            Data = data ?? Array.Empty<byte>();
            Metadata = metadata ?? new TextureTransferHelper.TextureMetadata
            {
                OwnerSteamId = ownerSteamId,
                TextureName = textureName
            };
        }
    }
}
