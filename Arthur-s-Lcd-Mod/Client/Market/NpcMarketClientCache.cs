using System;
using System.Collections.Generic;
#if DEBUG
using LcdMod.Common.Helpers;
#endif
using LcdMod.Common.Market;
using LcdMod.Common.Networking;
using Sandbox.ModAPI;

namespace LcdMod.Client.Market
{
    internal static class NpcMarketClientCache
    {
        static readonly long RetryDelayTicks = WorldTime.FromSeconds(5);
        static uint _nextRequestId;
        static readonly Dictionary<NpcMarketClientCacheKey, NpcMarketClientEntry> Entries =
            new Dictionary<NpcMarketClientCacheKey, NpcMarketClientEntry>();

        public static event Action<NpcMarketClientCacheKey, bool> Updated;

        public static void Reset()
        {
            Entries.Clear();
            Updated = null;
            _nextRequestId = 0;
        }

        public static void Update(NpcMarketClientCacheKey key)
        {
            var entry = GetOrCreate(key);
            if (entry.AccessDenied)
                return;

            var now = WorldTime.NowElapsedTicks();
            if (entry.Waiting && now >= entry.NextRequestAtTicks)
                SendRequest(key, entry.ActiveNoCache, entry.ActiveRequestId);

            if (!entry.Waiting && entry.Snapshot != null && entry.Snapshot.NextEconomyTickWorldElapsedTicks > 0 &&
                now >= entry.Snapshot.NextEconomyTickWorldElapsedTicks)
            {
                RequestRefresh(key, true);
            }
        }

        public static void EnsureRequested(NpcMarketClientCacheKey key)
        {
            var entry = GetOrCreate(key);
            if (!entry.AccessDenied && entry.Snapshot == null && !entry.Waiting)
                RequestRefresh(key, false);
        }

        public static PacketSyncNpcMarket GetSnapshot(NpcMarketClientCacheKey key)
        {
            NpcMarketClientEntry entry;
            return Entries.TryGetValue(key, out entry) ? entry.Snapshot : null;
        }

        public static bool CanForceRefresh(NpcMarketClientCacheKey key)
        {
            var entry = GetOrCreate(key);
            if (entry.AccessDenied)
                return false;

            var snapshot = entry.Snapshot;
            return snapshot == null || WorldTime.NowElapsedTicks() >= snapshot.NextNoCacheAllowedAtWorldElapsedTicks;
        }

        public static void RequestRefresh(NpcMarketClientCacheKey key, bool noCache)
        {
            if (GetOrCreate(key).AccessDenied)
                return;

#if DEBUG
            LogHelper.LogInfo("NPC market client refresh requested: host=" + key.HostBlockEntityId +
                              ", surface=" + key.HostSurfaceIndex + ", noCache=" + noCache);
#endif
            SendRequest(key, noCache, ++_nextRequestId);
        }

        public static void MarkAccessDenied(NpcMarketClientCacheKey key)
        {
            var entry = GetOrCreate(key);
            if (entry.AccessDenied)
                return;

            entry.AccessDenied = true;
            entry.Waiting = false;
            entry.ActiveRequestId = 0;
            entry.Snapshot = CreateAccessDeniedSnapshot(key);
#if DEBUG
            LogHelper.LogInfo("NPC market client marked access denied locally: host=" +
                              key.HostBlockEntityId + ", surface=" + key.HostSurfaceIndex);
#endif
            var updated = Updated;
            if (updated != null)
                updated(key, true);
        }

        public static void HandleSync(PacketSyncNpcMarket packet)
        {
            if (packet == null)
            {
#if DEBUG
                LogHelper.LogInfo("NPC market client received null sync packet.");
#endif
                return;
            }

            if (packet.Scope == null)
            {
#if DEBUG
                LogHelper.LogInfo("NPC market client received sync without scope: request=" + packet.RequestId +
                                  ", version=" + packet.Version);
#endif
                return;
            }

            var key = new NpcMarketClientCacheKey(packet.Scope.HostBlockEntityId, packet.Scope.HostSurfaceIndex);
            var entry = GetOrCreate(key);
            if (entry.Waiting && packet.RequestId != entry.ActiveRequestId)
            {
#if DEBUG
                LogHelper.LogInfo("NPC market client ignored stale sync: receivedRequest=" + packet.RequestId +
                                  ", activeRequest=" + entry.ActiveRequestId + ", host=" + key.HostBlockEntityId +
                                  ", surface=" + key.HostSurfaceIndex + ", scope=" + packet.Scope.Mode);
#endif
                return;
            }

            var previousSnapshot = entry.Snapshot;
            var rowDataChanged = RequiresRowRebuild(previousSnapshot, packet);

            entry.Snapshot = packet;
            entry.Waiting = false;
            entry.ActiveRequestId = packet.RequestId;
            entry.AccessDenied = packet.Scope.Mode == NpcMarketScopeMode.AccessDenied;
#if DEBUG
            LogHelper.LogInfo("NPC market client accepted sync: request=" + packet.RequestId +
                              ", host=" + key.HostBlockEntityId + ", surface=" + key.HostSurfaceIndex +
                              ", scope=" + packet.Scope.Mode + ", sellers=" +
                              (packet.Sellers?.Count ?? 0) + ", cache=" +
                              packet.WasServedFromCache + ", version=" + packet.Version);
#endif
            var updated = Updated;
            if (updated != null)
                updated(key, rowDataChanged);
        }

