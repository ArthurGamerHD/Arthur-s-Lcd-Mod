using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client;
using LcdMod.Client.Config;
using LcdMod.Client.Grid;
using LcdMod.Client.ScreenAreas;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using LcdMod.Server;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.Simulation | MyUpdateOrder.AfterSimulation)]
    public partial class LcdModSessionComponent : MySessionComponentBase, IModuleManager
    {
        static LcdModSessionComponent _instance;
        internal readonly Dictionary<long, MyTuple<IMyCubeGrid, GridLogic>> _grids = new Dictionary<long, MyTuple<IMyCubeGrid, GridLogic>>();
        internal int _updateTick;
        internal MyLanguagesEnum? _loadedLanguage;
        public static LcdModClientComponent Client;
        public static LcdModServerComponent Server;

        public static NetworkManager NetworkManager = new NetworkManager(PORT);

        public static event Action OnSave;
        public static event Action OnLanguageChanged;
        public static event Action OnAfterSimulationUpdate;
        public static SessionDebugSnapshot DebugSnapshot = SessionDebugSnapshot.Empty;

        public static IMyTerminalBlock LastSelectedBlock;

        public static Dictionary<long, GridLogic> Components = new Dictionary<long, GridLogic>();

        internal static void RaiseLanguageChanged()
        {
            OnLanguageChanged?.Invoke();
        }

        internal static void RaiseAfterSimulationUpdate()
        {
            OnAfterSimulationUpdate?.Invoke();
        }

        internal static void ClearClientEvents()
        {
            OnLanguageChanged = null;
            OnAfterSimulationUpdate = null;
        }

        public override void Simulate() => Client?.Simulate();

        public static GridLogic GetOrCreateGridLogic(IMyCubeGrid grid)
        {
            if (grid == null || Components == null)
                return null;

            GridLogic logic;
            if (Components.TryGetValue(grid.EntityId, out logic))
            {
                logic.MarkRequested();
                return logic;
            }

            logic = new GridLogic(grid);
            logic.MarkRequested();
            Components[grid.EntityId] = logic;

            var session = _instance;
            if (session != null && !session._grids.ContainsKey(grid.EntityId))
            {
                session._grids[grid.EntityId] = new MyTuple<IMyCubeGrid, GridLogic>(grid, logic);
                grid.OnMarkForClose += session.GridMarkedForClose;
            }

            return logic;
        }

        public override void SaveData()
        {
            if (MyAPIGateway.Session.IsServer)
                return;

            OnSave?.Invoke();
            ConfigManager.SaveAll();
        }

        public override void LoadData()
        {
            LogHelper.LogInfo("Init - Version " + VersionName);
            
            _instance = this;
            if (Components == null)
                Components = new Dictionary<long, GridLogic>();

            if (MyAPIGateway.Session.IsServer)
            {
                Server = new LcdModServerComponent(this);
                Server.LoadData();
            }

            if (!IsDedicatedServer)
            {
                Client = new LcdModClientComponent(this);
                Client.LoadData();
            }
        }

        protected override void UnloadData()
        {
            Client?.UnloadData();
            Server?.UnloadData();
            Client = null;
            Server = null;
            _instance = null;
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
            Client?.UpdateBeforeSimulation();
        }

        public override void UpdateAfterSimulation()
        {
            Client?.UpdateAfterSimulation();
#if DEBUG
            if (Client != null && !MyAPIGateway.Utilities.IsDedicated)
            {
                if (LocalConfigManager.DebugSurface)
                    ScreenAreaGeometry.DebugDraw();
                else
                    ScreenAreaGeometry.ClearDebugDraw();
            }
#endif
        }

        public override void BeforeStart()
        {
            try
            {
                NetworkManager.OnReceivedPacket += OnReceivedPacket;
                
                NetworkManager.Init();

                Server?.BeforeStart();
                Client?.BeforeStart();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        void OnReceivedPacket(ReceivedPacketEventArgs args)
        {
            try
            {
                switch (args.Code)
                {
                    case PackageCode.SyncConfig:
                        if (!MyAPIGateway.Utilities.IsDedicated)
                            Client?.HandleSyncConfig(args);
                        if (!args.IsFromServer)
                            Server?.HandleSyncConfig(args);
                        break;
                    case PackageCode.RequestTexture:
                        if (args.IsFromServer)
                            Client?.HandleRequestTexture(args);
                        else
                            Server?.HandleRequestTexture(args);
                        break;
                    case PackageCode.SyncTexture:
                        if (args.IsFromServer)
                            Client?.HandleSyncTexture(args);
                        else
                            Server?.HandleSyncTexture(args);
                        break;
                    case PackageCode.EditFaction:
                        Server?.HandleEditFaction(args);
                        break;
                    case PackageCode.PlayerInputBlacklist:
                        HandlePlayerInputBlacklist(args);
                        break;
                    case PackageCode.SortInventory:
                        Server?.HandleSortInventory(args);
                        break;
                    case PackageCode.TransferItems:
                        Server?.HandleTransferItems(args);
                        break;
                    default:
                        {
                            LogHelper.Log(MyLogSeverity.Error, $"Unexpected Packet Code Received");
                            break;
                        }
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        public static void HandlePlayerInputBlacklist(ReceivedPacketEventArgs args)
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            var packet = args.UnWrap<PacketPlayerInputBlacklist>();
            ApplyPlayerUseInputEnabled(packet.PlayerId, packet.Enabled);
        }
        
        static readonly List<string> UseInputControlIds = new List<string>
        {
            MyControlsSpace.PRIMARY_TOOL_ACTION.String,
            MyControlsSpace.SECONDARY_TOOL_ACTION.String
        };

        public static void ApplyPlayerUseInputEnabled(long playerId, bool enabled)
        {
            // this "technically" is client side but needs to be done on the server too for some reason
            foreach (var controlStringId in UseInputControlIds)
                MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(controlStringId, playerId, enabled);
        }

        static bool IsDedicatedServer =>
            MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session.IsServer;

        internal static void ApplySyncedConfig(IMyFunctionalBlock block, ScreenProviderConfig settings, ScreenProviderConfig source)
        {
            settings.CopyFrom(source);
            settings.BindRuntimeParent(block);
            ConfigManager.Save(block, settings);

            foreach (var app in ConfigManager.GetAppsForBlock(block))
                app.UseProviderConfig(settings);

            foreach (var app in ConfigManager.GetAppsForBlock(block))
                app.RequestRedraw();
        }
    }

    public struct SessionDebugSnapshot
    {
        public static readonly SessionDebugSnapshot Empty = new SessionDebugSnapshot(0, 0, 0, 0, 0, 0, 0, new string[0]);

        public readonly int UpdateTick;
        public readonly int TrackedGrids;
        public readonly int TrackedGridLogic;
        public readonly int RefreshInProgress;
        public readonly int TotalLastRefreshIterations;
        public readonly int TotalLastRefreshProcessed;
        public readonly int AverageNextBatchSize;
        public readonly string[] ModuleLines;

        public SessionDebugSnapshot(
            int updateTick,
            int trackedGrids,
            int trackedGridLogic,
            int refreshInProgress,
            int totalLastRefreshIterations,
            int totalLastRefreshProcessed,
            int averageNextBatchSize,
            string[] moduleLines)
        {
            UpdateTick = updateTick;
            TrackedGrids = trackedGrids;
            TrackedGridLogic = trackedGridLogic;
            RefreshInProgress = refreshInProgress;
            TotalLastRefreshIterations = totalLastRefreshIterations;
            TotalLastRefreshProcessed = totalLastRefreshProcessed;
            AverageNextBatchSize = averageNextBatchSize;
            ModuleLines = moduleLines ?? new string[0];
        }
    }
}
