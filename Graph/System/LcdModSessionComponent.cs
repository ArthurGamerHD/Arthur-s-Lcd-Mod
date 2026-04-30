using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Generated;
using Graph.Apps.Abstract;
using Graph.Helpers;
using Graph.Networking;
using Graph.System.ScreenAreas;
using Graph.System.Config;
using Graph.System.Config.Models;
using Graph.System.Config.Models.Apps;
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
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Graph.System
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.AfterSimulation)]
    public partial class LcdModSessionComponent : MySessionComponentBase, IModuleManager
    {
        static LcdModSessionComponent _instance;
        readonly Dictionary<long, MyTuple<IMyCubeGrid, GridLogic>> _grids = new Dictionary<long, MyTuple<IMyCubeGrid, GridLogic>>();
        int _updateTick;
        MyLanguagesEnum? _loadedLanguage;
        LcdModClientComponent Client;
        LcdModServerComponent Server;
        public static event Action OnLanguageChanged;
        public static event Action OnAfterSimulationUpdate;
        public static SessionDebugSnapshot DebugSnapshot = SessionDebugSnapshot.Empty;
        
        public static IMyTerminalBlock LastSelectedBlock;
        
        public static Dictionary<long, GridLogic> Components = new Dictionary<long, GridLogic>();
        public static List<TerminalControlsWrapper> Controls = new List<TerminalControlsWrapper>();

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

        public override void LoadData()
        {
            _instance = this;
            if (Components == null)
                Components = new Dictionary<long, GridLogic>();

            if (MyAPIGateway.Session.IsServer)
                Server = new LcdModServerComponent(this);

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

            ConfigManager.Close();
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
            ScreenAreaGeometry.DebugDraw();
#endif
        }

        public override void BeforeStart()
        {
            try
            {
                ConfigManager.Init();
                ConfigManager.NetworkManager.OnReceivedPacket += OnReceivedPacket;

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
                        if (args.IsFromServer)
                            Client?.HandleSyncConfig(args);
                        else
                            Server?.HandleSyncConfig(args);
                        break;
                    case PackageCode.EditFaction:
                        Server?.HandleEditFaction(args);
                        break;
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

        public int GetThisSurfaceIndex(IMyTerminalBlock block)
        {
            var multiTextPanel = block.Components.Get<MyMultiTextPanelComponent>();
            return multiTextPanel?.SelectedPanelIndex ?? 0;
        }

        static bool IsDedicatedServer =>
            MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session.IsServer;

        static void ApplySyncedConfig(IMyFunctionalBlock block, ScreenProviderConfig settings, ScreenProviderConfig source)
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
