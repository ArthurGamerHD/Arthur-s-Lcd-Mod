using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using LcdMod.Common.Market;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Components;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace LcdMod.Server
{
    internal sealed class NpcMarketService
    {
        const string KNOWN_STATIONS_FILE_NAME = "economy.xml";
        static readonly long ForceRefreshMinimumAgeTicks = WorldTime.FromSeconds(30);
        static readonly Type WorldStorageScopeType = typeof(NpcMarketService);

        readonly Dictionary<PendingRequestKey, PendingNpcMarketRequest> _pending =
            new Dictionary<PendingRequestKey, PendingNpcMarketRequest>();
        
        // no need to send more than one personalized request per player
        readonly Dictionary<MarketScopeCacheKey, ScopedNpcMarketReply> _scopedReplies =
            new Dictionary<MarketScopeCacheKey, ScopedNpcMarketReply>(); 
        
        readonly Dictionary<long, HashSet<long>> _knownStationIdsByPlayerId =
            new Dictionary<long, HashSet<long>>();
        
        readonly Dictionary<long, string> _playerNameHintsByIdentityId = new Dictionary<long, string>();

        ParsedNpcMarketCheckpoint _cache;
        bool _refreshInProgress;
        bool _knownStationLedgerDirty;
        int _saveGeneration;
        int _nextVersion;

        public NpcMarketService(LcdModSessionComponent session)
        {
        }

        public void LoadData()
        {
            if (MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer)
                return;

            _knownStationIdsByPlayerId.Clear();
            _playerNameHintsByIdentityId.Clear();
            _knownStationLedgerDirty = false;

            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
                return;

            // I really don't trust SE's own Economy system, so I store my own
            if (utilities.FileExistsInWorldStorage(KNOWN_STATIONS_FILE_NAME, WorldStorageScopeType))
            {
                try
                {
                    string xml;
                    using (var reader =
                           utilities.ReadFileInWorldStorage(KNOWN_STATIONS_FILE_NAME, WorldStorageScopeType))
                    {
                        xml = reader.ReadToEnd();
                    }

                    if (string.IsNullOrWhiteSpace(xml))
                    {
                        LogHelper.Log(MyLogSeverity.Warning, "NPC market knowledge ledger was empty.");
                        return;
                    }

                    var file = utilities.SerializeFromXML<EconomyKnownStationsFile>(xml);
                    if (file != null && file.Version != 1)
                    {
                        LogHelper.Log(MyLogSeverity.Warning,
                            "Unsupported NPC market knowledge ledger version: " + file.Version);
                        return;
                    }

                    var added = NpcMarketKnownStationsStorage.MergeStorageModel(
                        file,
                        _knownStationIdsByPlayerId,
                        _playerNameHintsByIdentityId);
#if DEBUG
                    LogHelper.LogInfo("NPC market knowledge ledger loaded: players=" +
                                      _knownStationIdsByPlayerId.Count + ", uniquePlayerStationLinks=" +
                                      CountKnownStations() + ", merged=" + added);
#endif
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, this);
                    LogHelper.Log(MyLogSeverity.Warning,
                        "NPC market knowledge ledger could not be loaded; continuing with an empty ledger.");
                    _knownStationIdsByPlayerId.Clear();
                    _playerNameHintsByIdentityId.Clear();
                    _knownStationLedgerDirty = false;
                }
            }
            else
            {
#if DEBUG
                LogHelper.LogInfo("NPC market knowledge ledger not found; starting empty.");
#endif
            }
        }

        public void HandleRequest(ulong senderSteamId, PacketRequestNpcMarket request)
        {
            if (request == null)
            {
#if DEBUG
                LogHelper.LogInfo("NPC market server received null request from steam=" + senderSteamId);
#endif
                return;
            }

#if DEBUG
            LogHelper.LogInfo("NPC market server received request: steam=" + senderSteamId +
                              ", request=" + request.RequestId + ", host=" + request.HostBlockEntityId +
                              ", surface=" + request.HostSurfaceIndex + ", noCache=" + request.NoCache);
#endif

            NpcMarketScopeMode errorMode;
            PendingNpcMarketRequest resolved;
            if (!TryResolveHostBlock(senderSteamId, request, out resolved, out errorMode))
            {
#if DEBUG
                LogHelper.LogInfo("NPC market server rejected request: steam=" + senderSteamId +
                                  ", request=" + request.RequestId + ", host=" + request.HostBlockEntityId +
                                  ", reason=" + errorMode);
#endif
                SendEmptyScope(senderSteamId, request, errorMode);
                return;
            }

            if (resolved.HostOwnerIdentityId == 0)
            {
#if DEBUG
                LogHelper.LogInfo("NPC market server rejected request: steam=" + senderSteamId +
                                  ", request=" + request.RequestId + ", host=" + request.HostBlockEntityId +
                                  ", reason=" + NpcMarketScopeMode.UnownedHostBlock);
#endif
                SendEmptyScope(senderSteamId, request, NpcMarketScopeMode.UnownedHostBlock);
                return;
            }

            var now = WorldTime.NowElapsedTicks();
            if (CanServeCache(request, now))
            {
#if DEBUG
                LogHelper.LogInfo("NPC market server serving request from cache: steam=" + senderSteamId +
                                  ", request=" + request.RequestId + ", version=" + _cache.Version);
#endif
                SendSnapshot(resolved, _cache, true);
                return;
            }

            var key = new PendingRequestKey(senderSteamId, request.RequestId);
            _pending[key] = resolved;
#if DEBUG
            LogHelper.LogInfo("NPC market server queued request: steam=" + senderSteamId +
                              ", request=" + request.RequestId + ", pending=" + _pending.Count +
                              ", refreshInProgress=" + _refreshInProgress);
#endif

            if (!_refreshInProgress)
                StartRefresh();
        }

        public void SaveData()
        {
            SaveKnownStations();
            _saveGeneration++;
            _cache = null;
            _scopedReplies.Clear();
        }

        public void UnloadData()
        {
            SaveKnownStations();
            _knownStationIdsByPlayerId.Clear();
            _playerNameHintsByIdentityId.Clear();
            _knownStationLedgerDirty = false;
        }

        void SaveKnownStations()
        {
            if (MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer)
                return;

            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
                return;

            if (!_knownStationLedgerDirty &&
                utilities.FileExistsInWorldStorage(KNOWN_STATIONS_FILE_NAME, WorldStorageScopeType))
            {
                return;
            }

            try
            {
                var file = NpcMarketKnownStationsStorage.CreateStorageModel(
                    _knownStationIdsByPlayerId,
                    _playerNameHintsByIdentityId);
                var xml = utilities.SerializeToXML(file);
                using (var writer = utilities.WriteFileInWorldStorage(KNOWN_STATIONS_FILE_NAME, WorldStorageScopeType))
                {
                    writer.Write(xml);
                }

                _knownStationLedgerDirty = false;
#if DEBUG
                LogHelper.LogInfo("NPC market knowledge ledger saved: file=" + KNOWN_STATIONS_FILE_NAME +
                                  ", players=" + file.Players.Count + ", uniquePlayerStationLinks=" +
                                  CountKnownStations());
#endif
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                LogHelper.Log(MyLogSeverity.Warning, "NPC market knowledge ledger could not be saved.");
            }
        }

        void MergeKnownStationsFromCheckpoint(Dictionary<long, HashSet<long>> checkpointKnownStations)
        {
            if (checkpointKnownStations == null) return;

            var totalAdded = 0;
            foreach (var player in checkpointKnownStations)
            {
                if (player.Key == 0 || player.Value == null)
                    continue;

                HashSet<long> durable = null;
                var addedForPlayer = 0;
                foreach (var stationId in player.Value)
                {
                    if (stationId == 0)
                        continue;

                    if (durable == null)
                        durable = GetOrCreateKnownStations(player.Key);

                    if (durable.Add(stationId))
                    {
                        addedForPlayer++;
                        totalAdded++;
                    }
                }

                if (addedForPlayer > 0)
                {
#if DEBUG
                    LogHelper.LogInfo("NPC market knowledge ledger merge: identity=" + player.Key +
                                      ", checkpointStations=" + player.Value.Count + ", newlyAdded=" +
                                      addedForPlayer + ", durableStations=" + durable.Count);
#endif
                }
            }

            if (totalAdded > 0)
            {
                _knownStationLedgerDirty = true;
                _scopedReplies.Clear();
#if DEBUG
                LogHelper.LogInfo("NPC market knowledge ledger merged checkpoint discoveries: " + totalAdded);
#endif
            }
        }

        void MergePlayerNameHintsFromCheckpoint(Dictionary<long, string> playerNameHints)
        {
            if (playerNameHints == null)
                return;

            foreach (var pair in playerNameHints)
            {
                if (pair.Key == 0 || string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                string existing;
                if (_playerNameHintsByIdentityId.TryGetValue(pair.Key, out existing) &&
                    string.Equals(existing, pair.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                _playerNameHintsByIdentityId[pair.Key] = pair.Value;
                _knownStationLedgerDirty = true;
            }
        }

        HashSet<long> GetOrCreateKnownStations(long playerIdentityId)
        {
            HashSet<long> stations;
            if (!_knownStationIdsByPlayerId.TryGetValue(playerIdentityId, out stations))
            {
                stations = new HashSet<long>();
                _knownStationIdsByPlayerId[playerIdentityId] = stations;
            }

            return stations;
        }

        int CountKnownStations()
        {
            var count = 0;
            foreach (var pair in _knownStationIdsByPlayerId)
            {
                if (pair.Key == 0 || pair.Value == null)
                    continue;

                foreach (var stationId in pair.Value)
                    if (stationId != 0)
                        count++;
            }

            return count;
        }

        bool CanServeCache(PacketRequestNpcMarket request, long now)
        {
            if (_cache == null)
                return false;

            if (IsPastExpectedEconomyTick(_cache, now))
                return false;

            if (!request.NoCache)
                return true;

            return now - _cache.BuiltAtWorldElapsedTicks < ForceRefreshMinimumAgeTicks;
        }

        static bool IsPastExpectedEconomyTick(ParsedNpcMarketCheckpoint cache, long now)
        {
            return cache.NextEconomyTickWorldElapsedTicks > 0 &&
                   now >= cache.NextEconomyTickWorldElapsedTicks;
        }

        bool TryResolveHostBlock(
            ulong senderSteamId,
            PacketRequestNpcMarket request,
            out PendingNpcMarketRequest resolved,
            out NpcMarketScopeMode errorMode)
        {
            resolved = null;
            errorMode = NpcMarketScopeMode.None;

            var entity = MyEntities.GetEntityById(request.HostBlockEntityId);
            var terminalBlock = entity as IMyTerminalBlock;
            var surfaceProvider = entity as IMyTextSurfaceProvider;
            if (terminalBlock == null || surfaceProvider == null || terminalBlock.Closed)
            {
                errorMode = NpcMarketScopeMode.InvalidHostBlock;
                return false;
            }

            var requestingIdentityId = MyAPIGateway.Players.TryGetIdentityId(senderSteamId);
            if (!terminalBlock.HasPlayerAccess(requestingIdentityId))
            {
                errorMode = NpcMarketScopeMode.AccessDenied;
                return false;
            }

            resolved = new PendingNpcMarketRequest
            {
                SenderSteamId = senderSteamId,
                RequestId = request.RequestId,
                NoCache = request.NoCache,
                HostBlockEntityId = request.HostBlockEntityId,
                HostSurfaceIndex = request.HostSurfaceIndex,
                HostOwnerIdentityId = terminalBlock.OwnerId,
                RequestingIdentityId = requestingIdentityId
            };
            return true;
        }

        void StartRefresh()
        {
            if (_refreshInProgress)
                return;

            _refreshInProgress = true;
#if DEBUG
            LogHelper.LogInfo("NPC market server starting refresh: pending=" + _pending.Count +
                              ", saveGeneration=" + _saveGeneration);
#endif

            var checkpoint = MyAPIGateway.Session.GetCheckpoint(MyAPIGateway.Session.Name);
            var work = new MarketRefreshWork
            {
                SaveGenerationAtCapture = _saveGeneration,
                Checkpoint = checkpoint
            };

            MyAPIGateway.Parallel.Start(
                delegate { ProcessCheckpoint(work); },
                delegate { CompleteRefresh(work); });
        }

        static void ProcessCheckpoint(MarketRefreshWork work)
        {
            try
            {
                var checkpoint = work.Checkpoint;
                var economy = FindEconomyComponent(checkpoint.SessionComponents);
                if (economy == null)
                    throw new InvalidOperationException("Economy component missing from checkpoint.");

                var capturedTicks = checkpoint.ElapsedGameTime;
                var nextTick = capturedTicks + WorldTime.FromMilliseconds(Math.Max(0.0, economy.LastEconomyTick));

                var result = new ParsedNpcMarketCheckpoint
                {
                    CapturedWorldElapsedTicks = capturedTicks,
                    NextEconomyTickWorldElapsedTicks = nextTick,
                    EconomyTickSeconds = checkpoint.Settings != null ? checkpoint.Settings.EconomyTickInSeconds : 0,
                    NpcSellerFactionsById = new Dictionary<long, ParsedNpcSellerFaction>(),
                    StationsById = new Dictionary<long, ParsedNpcStation>(),
                    PlayerFactionIdByIdentityId = new Dictionary<long, long>(),
                    MemberIdentityIdsByFactionId = new Dictionary<long, HashSet<long>>(),
                    VisitedStationIdsByIdentityId = new Dictionary<long, HashSet<long>>(),
                    PlayerNameHintsByIdentityId = new Dictionary<long, string>()
                };

                ParseFactionMembership(checkpoint.Factions, result);
                ParseVisitedStations(checkpoint, result);
                ParseNpcSellersAndStations(checkpoint.Factions, result);
                work.Result = result;
            }
            catch (Exception e)
            {
                work.Error = e;
            }
        }

        void CompleteRefresh(MarketRefreshWork work)
        {
            _refreshInProgress = false;

            if (work.Error != null)
            {
                LogHelper.Log(MyLogSeverity.Error, "NPC market refresh failed: " + work.Error);
#if DEBUG
                LogHelper.LogInfo("NPC market server clearing pending requests after refresh failure: pending=" +
                                  _pending.Count);
#endif
                _pending.Clear();
                return;
            }

            if (work.SaveGenerationAtCapture != _saveGeneration)
            {
#if DEBUG
                LogHelper.LogInfo("NPC market server discarded refresh due to save generation change: captured=" +
                                  work.SaveGenerationAtCapture + ", current=" + _saveGeneration +
                                  ", pending=" + _pending.Count);
#endif
                if (_pending.Count > 0)
                    StartRefresh();
                return;
            }

            var now = WorldTime.NowElapsedTicks();
            MergeKnownStationsFromCheckpoint(work.Result.VisitedStationIdsByIdentityId);
            MergePlayerNameHintsFromCheckpoint(work.Result.PlayerNameHintsByIdentityId);
            work.Result.Version = ++_nextVersion;
            work.Result.BuiltAtWorldElapsedTicks = now;
            work.Result.NextNoCacheAllowedAtWorldElapsedTicks = now + ForceRefreshMinimumAgeTicks;
            _cache = work.Result;
            _scopedReplies.Clear();
#if DEBUG
            LogHelper.LogInfo("NPC market server completed refresh: version=" + _cache.Version +
                              ", sellers=" + _cache.NpcSellerFactionsById.Count +
                              ", stations=" + _cache.StationsById.Count +
                              ", pending=" + _pending.Count);
#endif

            foreach (var request in _pending.Values)
            {
                NpcMarketScopeMode errorMode;
                PendingNpcMarketRequest resolved;
                var packetRequest = new PacketRequestNpcMarket
                {
                    RequestId = request.RequestId,
                    NoCache = request.NoCache,
                    HostBlockEntityId = request.HostBlockEntityId,
                    HostSurfaceIndex = request.HostSurfaceIndex
                };
                if (!TryResolveHostBlock(request.SenderSteamId, packetRequest, out resolved, out errorMode))
                    SendEmptyScope(request.SenderSteamId, packetRequest, errorMode);
                else
                    SendSnapshot(resolved, _cache, false);
            }

            _pending.Clear();
        }

        void SendSnapshot(PendingNpcMarketRequest request, ParsedNpcMarketCheckpoint cache, bool fromCache)
        {
            if (cache == null)
            {
                LogHelper.Log(MyLogSeverity.Warning, "NPC market server could not send snapshot because cache is null: steam=" +
                                  request.SenderSteamId + ", request=" + request.RequestId);
                return;
            }

            var scoped = GetOrBuildScopedReply(cache, request);
            LogHelper.LogInfo("NPC market request processed: steam=" + request.SenderSteamId +
                              ", request=" + request.RequestId + ", scope=" + scoped.Scope.Mode +
                              ", sellers=" + scoped.Sellers.Count + ", stations=" +
                              scoped.Scope.KnownStationCount + ", cache=" + fromCache + ", version=" +
                              cache.Version);
            var packet = new PacketSyncNpcMarket
            {
                RequestId = request.RequestId,
                Version = cache.Version,
                WasServedFromCache = fromCache,
                CapturedWorldElapsedTicks = cache.CapturedWorldElapsedTicks,
                CacheBuiltAtWorldElapsedTicks = cache.BuiltAtWorldElapsedTicks,
                NextEconomyTickWorldElapsedTicks = cache.NextEconomyTickWorldElapsedTicks,
                NextNoCacheAllowedAtWorldElapsedTicks = cache.NextNoCacheAllowedAtWorldElapsedTicks,
                EconomyTickSeconds = cache.EconomyTickSeconds,
                Scope = CloneScopeForHost(scoped.Scope, request),
                Sellers = scoped.Sellers
            };

            DeliverPacket(request.SenderSteamId, packet);
        }

        void SendEmptyScope(ulong steamId, PacketRequestNpcMarket request, NpcMarketScopeMode mode)
        {
            var now = WorldTime.NowElapsedTicks();
            LogHelper.LogInfo("NPC market request processed: steam=" + steamId +
                              ", request=" + request.RequestId + ", scope=" + mode + ", empty=true");
            var packet = new PacketSyncNpcMarket
            {
                RequestId = request.RequestId,
                Version = _cache != null ? _cache.Version : _nextVersion,
                WasServedFromCache = _cache != null,
                CapturedWorldElapsedTicks = _cache != null ? _cache.CapturedWorldElapsedTicks : now,
                CacheBuiltAtWorldElapsedTicks = _cache != null ? _cache.BuiltAtWorldElapsedTicks : now,
                NextEconomyTickWorldElapsedTicks = _cache != null ? _cache.NextEconomyTickWorldElapsedTicks : now,
                NextNoCacheAllowedAtWorldElapsedTicks = _cache != null ? _cache.NextNoCacheAllowedAtWorldElapsedTicks : now + ForceRefreshMinimumAgeTicks,
                EconomyTickSeconds = _cache != null ? _cache.EconomyTickSeconds : 0,
                Scope = new NpcMarketScopeDto
                {
                    Mode = mode,
                    HostBlockEntityId = request.HostBlockEntityId,
                    HostSurfaceIndex = request.HostSurfaceIndex
                },
                Sellers = new List<NpcMarketSellerFactionDto>()
            };

            DeliverPacket(steamId, packet);
        }

        void DeliverPacket(ulong steamId, PacketSyncNpcMarket packet)
        {
            var player = MyAPIGateway.Session != null ? MyAPIGateway.Session.Player : null;
            if (LcdModSessionComponent.Client != null && player != null && player.SteamUserId == steamId)
            {
#if DEBUG
                LogHelper.LogInfo("NPC market server delivering packet locally: steam=" + steamId +
                                  ", request=" + packet.RequestId);
#endif
                LcdModSessionComponent.Client.HandleLocalSyncNpcMarket(packet);
                return;
            }

#if DEBUG
            LogHelper.LogInfo("NPC market server transmitting packet to player: steam=" + steamId +
                              ", request=" + packet.RequestId);
#endif
            LcdModSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId, false);
        }

        ScopedNpcMarketReply GetOrBuildScopedReply(ParsedNpcMarketCheckpoint cache, PendingNpcMarketRequest request)
        {
            var scope = ResolveScope(cache, request.HostOwnerIdentityId);
            var key = MarketScopeCacheKey.From(scope);
            ScopedNpcMarketReply reply;
            if (_scopedReplies.TryGetValue(key, out reply))
                return reply;

            var knownStations = BuildKnownStationSet(cache, scope);
            var sellers = BuildSellerGroups(cache, knownStations);
            var knownStationCount = 0;
            for (var i = 0; i < sellers.Count; i++)
                knownStationCount += sellers[i].Stations != null ? sellers[i].Stations.Count : 0;

            scope.KnownStationCount = knownStationCount;
            reply = new ScopedNpcMarketReply
            {
                Scope = ToScopeDto(cache, scope),
                Sellers = sellers
            };
            _scopedReplies[key] = reply;
            return reply;
        }

        static MarketScopeDescriptor ResolveScope(ParsedNpcMarketCheckpoint cache, long hostOwnerIdentityId)
        {
            if (hostOwnerIdentityId == 0)
                return MarketScopeDescriptor.Unowned(hostOwnerIdentityId);

            long factionId;
            if (cache.PlayerFactionIdByIdentityId.TryGetValue(hostOwnerIdentityId, out factionId))
                return MarketScopeDescriptor.OwnerFactionUnion(hostOwnerIdentityId, factionId);

            return MarketScopeDescriptor.OwnerOnly(hostOwnerIdentityId);
        }

        Dictionary<long, StationKnowledgeAccumulator> BuildKnownStationSet(
            ParsedNpcMarketCheckpoint cache,
            MarketScopeDescriptor scope)
        {
            var known = new Dictionary<long, StationKnowledgeAccumulator>();
            if (scope.Mode == NpcMarketScopeMode.OwnerFactionUnion)
            {
                HashSet<long> memberIds;
                if (!cache.MemberIdentityIdsByFactionId.TryGetValue(scope.HostFactionId, out memberIds))
                    return known;

                foreach (var memberId in memberIds)
                    AddKnownStationsForIdentity(known, memberId, memberId == scope.HostOwnerIdentityId);
                return known;
            }

            if (scope.Mode == NpcMarketScopeMode.OwnerOnly)
                AddKnownStationsForIdentity(known, scope.HostOwnerIdentityId, true);

            return known;
        }

        void AddKnownStationsForIdentity(
            Dictionary<long, StationKnowledgeAccumulator> known,
            long identityId,
            bool isHostOwner)
        {
            HashSet<long> stationIds;
            if (!_knownStationIdsByPlayerId.TryGetValue(identityId, out stationIds))
                return;

            foreach (var stationId in stationIds)
            {
                StationKnowledgeAccumulator item;
                if (!known.TryGetValue(stationId, out item))
                {
                    item = new StationKnowledgeAccumulator();
                    known[stationId] = item;
                }

                item.KnownByMemberCount++;
                item.Flags |= isHostOwner
                    ? NpcMarketStationKnowledgeFlags.HostOwner
                    : NpcMarketStationKnowledgeFlags.OtherFactionMember;
            }
        }

        static List<NpcMarketSellerFactionDto> BuildSellerGroups(
            ParsedNpcMarketCheckpoint cache,
            Dictionary<long, StationKnowledgeAccumulator> knownStations)
        {
            var groups = new Dictionary<long, NpcMarketSellerFactionDto>();
            foreach (var known in knownStations)
            {
                ParsedNpcStation station;
                if (!cache.StationsById.TryGetValue(known.Key, out station))
                {
#if DEBUG
                    LogHelper.LogOnce("NpcMarketMissingKnownStation:" + known.Key,
                        "NPC market ledger referenced missing station: stationId=" + known.Key);
#endif
                    continue;
                }

                ParsedNpcSellerFaction seller;
                if (!cache.NpcSellerFactionsById.TryGetValue(station.NpcFactionId, out seller))
                    continue;

                NpcMarketSellerFactionDto dto;
                if (!groups.TryGetValue(seller.FactionId, out dto))
                {
                    dto = new NpcMarketSellerFactionDto
                    {
                        FactionId = seller.FactionId,
                        Tag = seller.Tag,
                        Name = seller.Name,
                        Stations = new List<NpcMarketStationDto>()
                    };
                    groups.Add(dto.FactionId, dto);
                }

                dto.Stations.Add(new NpcMarketStationDto
                {
                    StationId = station.StationId,
                    Name = station.Name,
                    DisplayName = ResolveStationDisplayName(station, seller),
                    NpcFactionId = station.NpcFactionId,
                    Position = station.Position,
                    StationType = station.StationType,
                    IsDeepSpaceStation = station.IsDeepSpaceStation,
                    KnowledgeFlags = known.Value.Flags,
                    KnownByMemberCount = known.Value.KnownByMemberCount,
                    Offers = station.Offers
                });
            }

            var result = new List<NpcMarketSellerFactionDto>(groups.Values);
            result.Sort(CompareSellerDtos);
            for (var i = 0; i < result.Count; i++)
                result[i].Stations.Sort(CompareStationDtos);
            return result;
        }

        static int CompareSellerDtos(NpcMarketSellerFactionDto x, NpcMarketSellerFactionDto y)
        {
            var tag = string.Compare(x.Tag, y.Tag, StringComparison.OrdinalIgnoreCase);
            return tag != 0 ? tag : string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }

        static int CompareStationDtos(NpcMarketStationDto x, NpcMarketStationDto y)
        {
            var name = string.Compare(GetStationDisplayName(x), GetStationDisplayName(y),
                StringComparison.OrdinalIgnoreCase);
            return name != 0 ? name : x.StationId.CompareTo(y.StationId);
        }

        static string ResolveStationDisplayName(ParsedNpcStation station, ParsedNpcSellerFaction seller)
        {
            if (station == null || string.IsNullOrWhiteSpace(station.Name))
                return string.Empty;

            var factionTag = seller != null ? seller.Tag : string.Empty;
            if (string.IsNullOrWhiteSpace(factionTag) ||
                station.Name.StartsWith(factionTag + " ", StringComparison.OrdinalIgnoreCase))
            {
                return station.Name;
            }

            return factionTag + " " + station.Name;
        }

        static string GetStationDisplayName(NpcMarketStationDto station)
        {
            return !string.IsNullOrWhiteSpace(station.DisplayName) ? station.DisplayName : station.Name;
        }

        static NpcMarketScopeDto ToScopeDto(ParsedNpcMarketCheckpoint cache, MarketScopeDescriptor scope)
        {
            var dto = new NpcMarketScopeDto
            {
                Mode = scope.Mode,
                HostOwnerIdentityId = scope.HostOwnerIdentityId,
                HostFactionId = scope.HostFactionId,
                KnownStationCount = scope.KnownStationCount
            };

            if (scope.HostFactionId != 0)
            {
                ParsedNpcSellerFaction faction;
                if (cache.NpcSellerFactionsById.TryGetValue(scope.HostFactionId, out faction))
                {
                    dto.HostFactionTag = faction.Tag;
                    dto.HostFactionName = faction.Name;
                }

                HashSet<long> members;
                if (cache.MemberIdentityIdsByFactionId.TryGetValue(scope.HostFactionId, out members))
                    dto.ContributingMemberCount = members.Count;
            }
            else if (scope.Mode == NpcMarketScopeMode.OwnerOnly)
            {
                dto.ContributingMemberCount = 1;
            }

            return dto;
        }

        static NpcMarketScopeDto CloneScopeForHost(NpcMarketScopeDto source, PendingNpcMarketRequest request)
        {
            return new NpcMarketScopeDto
            {
                Mode = source.Mode,
                HostBlockEntityId = request.HostBlockEntityId,
                HostSurfaceIndex = request.HostSurfaceIndex,
                HostOwnerIdentityId = source.HostOwnerIdentityId,
                HostFactionId = source.HostFactionId,
                HostFactionTag = source.HostFactionTag,
                HostFactionName = source.HostFactionName,
                ContributingMemberCount = source.ContributingMemberCount,
                KnownStationCount = source.KnownStationCount
            };
        }

        static void ParseFactionMembership(MyObjectBuilder_FactionCollection factions, ParsedNpcMarketCheckpoint cache)
        {
            if (factions == null || factions.Players == null || factions.Players.Dictionary == null)
                return;

            foreach (var pair in factions.Players.Dictionary)
            {
                cache.PlayerFactionIdByIdentityId[pair.Key] = pair.Value;
                GetOrCreate(cache.MemberIdentityIdsByFactionId, pair.Value).Add(pair.Key);
            }
        }

        static void ParseVisitedStations(MyObjectBuilder_Checkpoint checkpoint, ParsedNpcMarketCheckpoint cache)
        {
            if (checkpoint.AllPlayersData == null || checkpoint.AllPlayersData.Dictionary == null)
                return;

            foreach (var playerEntry in checkpoint.AllPlayersData.Dictionary)
            {
                var player = playerEntry.Value;
                if (player == null || player.IdentityId == 0)
                    continue;

                var playerName = player.DisplayName;
                if (!string.IsNullOrWhiteSpace(playerName))
                    cache.PlayerNameHintsByIdentityId[player.IdentityId] = playerName;

                var known = GetOrCreate(cache.VisitedStationIdsByIdentityId, player.IdentityId);
                if (player.VisitedStationIds == null)
                    continue;

                for (var i = 0; i < player.VisitedStationIds.Count; i++)
                    known.Add(player.VisitedStationIds[i]);
            }
        }

        static void ParseNpcSellersAndStations(MyObjectBuilder_FactionCollection factions, ParsedNpcMarketCheckpoint cache)
        {
            if (factions == null || factions.Factions == null)
                return;

            for (var i = 0; i < factions.Factions.Count; i++)
            {
                var faction = factions.Factions[i];
                if (faction == null)
                    continue;

                ParsedNpcSellerFaction seller;
                if (!cache.NpcSellerFactionsById.TryGetValue(faction.FactionId, out seller))
                {
                    seller = new ParsedNpcSellerFaction
                    {
                        FactionId = faction.FactionId,
                        Tag = faction.Tag,
                        Name = faction.Name,
                        StationIds = new List<long>()
                    };
                    cache.NpcSellerFactionsById[seller.FactionId] = seller;
                }

                if (faction.Stations == null || faction.Stations.Count == 0)
                    continue;

                seller.Tag = faction.Tag;
                seller.Name = faction.Name;

                for (var stationIndex = 0; stationIndex < faction.Stations.Count; stationIndex++)
                {
                    var parsed = ParseNpcStation(faction.Stations[stationIndex], faction.FactionId);
                    if (parsed == null)
                        continue;

                    cache.StationsById[parsed.StationId] = parsed;
                    seller.StationIds.Add(parsed.StationId);
                }
            }
        }

        static ParsedNpcStation ParseNpcStation(MyObjectBuilder_Station station, long npcFactionId)
        {
            if (station == null)
                return null;

            var offers = new List<NpcMarketOfferDto>();
            if (station.StoreItems != null)
            {
                for (var i = 0; i < station.StoreItems.Count; i++)
                {
                    var parsed = TryParseMarketListing(station.StoreItems[i]);
                    if (parsed != null)
                        offers.Add(parsed);
                }
            }

            offers.Sort(NpcMarketOfferDtoComparer.Instance);
            return new ParsedNpcStation
            {
                StationId = station.Id,
                Name = station.Name,
                NpcFactionId = npcFactionId,
                Position = station.Position,
                StationType = station.StationType,
                IsDeepSpaceStation = station.IsDeepSpaceStation,
                Offers = offers
            };
        }

        static NpcMarketOfferDto TryParseMarketListing(MyObjectBuilder_StoreItem item)
        {
            if (item == null || item.Amount <= 0 || item.PricePerUnit <= 0 ||
                !IsSupportedDirection(item.StoreItemType))
            {
                return null;
            }

            var dto = new NpcMarketOfferDto
            {
                StoreItemType = item.StoreItemType,
                ItemType = item.ItemType,
                RawPricePerUnit = item.PricePerUnit,
                PreviousRawPricePerUnit = item.PreviousPricePerUnit,
                Amount = item.Amount
            };

            switch (item.ItemType)
            {
                case ItemTypes.PhysicalItem:
                case ItemTypes.Gas:
                    if (!item.Item.HasValue)
                        return null;

                    var id = item.Item.Value;
                    dto.TypeId = id.TypeId.ToString();
                    dto.SubtypeId = id.SubtypeName;
                    return dto;

                case ItemTypes.Oxygen:
                case ItemTypes.Hydrogen:
                    return dto;

                case ItemTypes.Grid:
                    if (string.IsNullOrWhiteSpace(item.PrefabName))
                        return null;

                    dto.PrefabName = item.PrefabName;
                    dto.PrefabTotalPcu = item.PrefabTotalPcu;
                    return dto;

                default:
                    return null;
            }
        }

        static bool IsSupportedDirection(StoreItemTypes type)
        {
            return type == StoreItemTypes.Offer || type == StoreItemTypes.Order;
        }

        static HashSet<long> GetOrCreate(Dictionary<long, HashSet<long>> map, long key)
        {
            HashSet<long> value;
            if (!map.TryGetValue(key, out value))
            {
                value = new HashSet<long>();
                map[key] = value;
            }

            return value;
        }

        static MyObjectBuilder_SessionComponentEconomy FindEconomyComponent(List<MyObjectBuilder_SessionComponent> components)
        {
            if (components == null)
                return null;

            for (var i = 0; i < components.Count; i++)
            {
                var economy = components[i] as MyObjectBuilder_SessionComponentEconomy;
                if (economy != null)
                    return economy;
            }

            return null;
        }

        struct PendingRequestKey : IEquatable<PendingRequestKey>
        {
            readonly ulong _steamId;
            readonly uint _requestId;

            public PendingRequestKey(ulong steamId, uint requestId)
            {
                _steamId = steamId;
                _requestId = requestId;
            }

            public bool Equals(PendingRequestKey other)
            {
                return _steamId == other._steamId && _requestId == other._requestId;
            }

            public override bool Equals(object obj)
            {
                return obj is PendingRequestKey && Equals((PendingRequestKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_steamId.GetHashCode() * 397) ^ _requestId.GetHashCode();
                }
            }
        }

        struct MarketScopeCacheKey : IEquatable<MarketScopeCacheKey>
        {
            readonly NpcMarketScopeMode _mode;
            readonly long _ownerIdentityId;
            readonly long _ownerFactionId;

            MarketScopeCacheKey(NpcMarketScopeMode mode, long ownerIdentityId, long ownerFactionId)
            {
                _mode = mode;
                _ownerIdentityId = ownerIdentityId;
                _ownerFactionId = ownerFactionId;
            }

            public static MarketScopeCacheKey From(MarketScopeDescriptor scope)
            {
                return scope.Mode == NpcMarketScopeMode.OwnerFactionUnion
                    ? new MarketScopeCacheKey(scope.Mode, 0L, scope.HostFactionId)
                    : new MarketScopeCacheKey(scope.Mode, scope.HostOwnerIdentityId, 0L);
            }

            public bool Equals(MarketScopeCacheKey other)
            {
                return _mode == other._mode && _ownerIdentityId == other._ownerIdentityId && _ownerFactionId == other._ownerFactionId;
            }

            public override bool Equals(object obj)
            {
                return obj is MarketScopeCacheKey && Equals((MarketScopeCacheKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = (int)_mode;
                    hash = (hash * 397) ^ _ownerIdentityId.GetHashCode();
                    hash = (hash * 397) ^ _ownerFactionId.GetHashCode();
                    return hash;
                }
            }
        }

        sealed class PendingNpcMarketRequest
        {
            public ulong SenderSteamId;
            public uint RequestId;
            public bool NoCache;
            public long HostBlockEntityId;
            public int HostSurfaceIndex;
            public long HostOwnerIdentityId;
            public long RequestingIdentityId;
        }

        sealed class ParsedNpcMarketCheckpoint
        {
            public int Version;
            public long CapturedWorldElapsedTicks;
            public long BuiltAtWorldElapsedTicks;
            public long NextEconomyTickWorldElapsedTicks;
            public long NextNoCacheAllowedAtWorldElapsedTicks;
            public int EconomyTickSeconds;
            public Dictionary<long, ParsedNpcSellerFaction> NpcSellerFactionsById;
            public Dictionary<long, ParsedNpcStation> StationsById;
            public Dictionary<long, long> PlayerFactionIdByIdentityId;
            public Dictionary<long, HashSet<long>> MemberIdentityIdsByFactionId;
            public Dictionary<long, HashSet<long>> VisitedStationIdsByIdentityId;
            public Dictionary<long, string> PlayerNameHintsByIdentityId;
        }

        sealed class ParsedNpcSellerFaction
        {
            public long FactionId;
            public string Tag;
            public string Name;
            public List<long> StationIds;
        }

        sealed class ParsedNpcStation
        {
            public long StationId;
            public string Name;
            public long NpcFactionId;
            public Vector3D Position;
            public MyStationTypeEnum StationType;
            public bool IsDeepSpaceStation;
            public List<NpcMarketOfferDto> Offers;
        }

        sealed class ScopedNpcMarketReply
        {
            public NpcMarketScopeDto Scope;
            public List<NpcMarketSellerFactionDto> Sellers;
        }

        sealed class StationKnowledgeAccumulator
        {
            public NpcMarketStationKnowledgeFlags Flags;
            public int KnownByMemberCount;
        }

        struct MarketScopeDescriptor
        {
            public NpcMarketScopeMode Mode;
            public long HostOwnerIdentityId;
            public long HostFactionId;
            public int KnownStationCount;

            public static MarketScopeDescriptor Unowned(long ownerId)
            {
                return new MarketScopeDescriptor { Mode = NpcMarketScopeMode.UnownedHostBlock, HostOwnerIdentityId = ownerId };
            }

            public static MarketScopeDescriptor OwnerFactionUnion(long ownerId, long factionId)
            {
                return new MarketScopeDescriptor
                {
                    Mode = NpcMarketScopeMode.OwnerFactionUnion,
                    HostOwnerIdentityId = ownerId,
                    HostFactionId = factionId
                };
            }

            public static MarketScopeDescriptor OwnerOnly(long ownerId)
            {
                return new MarketScopeDescriptor { Mode = NpcMarketScopeMode.OwnerOnly, HostOwnerIdentityId = ownerId };
            }
        }

        sealed class MarketRefreshWork
        {
            public int SaveGenerationAtCapture;
            public MyObjectBuilder_Checkpoint Checkpoint;
            public ParsedNpcMarketCheckpoint Result;
            public Exception Error;
        }

        sealed class NpcMarketOfferDtoComparer : IComparer<NpcMarketOfferDto>
        {
            public static readonly NpcMarketOfferDtoComparer Instance = new NpcMarketOfferDtoComparer();

            public int Compare(NpcMarketOfferDto x, NpcMarketOfferDto y)
            {
                if (ReferenceEquals(x, y))
                    return 0;

                if (x == null)
                    return -1;

                if (y == null)
                    return 1;

                var result = x.ItemType.CompareTo(y.ItemType);
                if (result != 0)
                    return result;

                result = x.StoreItemType.CompareTo(y.StoreItemType);
                if (result != 0)
                    return result;

                result = string.Compare(x.TypeId, y.TypeId, StringComparison.Ordinal);
                if (result != 0)
                    return result;

                result = string.Compare(x.SubtypeId, y.SubtypeId, StringComparison.Ordinal);
                if (result != 0)
                    return result;

                result = string.Compare(x.PrefabName, y.PrefabName, StringComparison.Ordinal);
                if (result != 0)
                    return result;

                result = x.RawPricePerUnit.CompareTo(y.RawPricePerUnit);
                return result != 0 ? result : x.Amount.CompareTo(y.Amount);
            }
        }
    }
}
