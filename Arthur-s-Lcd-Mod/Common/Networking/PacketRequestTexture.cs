using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    public class PacketRequestTexture : NetworkPackage
    {
        [ProtoMember(1)] public ulong OwnerSteamId { get; set; }
        [ProtoMember(2)] public string TextureName { get; set; }
        [ProtoMember(3)] public ulong RequesterSteamId { get; set; }

        public override PackageCode Code => PackageCode.RequestTexture;

        // ReSharper disable once UnusedMember.Global
        public PacketRequestTexture()
        {
        }

        public PacketRequestTexture(ulong ownerSteamId, string textureName, ulong requesterSteamId = 0)
        {
            OwnerSteamId = ownerSteamId;
            TextureName = textureName;
            RequesterSteamId = requesterSteamId;
        }
    }
}
