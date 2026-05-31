using ProtoBuf;
using LcdMod.Common.Helpers;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    public class PacketSyncTexture : NetworkPackage
    {
        [ProtoMember(1)] public ulong OwnerSteamId { get; set; }
        [ProtoMember(2)] public string TextureName { get; set; }
        [ProtoMember(3)] public ulong RequesterSteamId { get; set; }
        [ProtoMember(4)] public byte[] Data { get; set; }
        [ProtoMember(5)] public TextureTransferHelper.TextureMetadata Metadata { get; set; }

        public override PackageCode Code => PackageCode.SyncTexture;

        // ReSharper disable once UnusedMember.Global
        public PacketSyncTexture()
        {
            Data = new byte[0];
            Metadata = new TextureTransferHelper.TextureMetadata();
        }

        public PacketSyncTexture(ulong ownerSteamId, string textureName, ulong requesterSteamId, byte[] data, TextureTransferHelper.TextureMetadata metadata = null)
        {
            OwnerSteamId = ownerSteamId;
            TextureName = textureName;
            RequesterSteamId = requesterSteamId;
            Data = data ?? new byte[0];
            Metadata = metadata ?? new TextureTransferHelper.TextureMetadata
            {
                OwnerSteamId = ownerSteamId,
                TextureName = textureName
            };
        }
    }
}
