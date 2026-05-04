using System.Collections.Generic;
using Graph.Networking;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;

namespace Graph.System
{
    public partial class LcdModSessionComponent
    {
        static readonly List<string> UseInputControlIds = new List<string>
        {
            MyControlsSpace.PRIMARY_TOOL_ACTION.String,
            MyControlsSpace.SECONDARY_TOOL_ACTION.String
        };

        public static void SetLocalPlayerUseInputBlocked(bool blocked)
        {
            var session = MyAPIGateway.Session;
            var player = session != null ? session.Player : null;
            if (player == null)
                return;

            SetPlayerUseInputBlocked(player.IdentityId, blocked);
        }

        static void SetPlayerUseInputBlocked(long playerId, bool blocked)
        {
            bool enabled = !blocked;
            ApplyPlayerUseInputEnabled(playerId, enabled);

            // Multiplayer server-side blacklist propagation is intentionally disabled for local-client testing.

            // if (MyAPIGateway.Multiplayer.MultiplayerActive &&
            //     !MyAPIGateway.Multiplayer.IsServer &&
            //     Config.ConfigManager.NetworkManager != null)
            //     Config.ConfigManager.NetworkManager.TransmitToServer(new PacketPlayerInputBlacklist(playerId, enabled), false);
        }

        static void HandlePlayerInputBlacklist(ReceivedPacketEventArgs args)
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            var packet = args.UnWrap<PacketPlayerInputBlacklist>();
            ApplyPlayerUseInputEnabled(packet.PlayerId, packet.Enabled);
        }

        static void ApplyPlayerUseInputEnabled(long playerId, bool enabled)
        {
            for (int i = 0; i < UseInputControlIds.Count; i++)
                MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(UseInputControlIds[i], playerId, enabled);
        }
    }
}
