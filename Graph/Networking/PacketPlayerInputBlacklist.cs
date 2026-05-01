using ProtoBuf;

namespace Graph.Networking
{
    [ProtoContract]
    public class PacketPlayerInputBlacklist : NetworkPackage
    {
        [ProtoMember(1)] public long PlayerId { get; set; }
        [ProtoMember(2)] public bool Enabled { get; set; }

        public override PackageCode Code => PackageCode.PlayerInputBlacklist;

        public PacketPlayerInputBlacklist()
        {
        }

        public PacketPlayerInputBlacklist(long playerId, bool enabled)
        {
            PlayerId = playerId;
            Enabled = enabled;
        }
    }
}
