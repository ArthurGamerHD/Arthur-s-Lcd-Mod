using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Graph.Apps.Abstract;
using Graph.Helpers;
using Graph.Networking;
using Graph.System.Config;
using Graph.System.TerminalControls;
using Graph.System.TerminalControls.Blueprint;
using Graph.System.TerminalControls.Color;
using Graph.System.TerminalControls.Filter;
using Graph.System.TerminalControls.Filter.Buttons;
using Graph.System.TerminalControls.Filter.Listbox;
using Graph.System.TerminalControls.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Graph.System
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.AfterSimulation)]
    public class LcdModSessionComponent : MySessionComponentBase
    {
        readonly Dictionary<long, MyTuple<IMyCubeGrid, GridLogic>> _grids = new Dictionary<long, MyTuple<IMyCubeGrid, GridLogic>>();

        int _updateTick;
        MyLanguagesEnum? _loadedLanguage;
        public static event Action OnLanguageChanged;

        public static string LastSelected;
        public static IMyTerminalBlock LastSelectedBlock;
        
        public static Dictionary<long, GridLogic> Components = new Dictionary<long, GridLogic>();
        public static List<TerminalControlsWrapper> Controls = new List<TerminalControlsWrapper>();

        public override void LoadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session.IsServer)
                return;

            var group = CmdManager.GetOrCreateGroup("/lcdMod", new CmdGroupInitializer(4));
            group.TryAdd("FactionColor", FactionHelper.SetColor);

            DebuggerHelper.Break();
            MyAPIGateway.Entities.OnEntityAdd += EntityAdded;

            MyAPIGateway.Session.Factions.FactionCreated += FactionUpdated;
            MyAPIGateway.Session.Factions.FactionEdited += FactionUpdated;
            MyAPIGateway.Session.Factions.FactionStateChanged += FactionStateChanged;
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

        protected override void UnloadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session.IsServer)
                return;

            MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlGetter;
            Controls.Clear();
            MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;
            _grids.Clear();
            Components.Clear();
            Components = null;

            ItemsSurfaceScriptBase.SpriteCache?.Clear();
            ItemsSurfaceScriptBase.SpriteCache = null;
            OnLanguageChanged = null;

            ListBoxItemHelper.PerTypeCache.Clear();

            ConfigManager.Close();
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
                if (grid == null || _grids.ContainsKey(grid.EntityId))
                    return;

                var logic = new GridLogic(grid);
                _grids[grid.EntityId] = new MyTuple<IMyCubeGrid, GridLogic>(grid, logic);
                Components[grid.EntityId] = logic;
                grid.OnMarkForClose += GridMarkedForClose;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        void GridMarkedForClose(IMyEntity ent)
        {
            try
            {
                _grids.Remove(ent.EntityId);
                Components.Remove(ent.EntityId);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session.IsServer)
                return;

            _updateTick++;
            if (_updateTick % 6 != 0)
                return;

            try
            {
                foreach (var grid in _grids.Values)
                {
                    if (grid.Item1.MarkedForClose)
                        continue;

                    grid.Item2.Update();
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        public override void BeforeStart()
        {
            try
            {
                ConfigManager.Init();
                ConfigManager.NetworkManager.OnReceivedPacket += OnReceivedPacket;

                if (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session.IsServer)
                    return;

                LoadLocalization();
                MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;
                MyAPIGateway.Gui.GuiControlRemoved += OnGuiControlRemoved;

                TerminalControlsListbox source = new ListboxBlockCandidates();
                TerminalControlsListbox target = new ListboxBlockSelected();

                Controls.Add(new SliderFontSize());
                Controls.Add(new SliderPadding());
                
                Controls.Add(new SwitchToggleColors());
                Controls.Add(new ColorPickerAccent());
                Controls.Add(new ColorPickerWarning());
                Controls.Add(new ColorPickerError());
                
                Controls.Add(new SwitchToggleHeader());
                Controls.Add(new SliderScale());
                Controls.Add(new SliderRotation());

                Controls.Add(new ComboboxDisplayMode());
                Controls.Add(new ComboboxGraphWindow());
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
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        void LoadLocalization()
        {
            var path = Path.Combine(ModContext.ModPathData, "Localization");
            var supportedLanguages = new HashSet<MyLanguagesEnum>();
            MyTexts.LoadSupportedLanguages(path, supportedLanguages);

            var configuredLanguage = MyAPIGateway.Session.Config.Language;
            var currentLanguage = supportedLanguages.Contains(configuredLanguage)
                ? configuredLanguage
                : MyLanguagesEnum.English;

            if (_loadedLanguage.HasValue && _loadedLanguage.Value == currentLanguage)
                return;

            _loadedLanguage = currentLanguage;

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
            OnLanguageChanged?.Invoke();
        }

        void OnReceivedPacket(ReceivedPacketEventArgs args)
        {
            try
            {
                switch (args.Code)
                {
                    case PackageCode.SyncConfig:
                    {
                        var packet = args.UnWrap<NetworkPackageSyncScreenConfig>();
                        var block = MyEntities.GetEntityById(packet.BlockId) as IMyFunctionalBlock;

                        if (block == null)
                            return;

                        ScreenProviderConfig settings;
                        if (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session.IsServer)
                        {
                            if (block.Storage == null)
                                block.Storage = new MyModStorageComponent();
                            settings = ConfigManager.TryLoad(block) ?? ConfigManager.CreateSettings(block);
                        }
                        else
                        {
                            settings = SurfaceScriptBase.Instances.FirstOrDefault(a => a.Block.Equals(block))
                                ?.ProviderConfig;
                        }

                        if (settings == null)
                            return;

                        settings.CopyFrom(packet.Config);
                        ConfigManager.Save(block, settings);

                        foreach (var app in ConfigManager.GetAppsForBlock(block)) 
                            app.RequestRedraw();

                        break;
                    }
                    case PackageCode.EditFaction:
                    {
                        var packet = args.UnWrap<PacketEditFaction>();
                        var sender = MyAPIGateway.Players.TryGetIdentityId(args.SenderId);
                        var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(sender);

                        if (faction == null || packet.FactionId != faction.FactionId || !(faction.IsLeader(sender) || faction.IsFounder(sender)))
                        {
                            MyVisualScriptLogicProvider.SendChatMessageColored("Unable to edit faction", Color.Red, "Error", sender);
                            return;
                        }
                        
                        FactionHelper.EditFaction(packet);
                        break;
                    }
                    default:
                    {
                        MyLog.Default.Log(MyLogSeverity.Error, $"{nameof(Graph)}: Unexpected Packet Code Received");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
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
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        void SetupProviderTerminal(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            var provider = block as IMyTextSurfaceProvider;
            if (provider == null)
                return;

            LastSelectedBlock = block;
            LastSelected = provider.GetSurface(GetThisSurfaceIndex(block))?.Script ?? string.Empty;
            
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

        public int GetThisSurfaceIndex(IMyTerminalBlock block)
        {
            var multiTextPanel = block.Components.Get<MyMultiTextPanelComponent>();
            return multiTextPanel?.SelectedPanelIndex ?? 0;
        }
    }
}
