using Generated;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    [NetworkPayload(3)]
    public partial class PacketPlayerInputBlacklist
    {
        [ProtoMember(1)] public long PlayerId { get; set; }
        [ProtoMember(2)] public bool Enabled { get; set; }

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
