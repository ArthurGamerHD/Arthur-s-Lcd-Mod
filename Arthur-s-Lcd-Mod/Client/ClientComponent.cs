using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Generated;
using LcdMod.Client.Audio;
#if EXPERIMENTAL
using LcdMod.Client.Diagnostics;
#endif
using LcdMod.Client.Market;
using LcdMod.Client.Ftue;
using LcdMod.Client.Modules.Power;
using LcdMod.Client.Modules.Cartography;
using LcdMod.Client.Modules.RoomEnvironment;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using ParallelTasks;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using ItemsAppBase = LcdMod.Client.Apps.Abstract.ItemsApp;
using ItemsSurfaceScriptBase = LcdMod.Client.SurfaceScripts.Abstract.ItemsSurfaceScriptBase;

namespace LcdMod.Client
{
    public sealed class LcdModClientComponent
    {
        public static readonly List<Action> RunNextFrame = new List<Action>();
        static readonly List<Action> RunThisFrame = new List<Action>();
        
        public static readonly List<Task> Blocker = new List<Task>();
        
        sealed class ScheduledOnePerFrameAction
        {
            public Action Action;
            public long DueFrame;
        }

        static readonly List<ScheduledOnePerFrameAction> RunOnePerFrame =
            new List<ScheduledOnePerFrameAction>();

        readonly LcdModSessionComponent _session;
        readonly TerminalManager _terminalManager;
        readonly AudioPocService _audioPoc = new AudioPocService();
        readonly AudioImportService _audioImport = new AudioImportService();
        readonly AudioBroadcastClientService _audioBroadcast = new AudioBroadcastClientService();
        readonly GameAudioTestReportService _audioTestReport = new GameAudioTestReportService();
#if DEBUG
        readonly CartographyDebugReportService _cartographyDebugReport;
#endif
#if EXPERIMENTAL
        readonly AppRunProfilerService _appRunProfiler = new AppRunProfilerService();
#endif

        public LcdModClientComponent(LcdModSessionComponent session)
        {
            _session = session;
            _terminalManager = new TerminalManager(session);
            RoomEnvironment = new GridRoomEnvironmentClientModule();
            Ftue = new FtueService();
            Cartography = new CartographyModule();
#if DEBUG
            _cartographyDebugReport = new CartographyDebugReportService(Cartography);
#endif
        }

        public PowerDataModule PowerData { get; private set; }
        public CartographyModule Cartography { get; private set; }
        public GridRoomEnvironmentClientModule RoomEnvironment { get; }
        internal FtueService Ftue { get; }
        public static event Action OnUpdateBeforeSimulation;

        public void LoadData()
        {
            LocalConfigManager.Load();
            _session.RegisterModules();
            PowerData = new PowerDataModule();
            RunNextFrame.Add(TextureHelper.InitializeColorfulIconsApi);

            var group = CommandManager.GetOrCreateGroup("/lcdMod", new CmdGroupInitializer(7));
            group.TryAdd("FactionColor", FactionHelper.SetColor);
            group.TryAdd("Advanced", LocalConfigManager.SetAdvancedTweakablesCommand, 1);
            group.TryAdd("RenderUserGeneratedTextures", LocalConfigManager.RenderUserGeneratedTextures);
            group.TryAdd("LegacyLocalTextureStorage", LocalConfigManager.SetLegacyLocalTextureStorageCommand);
            group.TryAdd("PreloadTextures", _ => TextureHelper.PreloadAllTextures());
            group.TryAdd("ClearCache", TextureHelper.ClearCacheCommand);
            group.TryAdd("ResetFtue", Ftue.ResetCommand);
            group.TryAdd("ImportTextures", _ => TextureHelper.Import(true));
            group.TryAdd("RemoveLocalTexture", TextureHelper.RemoveLocalTexture);
            group.TryAdd("ImportLocalTexture", TextureHelper.ImportLocalTexture);
            group.TryAdd("PlayAudio", _audioPoc.PlayAudioCommand, 1);
            group.TryAdd("PlayGameAudio", _audioPoc.PlayGameAudioCommand, 1);
            group.TryAdd("ImportLocalAudio", _audioImport.ImportLocalAudioCommand, 1);
            group.TryAdd("ImportAudios", _audioImport.ImportAudiosCommand);
            group.TryAdd("StreamAudio", _audioBroadcast.StreamAudioCommand, 1);
            group.TryAdd("TestAudio", _audioTestReport.TestAllGameAudioCommand);
            group.TryAdd("TestGameAudio", _audioTestReport.TestAllGameAudioCommand);
#if EXPERIMENTAL
            group.TryAdd("Profile", _appRunProfiler.Command);
#endif
#if DEBUG
            group.TryAdd("TestCartography", _cartographyDebugReport.Command);
#endif
#if DEBUG
            group.TryAdd("DebugInteractive", LocalConfigManager.SetDebugInteractiveCommand);
            group.TryAdd("DebugSurface", LocalConfigManager.SetDebugSurfaceCommand);
            group.TryAdd("SpriteCountDebug", LocalConfigManager.SetSpriteCountDebugCommand);
            group.TryAdd("VisibleClip", LocalConfigManager.SetVisibleClipCommand);
            group.TryAdd("TextInput",
                strings => TextInputHelper.SpawnForLocalPlayer(strings.FirstOrDefault(),
                    s => MyAPIGateway.Utilities.ShowNotification("User typed: " + s), "Hello World!",
                    strings.Length > 1 ? strings[1] : string.Empty));
#endif

            DebuggerHelper.Break();
            MyAPIGateway.Entities.OnEntityAdd += EntityAdded;

            MyAPIGateway.Session.Factions.FactionCreated += FactionUpdated;
            MyAPIGateway.Session.Factions.FactionEdited += FactionUpdated;
            MyAPIGateway.Session.Factions.FactionStateChanged += FactionStateChanged;
        }

