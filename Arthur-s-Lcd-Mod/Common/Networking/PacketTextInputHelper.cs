using ProtoBuf;

namespace LcdMod.Common.Networking
{
    public enum GhostLcdAction
    {
        Spawn  = 0,
        Remove = 1,
    }

    [ProtoContract]
    public class PacketTextInputHelper : NetworkPackage
    {
        [ProtoMember(1)] public long PlayerId     { get; set; }
        [ProtoMember(2)] public GhostLcdAction Action { get; set; }
        [ProtoMember(3)] public int  LifetimeTicks { get; set; }
        [ProtoMember(4)] public long GridId { get; set; }

        public override PackageCode Code => PackageCode.TextInputHelper;


        public PacketTextInputHelper() { }

        public PacketTextInputHelper(long playerId, GhostLcdAction action, int lifetimeTicks)
        {
            PlayerId      = playerId;
            Action        = action;
            LifetimeTicks = lifetimeTicks;
        }
    }
}
