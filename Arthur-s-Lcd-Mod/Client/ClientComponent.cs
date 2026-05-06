using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Client.TerminalControls;
using LcdMod.Client.TerminalControls.Blueprint;
using LcdMod.Client.TerminalControls.Color;
using LcdMod.Client.TerminalControls.Filter;
using LcdMod.Client.TerminalControls.Filter.Buttons;
using LcdMod.Client.TerminalControls.Filter.Listbox;
using LcdMod.Client.TerminalControls.Generic;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using ItemsSurfaceScriptBase = LcdMod.Client.Apps.Abstract.ItemsSurfaceScriptBase;
using SliderCursorScale = LcdMod.Client.TerminalControls.Interactive.SliderCursorScale;
using SliderFontSize = LcdMod.Client.TerminalControls.Scale.SliderFontSize;
using SliderScale = LcdMod.Client.TerminalControls.Scale.SliderScale;
using SwitchToggleAlt = LcdMod.Client.TerminalControls.Interactive.SwitchToggleAlt;

namespace LcdMod.Client
{
    public sealed class LcdModClientComponent
    {
        public static readonly List<TerminalControlsWrapper> Controls = new List<TerminalControlsWrapper>();
        public static readonly List<Action> RunNextFrame = new List<Action>();
        static readonly List<Action> RunThisFrame = new List<Action>();

        readonly LcdModSessionComponent _session;

        public LcdModClientComponent(LcdModSessionComponent session)
        {
            _session = session;
        }

        public void LoadData()
        {
            _session.RegisterModules();

            var group = CommandManager.GetOrCreateGroup("/lcdMod", new CmdGroupInitializer(4));
            group.TryAdd("FactionColor", FactionHelper.SetColor);
            group.TryAdd("PreloadTextures", _ => BlockIconHelper.PreloadAllTextures());

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
            MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;
            MyAPIGateway.Gui.GuiControlRemoved += OnGuiControlRemoved;

            TerminalControlsListbox source = new ListboxBlockCandidates();
            TerminalControlsListbox target = new ListboxBlockSelected();

            Controls.Add(new SliderFontSize());
            Controls.Add(new SliderPadding());
            Controls.Add(new SliderFov());

            Controls.Add(new SwitchToggleColors());
            Controls.Add(new ColorPickerAccent());
            Controls.Add(new ColorPickerWarning());
            Controls.Add(new ColorPickerError());

            Controls.Add(new SwitchToggleHeader());
            Controls.Add(new SliderScale());
            Controls.Add(new SliderCursorScale());
            Controls.Add(new SwitchToggleAlt());
            Controls.Add(new SliderRotation());

            Controls.Add(new ComboboxDisplayMode());
            Controls.Add(new ComboboxGraphWindow());
            Controls.Add(new SliderOreScannerConeAngle());
            Controls.Add(new ListboxOreScannerReference());
            Controls.Add(new ListboxReferenceBlockSelection());
            Controls.Add(new SwitchToggleLines());

            Controls.Add(new ListboxProjectorSelection());
            Controls.Add(new CheckboxHideEmpty());
            Controls.Add(new SeparatorFilter());
            Controls.Add(new LabelSeparator());
            Controls.Add(source);
            Controls.Add(new ButtonBlockAddToSelection(source, target));
            Controls.Add(target);
            Controls.Add(new ButtonBlockRemoveFromSelection(source, target));

            source = new ListboxItemsCandidates();
            target = new ListboxItemsSelected();

            Controls.Add(source);
            Controls.Add(new ButtonItemAddToSelection(source, target));
            Controls.Add(target);
            Controls.Add(new ButtonItemRemoveFromSelection(source, target));

            Controls.Add(new ComboboxSorting());
        }

        public void UnloadData()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlGetter;
            MyAPIGateway.Gui.GuiControlRemoved -= OnGuiControlRemoved;
            MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;
            MyAPIGateway.Session.Factions.FactionCreated -= FactionUpdated;
            MyAPIGateway.Session.Factions.FactionEdited -= FactionUpdated;
            MyAPIGateway.Session.Factions.FactionStateChanged -= FactionStateChanged;

            Controls.Clear();
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

        void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (controls == null)
                return;

            try
            {
                SetupProviderTerminal(block, controls);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
        }

        void SetupProviderTerminal(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            var provider = block as IMyTextSurfaceProvider;
            if (provider == null)
                return;

            LcdModSessionComponent.LastSelectedBlock = block;

            if (provider is IMyTextPanel)
            {
                controls.AddRange(Controls.Select(control => control.TerminalControl));
            }
            else if (provider.SurfaceCount > 0)
            {
                var index = controls.FindIndex(p => p.Id == "Script") + 3;

                foreach (var control in Controls)
                {
                    controls.AddOrInsert(control.TerminalControl, index);
                    index++;
                }
            }
        }

        public static void SetLocalPlayerUseInputBlocked(bool blocked) => SetPlayerUseInputBlocked(MyAPIGateway.Session.LocalHumanPlayer.IdentityId, blocked);

        static void SetPlayerUseInputBlocked(long playerId, bool blocked)
        {
            bool enabled = !blocked;
            LcdModSessionComponent.ApplyPlayerUseInputEnabled(playerId, enabled);

            // Multiplayer server-side blacklist propagation is intentionally disabled for local-client testing.

            if (MyAPIGateway.Multiplayer.MultiplayerActive &&
                !MyAPIGateway.Multiplayer.IsServer &&
                ConfigManager.NetworkManager != null)
                ConfigManager.NetworkManager.TransmitToServer(new PacketPlayerInputBlacklist(playerId, enabled), false);
        }
    }
}