        public void BeforeStart()
        {
            PlanetHelper.RefreshPlanets();
            LoadLocalization();
            Ftue.Load();
            MyAPIGateway.Gui.GuiControlRemoved += OnGuiControlRemoved;
            _terminalManager.Initialize();
        }

        public void UnloadData()
        {
            Ftue.Unload();
#if EXPERIMENTAL
            _appRunProfiler.Unload();
#endif
            _audioPoc.Unload();
            _audioBroadcast.Unload();
            LocalConfigManager.Save();
            TextureHelper.UnloadColorfulIconsApi();
            if (PowerData != null)
                PowerData.Clear();
            PowerData = null;
#if DEBUG
            _cartographyDebugReport.Unload();
#endif
            if (Cartography != null)
                Cartography.Clear();
            Cartography = null;
            _terminalManager.Unload();
            MyAPIGateway.Gui.GuiControlRemoved -= OnGuiControlRemoved;
            MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;
            MyAPIGateway.Session.Factions.FactionCreated -= FactionUpdated;
            MyAPIGateway.Session.Factions.FactionEdited -= FactionUpdated;
            MyAPIGateway.Session.Factions.FactionStateChanged -= FactionStateChanged;

            _session._grids.Clear();
            LcdModSessionComponent.Components.Clear();
            _session.ClearModules();
            PlanetHelper.Clear();
            LcdModSessionComponent.Components = null;
            LcdModSessionComponent.DebugSnapshot = SessionDebugSnapshot.Empty;

            ItemsSurfaceScriptBase.SpriteCache?.Clear();
            ItemsSurfaceScriptBase.SpriteCache = null;
            ItemsAppBase.SpriteCache?.Clear();
            LcdModSessionComponent.ClearClientEvents();

            ListBoxItemHelper.PerTypeCache.Clear();
            NpcMarketClientCache.Reset();
            //TextureHelper.UnloadTextureCache();
            RunNextFrame.Clear();
            RunThisFrame.Clear();
            Blocker.Clear();
            RunOnePerFrame.Clear();
            OnUpdateBeforeSimulation = null;
        }

        public void UpdateBeforeSimulation()
        {
            try
            {
                _session._updateTick++;
                foreach (var grid in _session._grids.Values)
                {
                    if (grid.Item1.MarkedForClose)
                        continue;

                    grid.Item2.Update();
                }

                if (PowerData != null)
                    PowerData.Update(MyAPIGateway.Session.GameplayFrameCounter);

                _session.UpdateModules();
                UpdateDebugSnapshot();
                OnUpdateBeforeSimulation?.Invoke();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
        }


        static void RunNextFrameActions()
        {
            if (RunNextFrame.Count == 0)
                return;

            RunThisFrame.Clear();
            RunThisFrame.AddRange(RunNextFrame);
            RunNextFrame.Clear();

            for (int i = 0; i < RunThisFrame.Count; i++)
            {
                var action = RunThisFrame[i];
                if (action == null)
                    continue;

                try
                {
                    action();
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, nameof(RunNextFrameActions));
                }
            }

            RunThisFrame.Clear();
        }


