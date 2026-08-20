using Generated;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    [NetworkPayload(6)]
    public partial class PacketRequestTexture
    {
        [ProtoMember(1)] public ulong OwnerSteamId { get; set; }
        [ProtoMember(2)] public string TextureName { get; set; }
        [ProtoMember(3)] public ulong RequesterSteamId { get; set; }

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
