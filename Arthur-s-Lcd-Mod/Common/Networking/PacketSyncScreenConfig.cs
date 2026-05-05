using LcdMod.Common.Config.Models;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    internal class NetworkPackageSyncScreenConfig : NetworkPackage
    {
        public override PackageCode Code => PackageCode.SyncConfig;
        [ProtoMember(1)] public long BlockId { get; set; }
        [ProtoMember(2)] public ScreenProviderConfig Config { get; set; }

        // ReSharper disable once UnusedMember.Global
        public NetworkPackageSyncScreenConfig()// Needed for Protobuf
        {
        }

        public NetworkPackageSyncScreenConfig(long senderId, ScreenProviderConfig config)
        {
            BlockId = senderId;
            Config = config;
        }
    }
}