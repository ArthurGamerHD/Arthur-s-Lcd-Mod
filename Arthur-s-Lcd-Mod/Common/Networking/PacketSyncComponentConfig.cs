using LcdMod.Common.Config.Models;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    internal class NetworkPackageSyncComponentConfig : NetworkPackage
    {
        public override PackageCode Code => PackageCode.SyncConfig;
        [ProtoMember(1)] public long BlockId { get; set; }
        [ProtoMember(2)] public ScreenProviderConfig Config { get; set; }

        // ReSharper disable once UnusedMember.Global
        public NetworkPackageSyncComponentConfig()// Needed for Protobuf
        {
        }

        public NetworkPackageSyncComponentConfig(long senderId, ScreenProviderConfig config)
        {
            BlockId = senderId;
            Config = config;
        }
    }
}