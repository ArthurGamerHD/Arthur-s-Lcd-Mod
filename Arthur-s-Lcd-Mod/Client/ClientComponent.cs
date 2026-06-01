using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using ItemsSurfaceScriptBase = LcdMod.Client.SurfaceScripts.Abstract.ItemsSurfaceScriptBase;

namespace LcdMod.Client
{
    public sealed class LcdModClientComponent
    {
        public static readonly List<Action> RunNextFrame = new List<Action>();
        static readonly List<Action> RunThisFrame = new List<Action>();

        readonly LcdModSessionComponent _session;
        readonly TerminalManager _terminalManager;

        public LcdModClientComponent(LcdModSessionComponent session)
        {
            _session = session;
            _terminalManager = new TerminalManager(session);
        }

        public void LoadData()
        {
            LocalConfigManager.Load();
            _session.RegisterModules();

            var group = CommandManager.GetOrCreateGroup("/lcdMod", new CmdGroupInitializer(7));
            group.TryAdd("FactionColor", FactionHelper.SetColor);
            group.TryAdd("Advanced", LocalConfigManager.SetAdvancedTweakablesCommand, 1);
            group.TryAdd("RenderUserGeneratedTextures", LocalConfigManager.RenderUserGeneratedTextures);
            group.TryAdd("PreloadTextures", _ => TextureHelper.PreloadAllTextures());
            group.TryAdd("ClearCache", TextureHelper.ClearCacheCommand);
            group.TryAdd("ImportTextures", _ => TextureHelper.Import(true));
            group.TryAdd("RemoveLocalTexture", TextureHelper.RemoveLocalTexture);
            group.TryAdd("ImportLocalTexture", TextureHelper.ImportLocalTexture);
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
            MyAPIGateway.Gui.GuiControlRemoved += OnGuiControlRemoved;
            _terminalManager.Initialize();
        }

        public void UnloadData()
        {
            LocalConfigManager.Save();
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
            LcdModSessionComponent.ClearClientEvents();

            ListBoxItemHelper.PerTypeCache.Clear();
            //TextureHelper.UnloadTextureCache();
            RunNextFrame.Clear();
            RunThisFrame.Clear();
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

                _session.UpdateModules();
                UpdateDebugSnapshot();
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

        public void Simulate()
        {
            RunNextFrameActions();
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
        }

        public void HandleSyncConfig(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<NetworkPackageSyncScreenConfig>();
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
            if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
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
            TextureHelper.SaveRemoteTexture(packet, false);
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
