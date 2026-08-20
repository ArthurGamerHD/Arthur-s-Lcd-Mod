using System;
using System.Collections.Generic;
using System.IO;
using Generated;
using LcdMod.Client;
using LcdMod.Client.Config;
using LcdMod.Client.GridData;
#if DEBUG
using LcdMod.Client.ScreenAreas;
#endif
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using LcdMod.Server;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using VRage.ModAPI;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.Simulation | MyUpdateOrder.AfterSimulation)]
    public partial class LcdModSessionComponent : MySessionComponentBase, IModuleManager, ISingleton<LcdModSessionComponent>
    {
        static LcdModSessionComponent _instance;
        internal readonly Dictionary<long, MyTuple<IMyCubeGrid, GridLogic>> _grids = new Dictionary<long, MyTuple<IMyCubeGrid, GridLogic>>();
        internal int _updateTick;
        internal MyLanguagesEnum? _loadedLanguage;
        public static LcdModClientComponent Client;
        public static LcdModServerComponent Server;

        public static NetworkManager NetworkManager = new NetworkManager(new NetworkParameters(
            PORT,
            96 * 1024,
            32 * 1024 * 1024,
            384));

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
            if (grid == null || grid.Closed || grid.MarkedForClose || Components == null)
                return null;

            var gridId = grid.EntityId;
            GridLogic logic;
            if (Components.TryGetValue(gridId, out logic))
            {
                if (logic != null && logic.TargetGrid == gridId && logic.IsAlive)
                {
                    EnsureGridLogicTracked(grid, logic);
                    return logic;
                }

                if (logic != null)
                    logic.Unload();
            }

            logic = new GridLogic(grid);
            Components[gridId] = logic;
            EnsureGridLogicTracked(grid, logic);
            return logic;
        }

        static void EnsureGridLogicTracked(IMyCubeGrid grid, GridLogic logic)
        {
            var session = _instance;
            if (session == null)
                return;

            MyTuple<IMyCubeGrid, GridLogic> tracked;
            if (session._grids.TryGetValue(grid.EntityId, out tracked) && tracked.Item1 != null)
                tracked.Item1.OnMarkForClose -= session.GridMarkedForClose;

            session._grids[grid.EntityId] = new MyTuple<IMyCubeGrid, GridLogic>(grid, logic);
            grid.OnMarkForClose -= session.GridMarkedForClose;
            grid.OnMarkForClose += session.GridMarkedForClose;
        }

        public override void SaveData()
        {
            if (MyAPIGateway.Session.IsServer)
            {
                Server?.SaveData();
                return;
            }

            OnSave?.Invoke();
            ConfigManager.SaveAll();
        }

        public override void LoadData()
        {
            LogHelper.LogInfo("Init - Version " + VersionName);
            
            _instance = this;
            RegisterSingleton();
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
            if (Components != null)
            {
                foreach (var logic in Components.Values)
                {
                    if (logic != null)
                        logic.Unload();
                }
            }

            Client?.UnloadData();
            Server?.UnloadData();
            Client = null;
            Server = null;
            UnregisterSingleton();
            _instance = null;
        }

        void GridMarkedForClose(IMyEntity ent)
        {
            try
            {
                MyTuple<IMyCubeGrid, GridLogic> tracked;
                if (_grids.TryGetValue(ent.EntityId, out tracked) && ReferenceEquals(tracked.Item1, ent))
                {
                    if (tracked.Item1 != null)
                        tracked.Item1.OnMarkForClose -= GridMarkedForClose;
                    _grids.Remove(ent.EntityId);

                    GridLogic registered;
                    if (Components != null && Components.TryGetValue(ent.EntityId, out registered) &&
                        ReferenceEquals(registered, tracked.Item2))
                        Components.Remove(ent.EntityId);

                    if (tracked.Item2 != null)
                        tracked.Item2.Unload();
                }
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
                NetworkManager.Init();

                Server?.BeforeStart();
                Client?.BeforeStart();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        [NetworkCallback(typeof(PacketPlayerInputBlacklist), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandlePlayerInputBlacklist(PacketPlayerInputBlacklist packet)
        {
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

            foreach (var app in ConfigManager.GetAppsForBlock(block))
                app.UseProviderConfig(settings);

            ConfigManager.Save(block, settings);

            foreach (var app in ConfigManager.GetAppsForBlock(block))
                app.RequestRedraw();
        }
    }

    public struct SessionDebugSnapshot
    {
        public static readonly SessionDebugSnapshot Empty = new SessionDebugSnapshot(0, 0, 0, 0, 0, 0, 0, Array.Empty<string>());

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
            ModuleLines = moduleLines ?? Array.Empty<string>();
        }
    }
}