        public static void ScheduleOnePerFrame(Action action)
        {
            ScheduleOnePerFrame(action, 0);
        }

        public static void ScheduleOnePerFrame(Action action, int delayTicks)
        {
            if (action == null)
                return;

            long currentFrame = MyAPIGateway.Session != null
                ? MyAPIGateway.Session.GameplayFrameCounter
                : 0L;
            RunOnePerFrame.Add(new ScheduledOnePerFrameAction
            {
                Action = action,
                DueFrame = currentFrame + Math.Max(0, delayTicks)
            });
        }

        static void RunOnePerFrameAction()
        {
            if (RunOnePerFrame.Count == 0)
                return;

            long currentFrame = MyAPIGateway.Session != null
                ? MyAPIGateway.Session.GameplayFrameCounter
                : long.MaxValue;
            int scheduledIndex = -1;
            for (int i = 0; i < RunOnePerFrame.Count; i++)
            {
                if (RunOnePerFrame[i].DueFrame > currentFrame)
                    continue;

                scheduledIndex = i;
                break;
            }

            if (scheduledIndex < 0)
                return;

            var scheduled = RunOnePerFrame[scheduledIndex];
            RunOnePerFrame.RemoveAt(scheduledIndex);
            var action = scheduled.Action;
            if (action == null)
                return;

            try
            {
                action();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, nameof(RunOnePerFrameAction));
            }
        }

        public void Simulate()
        {
            RunNextFrameActions();
            RunOnePerFrameAction();
#if EXPERIMENTAL
            _appRunProfiler.Update();
#endif
            _audioPoc.Update();
            _audioBroadcast.Update();
        }