        static bool RequiresRowRebuild(PacketSyncNpcMarket previous, PacketSyncNpcMarket current)
        {
            if (previous == null || current == null)
                return true;

            if (previous.Version != current.Version)
                return true;

            var previousScope = previous.Scope;
            var currentScope = current.Scope;
            if (previousScope == null || currentScope == null)
                return previousScope != currentScope;

            return previousScope.Mode != currentScope.Mode ||
                   previousScope.HostOwnerIdentityId != currentScope.HostOwnerIdentityId ||
                   previousScope.HostFactionId != currentScope.HostFactionId ||
                   previousScope.ContributingMemberCount != currentScope.ContributingMemberCount ||
                   previousScope.KnownStationCount != currentScope.KnownStationCount;
        }

        static void SendRequest(NpcMarketClientCacheKey key, bool noCache, uint requestId)
        {
            if (GetOrCreate(key).AccessDenied)
                return;

            if (requestId == 0)
                requestId = ++_nextRequestId;

            var entry = GetOrCreate(key);
            entry.ActiveRequestId = requestId;
            entry.ActiveNoCache = noCache;
            entry.Waiting = true;
            entry.NextRequestAtTicks = WorldTime.NowElapsedTicks() + RetryDelayTicks;
#if DEBUG
            LogHelper.LogInfo("NPC market client sending request: request=" + requestId +
                              ", host=" + key.HostBlockEntityId + ", surface=" + key.HostSurfaceIndex +
                              ", noCache=" + noCache + ", localServer=" +
                              (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer));
#endif
            var packet = new PacketRequestNpcMarket
            {
                RequestId = requestId,
                NoCache = noCache,
                HostBlockEntityId = key.HostBlockEntityId,
                HostSurfaceIndex = key.HostSurfaceIndex
            };

            if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer &&
                LcdModSessionComponent.Server != null)
            {
                LcdModSessionComponent.Server.HandleLocalRequestNpcMarket(packet);
                return;
            }

            LcdModSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        static PacketSyncNpcMarket CreateAccessDeniedSnapshot(NpcMarketClientCacheKey key)
        {
            var now = WorldTime.NowElapsedTicks();
            return new PacketSyncNpcMarket
            {
                CapturedWorldElapsedTicks = now,
                CacheBuiltAtWorldElapsedTicks = now,
                NextEconomyTickWorldElapsedTicks = now,
                NextNoCacheAllowedAtWorldElapsedTicks = long.MaxValue,
                Scope = new NpcMarketScopeDto
                {
                    Mode = NpcMarketScopeMode.AccessDenied,
                    HostBlockEntityId = key.HostBlockEntityId,
                    HostSurfaceIndex = key.HostSurfaceIndex
                },
                Sellers = new List<NpcMarketSellerFactionDto>()
            };
        }

        static NpcMarketClientEntry GetOrCreate(NpcMarketClientCacheKey key)
        {
            NpcMarketClientEntry entry;
            if (!Entries.TryGetValue(key, out entry))
            {
                entry = new NpcMarketClientEntry();
                Entries[key] = entry;
            }

            return entry;
        }
    }

    internal struct NpcMarketClientCacheKey : IEquatable<NpcMarketClientCacheKey>
    {
        public readonly long HostBlockEntityId;
        public readonly int HostSurfaceIndex;

        public NpcMarketClientCacheKey(long hostBlockEntityId, int hostSurfaceIndex)
        {
            HostBlockEntityId = hostBlockEntityId;
            HostSurfaceIndex = hostSurfaceIndex;
        }

        public bool Equals(NpcMarketClientCacheKey other)
        {
            return HostBlockEntityId == other.HostBlockEntityId && HostSurfaceIndex == other.HostSurfaceIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is NpcMarketClientCacheKey && Equals((NpcMarketClientCacheKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (HostBlockEntityId.GetHashCode() * 397) ^ HostSurfaceIndex;
            }
        }
    }

    internal sealed class NpcMarketClientEntry
    {
        public PacketSyncNpcMarket Snapshot;
        public uint ActiveRequestId;
        public long NextRequestAtTicks;
        public bool Waiting;
        public bool ActiveNoCache;
        public bool AccessDenied;
    }
}