        public void UpdateAfterSimulation()
        {
            try
            {
                _session.PostUpdateModules();
                LcdModSessionComponent.RaiseAfterSimulationUpdate();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
            
            WaitForBlockers();
        }

        void WaitForBlockers()
        {
            foreach (var task in Blocker)
            {
                if (task.valid && !task.IsComplete)
                    task.Wait();
            }
            
            Blocker.Clear();
        }

        public void HandleSyncConfig(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<NetworkPackageSyncComponentConfig>();
            var block = MyEntities.GetEntityById(packet.BlockId) as IMyFunctionalBlock;
            if (block == null)
                return;

            var settings = SurfaceScriptBase.Instances.FirstOrDefault(a => a.Block.EntityId.Equals(block.EntityId))
                ?.ProviderConfig;
            if (settings == null)
                return;

            LcdModSessionComponent.ApplySyncedConfig(block, settings, packet.Config);
        }

        public void HandleRequestTexture(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<PacketRequestTexture>();
            if (packet == null || string.IsNullOrWhiteSpace(packet.TextureName))
                return;

            HandleLocalRequestTexture(packet);
        }

        public void HandleLocalRequestTexture(PacketRequestTexture packet)
        {
            if (packet == null || string.IsNullOrWhiteSpace(packet.TextureName))
                return;

            var localSteamId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            if (localSteamId == 0 || packet.OwnerSteamId != localSteamId)
                return;

            byte[] textureBytes;
            string fileName;
            string failureReason;
            if (!TextureTransferHelper.TryReadTextureBytesForSync( packet.OwnerSteamId,
                    packet.TextureName, out textureBytes, out fileName, out failureReason))
            {
                LogHelper.Log(MyLogSeverity.Warning,
                    $"Client did not find requested texture {TextureTransferHelper.BuildTextureKey(packet.OwnerSteamId, packet.TextureName)} for requester {packet.RequesterSteamId}: {failureReason}");
                return;
            }

            LogHelper.LogInfo(
                $"Client found requested texture {TextureTransferHelper.BuildTextureKey(packet.OwnerSteamId, packet.TextureName)} at {fileName} for requester {packet.RequesterSteamId}");

            int width;
            int height;
            TextureTransferHelper.TryGetDdsDimensions(textureBytes, out width, out height);

            var ownerName = MyAPIGateway.Session?.Player?.DisplayName ?? string.Empty;
            var metadata = new TextureTransferHelper.TextureMetadata
            {
                OwnerSteamId = localSteamId,
                OwnerName = ownerName,
                RegistrationName = TextureTransferHelper.BuildTextureKey(localSteamId, packet.TextureName),
                TextureName = TextureTransferHelper.NormalizeTextureName(packet.TextureName),
                SourceFileName = fileName,
                Width = width,
                Height = height,
                LastUpdatedUtcTicks = DateTime.UtcNow.Ticks
            };

            var syncPacket = new PacketSyncTexture(packet.OwnerSteamId, packet.TextureName, packet.RequesterSteamId,
                textureBytes, metadata);
            LogHelper.LogInfo(
                $"Client sending requested texture {TextureTransferHelper.BuildTextureKey(packet.OwnerSteamId, packet.TextureName)} to server ({textureBytes.Length} bytes)");
            if (_session.Session.IsServer && LcdModSessionComponent.Server != null)
            {
                LcdModSessionComponent.Server.HandleLocalSyncTexture(syncPacket);
                return;
            }

            LcdModSessionComponent.NetworkManager.TransmitToServer(syncPacket, false);
        }

        public void HandleSyncTexture(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<PacketSyncTexture>();
            if (packet == null || string.IsNullOrWhiteSpace(packet.TextureName) || !TextureTransferHelper.IsValidTexturePayload(packet.Data))
                return;

            HandleLocalSyncTexture(packet);
        }

        public void HandleLocalSyncTexture(PacketSyncTexture packet)
        {
            if (packet == null || string.IsNullOrWhiteSpace(packet.TextureName) || !TextureTransferHelper.IsValidTexturePayload(packet.Data))
                return;

            var localSteamId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            if (localSteamId != 0 && packet.RequesterSteamId != 0 && packet.RequesterSteamId != localSteamId)
                return;

            LogHelper.LogInfo(
                $"Client received requested texture {TextureTransferHelper.BuildTextureKey(packet.OwnerSteamId, packet.TextureName)} from server ({packet.Data.Length} bytes)");
            TextureHelper.SaveRemoteTexture(packet);
        }

        public void HandleSyncNpcMarket(ReceivedPacketEventArgs args)
        {
            HandleLocalSyncNpcMarket(args.UnWrap<PacketSyncNpcMarket>());
        }

        public void HandleLocalSyncNpcMarket(PacketSyncNpcMarket packet)
        {
#if DEBUG
            LogHelper.LogInfo("Client received NPC market sync packet: request=" +
                              (packet != null ? packet.RequestId.ToString() : "<null>") +
                              ", scope=" + (packet != null && packet.Scope != null
                                  ? packet.Scope.Mode.ToString()
                                  : "<none>"));
#endif
            NpcMarketClientCache.HandleSync(packet);
        }

        public void HandleSyncBroadcastAudio(ReceivedPacketEventArgs args)
        {
            if (!args.IsFromServer)
                return;

            _audioBroadcast.HandleSync(args.UnWrap<PacketSyncBroadcastAudio>());
        }

        public void HandleLocalSyncBroadcastAudio(PacketSyncBroadcastAudio packet)
        {
            _audioBroadcast.HandleSync(packet);
        }

        public bool StartMediaPlayerLocalAudioStream(IMyTerminalBlock block, int surfaceIndex, AudioAssetMetadata asset, string title)
        {
            return _audioBroadcast.StartMediaPlayerLocalAudioStream(block, surfaceIndex, asset, title);
        }

        public bool CancelMediaPlayerLocalAudioStream(long blockEntityId, int surfaceIndex, bool stopPlayback)
        {
            return _audioBroadcast.CancelMediaPlayerLocalAudioStream(
                blockEntityId,
                surfaceIndex,
                stopPlayback);
        }

        public void HandleSyncMediaStreamChunk(ReceivedPacketEventArgs args)
        {
            if (!args.IsFromServer)
                return;

            HandleLocalSyncMediaStreamChunk(args.UnWrap<PacketSyncMediaStreamChunk>());
        }

        public void HandleLocalSyncMediaStreamChunk(PacketSyncMediaStreamChunk packet)
        {
            _audioBroadcast.HandleStreamChunk(packet);
        }

        public void HandleMediaStreamControl(ReceivedPacketEventArgs args)
        {
            if (!args.IsFromServer)
                return;

            HandleLocalMediaStreamControl(args.UnWrap<PacketMediaStreamControl>());
        }

        public void HandleLocalMediaStreamControl(PacketMediaStreamControl packet)
        {
            _audioBroadcast.HandleStreamControl(packet);
        }

        public void HandleSyncMediaPlayerCommand(ReceivedPacketEventArgs args)
        {
            if (!args.IsFromServer)
                return;

            HandleLocalSyncMediaPlayerCommand(args.UnWrap<PacketSyncMediaPlayerCommand>());
        }

        public void HandleLocalSyncMediaPlayerCommand(PacketSyncMediaPlayerCommand packet)
        {
            if (packet == null ||
                packet.BlockEntityId == 0 ||
                packet.SurfaceIndex < 0 ||
                packet.AppTypeId != (int)AppType.MediaPlayer)
            {
                return;
            }

            var block = MyEntities.GetEntityById(packet.BlockEntityId) as IMyTerminalBlock;
            if (block == null || block.Closed || block.MarkedForClose || block.CubeGrid == null)
                return;

            var gridLogic = LcdModSessionComponent.GetOrCreateGridLogic(block.CubeGrid);
            if (gridLogic == null)
                return;

            var player = gridLogic.GetMediaPlayer(packet.BlockEntityId, packet.SurfaceIndex);
            if (player == null)
                return;

            if (packet.Command == MediaPlayerCommandKind.Play ||
                packet.Command == MediaPlayerCommandKind.Stop)
            {
                _audioBroadcast.CancelMediaPlayerLocalAudioStream(
                    packet.BlockEntityId,
                    packet.SurfaceIndex,
                    stopPlayback: true);
            }

            ApplyMediaPlayerCommand(block, player, packet);
            gridLogic.MarkRequested();
        }

        static void ApplyMediaPlayerCommand(IMyTerminalBlock block, GridMediaPlayer player, PacketSyncMediaPlayerCommand packet)
        {
            switch (packet.Command)
            {
                case MediaPlayerCommandKind.Play:
                    ApplyMediaPlayerPlayCommand(block, player, packet);
                    break;
                case MediaPlayerCommandKind.Pause:
                    if (player.CanSeek)
                        player.SeekTo(GetSyncedPosition(packet));
                    player.Pause();
                    break;
                case MediaPlayerCommandKind.Resume:
                    if (player.CanSeek)
                        player.SeekTo(GetSyncedPosition(packet));
                    player.Resume();
                    break;
                case MediaPlayerCommandKind.Stop:
                    player.ResetPlaybackEngine();
                    break;
                case MediaPlayerCommandKind.Seek:
                    player.SeekTo(GetSyncedPosition(packet));
                    break;
            }
        }

        static void ApplyMediaPlayerPlayCommand(IMyTerminalBlock block, GridMediaPlayer player, PacketSyncMediaPlayerCommand packet)
        {
            var startPosition = GetSyncedPosition(packet);
            switch (packet.SourceKind)
            {
                case MediaPlayerSourceKind.SoundSubtype:
                    player.PlayGameSound(block, packet.SourceId, false, startPosition);
                    break;
                case MediaPlayerSourceKind.ContentPath:
                    player.PlayGameAudioFile(block, packet.DisplayName, packet.SourceId, false, startPosition);
                    break;
            }
        }

        static double GetSyncedPosition(PacketSyncMediaPlayerCommand packet)
        {
            if (packet == null || double.IsNaN(packet.PositionSeconds) || double.IsInfinity(packet.PositionSeconds))
                return 0.0;

            var position = packet.PositionSeconds;
            if (packet.ServerFrame > 0 && MyAPIGateway.Session != null)
            {
                var frameDelta = MyAPIGateway.Session.GameplayFrameCounter - packet.ServerFrame;
                if (frameDelta > 0)
                    position += frameDelta / 60.0;
            }

            return position < 0.0 ? 0.0 : position;
        }

        void FactionStateChanged(MyFactionStateChange change, long faction1, long faction2, long player, long client)
        {
            if (change < MyFactionStateChange.FactionMemberSendJoin)
                return;

            FactionUpdated(faction1);
            FactionUpdated(faction2);
        }

        void FactionUpdated(long obj)
        {
            var affected = SurfaceScriptBase.Instances.Where(a => a.Faction != null && a.Faction.FactionId == obj)
                .ToList();

            var faction = MyAPIGateway.Session.Factions.TryGetFactionById(obj);

            if (faction != null)
                affected.AddRange(
                    SurfaceScriptBase.Instances.Where(a => a.Block != null &&
                                                           (faction.FounderId == a.Block.OwnerId ||
                                                            faction.Members.ContainsKey(a.Block.OwnerId))));

            affected.ForEach(a => a.UpdateFaction(faction));
        }

        void OnGuiControlRemoved(object obj)
        {
            if (obj.ToString().EndsWith("ScreenOptionsSpace"))
                LoadLocalization();
        }

        void EntityAdded(IMyEntity ent)
        {
            try
            {
                var grid = ent as IMyCubeGrid;
                if (grid != null)
                    return;

                PlanetHelper.OnEntityAdded(ent);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
        }

        void UpdateDebugSnapshot()
        {
            int trackedGrids = _session._grids.Count;
            int trackedLogic = LcdModSessionComponent.Components != null ? LcdModSessionComponent.Components.Count : 0;
            int refreshing = 0;
            int totalLastIterations = 0;
            int totalLastProcessed = 0;
            int totalNextBatch = 0;
            int logicCount = 0;

            if (LcdModSessionComponent.Components != null)
            {
                foreach (var pair in LcdModSessionComponent.Components)
                {
                    var logic = pair.Value;
                    if (logic == null)
                        continue;

                    logicCount++;
                    if (logic.IsRefreshRunning)
                        refreshing++;

                    totalLastIterations += logic.LastRefreshIterations;
                    totalLastProcessed += logic.LastRefreshProcessed;
                    totalNextBatch += logic.EstimatedNextRefreshBatchSize;
                }
            }

            int averageNextBatch = logicCount > 0 ? (int)Math.Round(totalNextBatch / (double)logicCount) : 0;
            var moduleLines = LcdModSessionComponent.GetModuleDebugLines().ToArray();
            LcdModSessionComponent.DebugSnapshot = new SessionDebugSnapshot(
                _session._updateTick,
                trackedGrids,
                trackedLogic,
                refreshing,
                totalLastIterations,
                totalLastProcessed,
                averageNextBatch,
                moduleLines);
        }

        void LoadLocalization()
        {
            var path = Path.Combine(_session.ModContext.ModPathData, "Localization");

            if (path.EndsWith("/Localization")) // workaround for linux-native
                path = path.Replace("\\",
                    "/"); // todo: remove after fix https://github.com/viktor-ferenczi/se-linux-compat

            var supportedLanguages = new HashSet<MyLanguagesEnum>();
            MyTexts.LoadSupportedLanguages(path, supportedLanguages);

            var configuredLanguage = MyAPIGateway.Session.Config.Language;
            var currentLanguage = supportedLanguages.Contains(configuredLanguage)
                ? configuredLanguage
                : MyLanguagesEnum.English;

            if (_session._loadedLanguage.HasValue && _session._loadedLanguage.Value == currentLanguage)
                return;

            _session._loadedLanguage = currentLanguage;

            var languageDescription = MyTexts.Languages
                .Where(x => x.Key == currentLanguage)
                .Select(x => x.Value)
                .FirstOrDefault();

            if (languageDescription == null)
                return;

            var cultureName = string.IsNullOrWhiteSpace(languageDescription.CultureName)
                ? null
                : languageDescription.CultureName;
            var subcultureName = string.IsNullOrWhiteSpace(languageDescription.SubcultureName)
                ? null
                : languageDescription.SubcultureName;

            MyTexts.LoadTexts(path, cultureName, subcultureName);
            LcdModSessionComponent.RaiseLanguageChanged();
        }

        public static void SetLocalPlayerUseInputBlocked(bool blocked) =>
            SetPlayerUseInputBlocked(MyAPIGateway.Session.LocalHumanPlayer.IdentityId, blocked);

        static void SetPlayerUseInputBlocked(long playerId, bool blocked)
        {
            bool enabled = !blocked;
            LcdModSessionComponent.ApplyPlayerUseInputEnabled(playerId, enabled);

            if (MyAPIGateway.Multiplayer.MultiplayerActive &&
                !MyAPIGateway.Multiplayer.IsServer &&
                LcdModSessionComponent.NetworkManager != null)
                LcdModSessionComponent.NetworkManager.TransmitToServer(
                    new PacketPlayerInputBlacklist(playerId, enabled), false);
        }
    }
}
