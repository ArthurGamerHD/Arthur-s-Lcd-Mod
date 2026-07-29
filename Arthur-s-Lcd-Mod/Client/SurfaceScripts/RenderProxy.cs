using System.Collections.Generic;
using System;
using System.Linq;
using Generated;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.ScreenAreas;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Proxy;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Models;
using LcdMod.Client.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(Apps.RenderProxyApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class RenderProxySurfaceScript : InteractiveSurfaceScript,
        IReferenceBlockSelection,
        IProxyAutoOffset
    {
        public const string ID = MOD_PREFIX + "RenderProxy";
        public const string TITLE = MOD_PREFIX + "RenderProxy";

        List<MySprite> _sprites = new List<MySprite>();
        readonly List<Control> _parentInteractiveEntries = new List<Control>();
        RenderProxyConfigComponent RenderProxyComponent => Config.GetComponent<RenderProxyConfigComponent>();
        BlockReferenceConfigComponent RenderProxyReferenceComponent =>
            Config.GetComponent<BlockReferenceConfigComponent>(RENDER_PROXY_REFERENCE);
        protected override bool ClipToBounds => true;

        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;


        static readonly List<IMySlimBlock> AutoCascadeBlocks = new List<IMySlimBlock>();
        static readonly HashSet<long> ActiveRotationCascadeHosts = new HashSet<long>();
        static SurfaceCollection _activeInstanceCollection;

        SurfaceScriptBase _parent;
        ISurfaceTssInstances _parentInstances;
        SurfaceScriptBase _registeredParent;
        SurfaceScriptBase _renderEventParent;
        long _registeredProxyKey;
        long _resolvedReferenceBlockId;
        int _resolvedHostRotationIndex = -1;
        float _lastOffsetX;
        float _lastOffsetY;
        bool _hostScriptUnsupported;
        bool _hostResolutionUnsupported;
        long _rebindBlockedUntilFrame = long.MinValue;
        long _lastObservedReferenceId;
        float _lastObservedOffsetX;
        float _lastObservedOffsetY;
        bool _hasObservedConfig;
        int _initialAutoAdjustAttempts;

        const int INITIAL_AUTO_ADJUST_MAX_ATTEMPTS = 10;

        public override IApp App
        {
            get
            {
                if (!IsParentAlive(_parent))
                    return null;

                try
                {
                    return _parent.App;
                }
                catch
                {
                    return null;
                }
            }
        }

        IApp AppInteractive => App;

        public override List<Control> InteractiveList
        {
            get
            {
                var parentInteractive = _parent as InteractiveSurfaceScript;
                if (IsParentAlive(_parent) && parentInteractive != null)
                {
                    _parentInteractiveEntries.Clear();
                    _parentInteractiveEntries.AddRange(parentInteractive.InteractiveEntries);
                    return _parentInteractiveEntries;
                }

                var appInteractive = AppInteractive;
                if (appInteractive != null)
                    return appInteractive.VisualChildren as List<Control>;

                var game = App as IGame;
                return game?.Interactive;
            }
        }

        public override Vector2 HitTestOffset => IsParentAlive(_parent) ? GetCurrentProxyOffset() : Vector2.Zero;

        public RenderProxySurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block,
            size)
        {
            EnsureActiveInstanceChangeSubscription();
        }

        public bool IsReferenceBlockCandidate(IMyTerminalBlock block) => IsBasicReferenceBlockCandidate(block);

        public bool TryGetReferenceBlockCandidates(List<IMyTerminalBlock> blocks) => false;

        bool IsBasicReferenceBlockCandidate(IMyTerminalBlock block)
        {
            if (!(block is IMyTextPanel) || block.MarkedForClose || block.Equals(Block))
                return false;

            return !ConfigManager.GetAppsForBlock(block).Any(a => a is RenderProxySurfaceScript);
        }

        bool HasMatchingResolution(IMyTerminalBlock block)
        {
            var panel = block as IMyTextPanel;
            if (panel == null)
                return false;

            return SameResolution(panel.TextureSize, Surface.TextureSize);
        }

        bool HasMatchingResolution(SurfaceScriptBase parent)
        {
            return parent != null && SameResolution(parent.TextureSize, Surface.TextureSize);
        }

        static bool SameResolution(Vector2 a, Vector2 b)
        {
            return Math.Abs(a.X - b.X) < 0.5f &&
                   Math.Abs(a.Y - b.Y) < 0.5f;
        }

        public bool CanApplyProxyAutoOffset()
        {
            sbyte x;
            sbyte y;
            ProxyAutoContext context;
            return TryCalculateAutoOffset(out x, out y, out context);
        }

        public void ApplyProxyAutoOffset()
        {
            TryApplyProxyAutoOffset(true);
        }

        bool TryApplyProxyAutoOffset(bool cascade)
        {
            sbyte x;
            sbyte y;
            ProxyAutoContext context;
            if (!TryCalculateAutoOffset(out x, out y, out context))
                return false;

            if (RenderProxyComponent.XAxisOffset != x || RenderProxyComponent.YAxisOffset != y)
            {
                RenderProxyComponent.XAxisOffset = x;
                RenderProxyComponent.YAxisOffset = y;

                var terminalBlock = Block as IMyTerminalBlock;
                if (terminalBlock != null)
                    ConfigManager.Sync(terminalBlock);

                if (MyAPIGateway.Session != null)
                    _rebindBlockedUntilFrame = MyAPIGateway.Session.GameplayFrameCounter + 1;

                RenderSprites();
            }

            if (cascade)
                StartProxyAutoCascade(context, x, y);

            return true;
        }

        static void EnsureActiveInstanceChangeSubscription()
        {
            var collection = Instances;
            if (ReferenceEquals(_activeInstanceCollection, collection))
                return;

            if (_activeInstanceCollection != null)
                _activeInstanceCollection.ActiveInstanceChanged -= HandleActiveInstanceChanged;

            _activeInstanceCollection = collection;

            if (_activeInstanceCollection != null)
                _activeInstanceCollection.ActiveInstanceChanged += HandleActiveInstanceChanged;
        }

        static void HandleActiveInstanceChanged(SurfaceScriptBase activeInstance)
        {
            var proxy = activeInstance as RenderProxySurfaceScript;
            if (proxy == null)
                return;

            proxy.HandleBecameActiveInstance();
        }

        void HandleBecameActiveInstance()
        {
            _initialAutoAdjustAttempts = 0;
            ScheduleInitialAutoAdjust();
        }

        void ScheduleInitialAutoAdjust()
        {
            if (!RenderProxyComponent.EnableAutoAdjust)
                return;

            if (_initialAutoAdjustAttempts >= INITIAL_AUTO_ADJUST_MAX_ATTEMPTS)
                return;

            _initialAutoAdjustAttempts++;
            LcdModClientComponent.RunNextFrame.Add(TryInitialAutoAdjust);
        }

        void TryInitialAutoAdjust()
        {
            if (Block == null || Block.MarkedForClose)
                return;

            if (!RenderProxyComponent.EnableAutoAdjust)
                return;

            if (!TryApplyProxyAutoOffset(false))
                ScheduleInitialAutoAdjust();
        }

        bool TryCalculateAutoOffset(out sbyte x, out sbyte y, out ProxyAutoContext context)
        {
            x = 0;
            y = 0;
            return TryGetAutoContext(out context) &&
                   TryCalculateAutoOffsetFor(Block as IMyTextPanel, context, out x, out y);
        }

        bool TryGetAutoContext(out ProxyAutoContext context)
        {
            context = null;

            IMyTerminalBlock hostBlock;
            int hostRotationIndex;
            SurfaceScriptBase host;
            if (!TryGetConfiguredHost(out hostBlock, out hostRotationIndex, out host))
                return false;

            if (hostBlock.BlockDefinition.SubtypeName != Block.BlockDefinition.SubtypeName)
                return false;

            if (Block.CubeGrid == null || hostBlock.CubeGrid == null || Block.CubeGrid != hostBlock.CubeGrid)
                return false;

            Vector3I hostRight;
            Vector3I hostUp;
            Vector3I hostForward;
            Vector3D hostCenter;
            double hostStepX;
            double hostStepY;
            if (!TryGetScreenGridFrame(hostBlock, hostRotationIndex, out hostRight, out hostUp, out hostForward,
                    out hostCenter, out hostStepX, out hostStepY))
                return false;

            context = new ProxyAutoContext
            {
                HostId = hostBlock.EntityId,
                HostRotationIndex = hostRotationIndex,
                Grid = Block.CubeGrid,
                ScreenSubtype = Block.BlockDefinition.SubtypeName,
                TextureSize = Surface.TextureSize,
                Right = hostRight,
                Up = hostUp,
                Forward = hostForward,
                HostCenter = hostCenter,
                StepX = hostStepX,
                StepY = hostStepY
            };
            return true;
        }

        bool TryCalculateAutoOffsetFor(IMyTextPanel panel, ProxyAutoContext context, out sbyte x, out sbyte y)
        {
            x = 0;
            y = 0;

            if (!IsAutoCascadeCandidatePanel(panel, context))
                return false;

            Vector3I panelRight;
            Vector3I panelUp;
            Vector3I panelForward;
            Vector3D panelCenter;
            double panelStepX;
            double panelStepY;
            if (!TryGetScreenGridFrame(panel, context.HostRotationIndex, out panelRight, out panelUp,
                    out panelForward, out panelCenter, out panelStepX, out panelStepY))
                return false;

            return TryCalculateOffsetFromPanelFrame(
                panelRight,
                panelUp,
                panelForward,
                panelCenter,
                panelStepX,
                panelStepY,
                context,
                out x,
                out y);
        }

        static bool TryCalculateOffsetFromPanelFrame(
            Vector3I panelRight,
            Vector3I panelUp,
            Vector3I panelForward,
            Vector3D panelCenter,
            double panelStepX,
            double panelStepY,
            ProxyAutoContext context,
            out sbyte x,
            out sbyte y)
        {
            x = 0;
            y = 0;

            if (context == null)
                return false;

            if (panelRight != context.Right || panelUp != context.Up || panelForward != context.Forward)
                return false;

            if (context.StepX <= 0.05d || context.StepY <= 0.05d || panelStepX <= 0.05d || panelStepY <= 0.05d)
                return false;

            if (Math.Abs(panelStepX - context.StepX) > 0.05d || Math.Abs(panelStepY - context.StepY) > 0.05d)
                return false;

            var delta = panelCenter - context.HostCenter;
            double planeOffset = Dot(delta, context.Forward);
            if (Math.Abs(planeOffset) > 0.05d)
                return false;

            double xScreens = Dot(delta, context.Right) / context.StepX;
            double yScreens = Dot(delta, -context.Up) / context.StepY;

            int xInt;
            int yInt;
            if (!TryRoundCellOffset(xScreens, out xInt) ||
                !TryRoundCellOffset(yScreens, out yInt))
                return false;

            if (xInt < sbyte.MinValue || xInt > sbyte.MaxValue ||
                yInt < sbyte.MinValue || yInt > sbyte.MaxValue)
                return false;

            x = (sbyte)xInt;
            y = (sbyte)yInt;
            return true;
        }

        void StartProxyAutoCascade(ProxyAutoContext context, sbyte startX, sbyte startY)
        {
            if (context == null || context.Grid == null)
                return;

            var targets = CollectProxyAutoCascadeTargets(context, startX, startY);
            if (targets.Count == 0)
                return;

            ScheduleProxyAutoCascade(targets, 0);
        }

        List<ProxyAutoTarget> CollectProxyAutoCascadeTargets(ProxyAutoContext context, int startX, int startY)
        {
            var targets = new List<ProxyAutoTarget>();
            AutoCascadeBlocks.Clear();
            context.Grid.GetBlocks(AutoCascadeBlocks);

            int minX = Math.Min(0, startX);
            int maxX = Math.Max(0, startX);
            int minY = Math.Min(0, startY);
            int maxY = Math.Max(0, startY);

            for (int i = 0; i < AutoCascadeBlocks.Count; i++)
            {
                var panel = AutoCascadeBlocks[i].FatBlock as IMyTextPanel;
                if (panel == null ||
                    panel.EntityId == Block.EntityId ||
                    panel.EntityId == context.HostId ||
                    !IsAutoCascadeEditable(panel))
                {
                    continue;
                }

                sbyte x;
                sbyte y;
                if (!TryCalculateAutoOffsetFor(panel, context, out x, out y))
                    continue;

                if (x == 0 && y == 0)
                    continue;

                if (x < minX || x > maxX || y < minY || y > maxY)
                    continue;

                targets.Add(new ProxyAutoTarget(
                    panel,
                    x,
                    y,
                    context.HostId,
                    Math.Abs(x - startX) + Math.Abs(y - startY),
                    context.HostRotationIndex));
            }

            AutoCascadeBlocks.Clear();
            targets.Sort((a, b) =>
            {
                int distance = a.DistanceFromStart.CompareTo(b.DistanceFromStart);
                if (distance != 0)
                    return distance;

                return a.Block.EntityId.CompareTo(b.Block.EntityId);
            });
            return targets;
        }

        static void ScheduleProxyAutoCascade(List<ProxyAutoTarget> targets, int index)
        {
            if (targets == null || index >= targets.Count)
                return;

            LcdModClientComponent.RunNextFrame.Add(() =>
            {
                ApplyProxyAutoCascadeTarget(targets[index]);
                ScheduleProxyAutoCascade(targets, index + 1);
            });
        }

        static void ApplyProxyAutoCascadeTarget(ProxyAutoTarget target)
        {
            if (target == null || !IsAutoCascadeEditable(target.Block))
                return;

            ConfigureProxyPanel(target.Block, target.X, target.Y, target.HostId, target.RotationIndex);
        }

        static void ConfigureProxyPanel(IMyTextPanel panel, sbyte x, sbyte y, long hostId, int hostRotationIndex)
        {
            if (panel == null)
                return;

            var providerConfig = ConfigManager.GetConfigForBlock(panel) ??
                                 ConfigManager.TryLoad(panel) ??
                                 ConfigManager.CreateSettings(panel);
            if (!providerConfig.CanWrite)
                return;

            providerConfig.EnsureSurfaceApp(0, AppType.RenderProxy);
            var surface = providerConfig.GetSurfaceConfig(0);
            if (!providerConfig.CanWriteConfig(surface))
                return;
            var reference = surface?.TryGet<BlockReferenceConfigComponent>(RENDER_PROXY_REFERENCE);
            var proxyConfig = surface?.TryGetComponent<RenderProxyConfigComponent>();
            if (reference == null || proxyConfig == null)
                return;

            reference.EntityId = hostId;
            proxyConfig.XAxisOffset = x;
            proxyConfig.YAxisOffset = y;

            ConfigManager.Sync(panel, providerConfig);

            bool rotationChanged = SetSelectedTextPanelRotationIndex(panel, hostRotationIndex);
            EnsureProxyScript(panel, rotationChanged);
        }

        static bool IsAutoCascadeEditable(IMyTextPanel panel)
        {
            if (panel == null || panel.MarkedForClose)
                return false;

            if (panel.ContentType == ContentType.NONE)
                return true;

            var script = panel.Script;
            return string.IsNullOrWhiteSpace(script) ||
                   string.Equals(script, "none", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(script, ID, StringComparison.Ordinal);
        }

        static bool IsAutoCascadeCandidatePanel(IMyTextPanel panel, ProxyAutoContext context)
        {
            return panel != null &&
                   context != null &&
                   !panel.MarkedForClose &&
                   panel.CubeGrid == context.Grid &&
                   panel.BlockDefinition.SubtypeName == context.ScreenSubtype &&
                   SameResolution(panel.TextureSize, context.TextureSize);
        }

        static void ScheduleLinkedProxyRotationUpdate(IMyTerminalBlock hostBlock, int hostRotationIndex)
        {
            var hostPanel = hostBlock as IMyTextPanel;
            if (hostPanel == null || hostPanel.CubeGrid == null)
                return;

            ProxyAutoContext context;
            if (!TryCreateProxyAutoContext(hostPanel, hostRotationIndex, out context))
                return;

            long scheduleKey = (hostPanel.EntityId * 397L) ^ hostRotationIndex;
            if (ActiveRotationCascadeHosts.Contains(scheduleKey))
                return;

            var targets = CollectLinkedProxyRotationTargets(context);
            if (targets.Count == 0)
                return;

            ActiveRotationCascadeHosts.Add(scheduleKey);
            ScheduleLinkedProxyRotationTargets(targets, 0, scheduleKey);
        }

        static bool TryCreateProxyAutoContext(IMyTextPanel hostPanel, int hostRotationIndex,
            out ProxyAutoContext context)
        {
            context = null;
            if (hostPanel == null)
                return false;

            Vector3I hostRight;
            Vector3I hostUp;
            Vector3I hostForward;
            Vector3D hostCenter;
            double hostStepX;
            double hostStepY;
            if (!TryGetScreenGridFrame(hostPanel, hostRotationIndex, out hostRight, out hostUp, out hostForward,
                    out hostCenter, out hostStepX, out hostStepY))
                return false;

            context = new ProxyAutoContext
            {
                HostId = hostPanel.EntityId,
                HostRotationIndex = hostRotationIndex,
                Grid = hostPanel.CubeGrid,
                ScreenSubtype = hostPanel.BlockDefinition.SubtypeName,
                TextureSize = hostPanel.TextureSize,
                Right = hostRight,
                Up = hostUp,
                Forward = hostForward,
                HostCenter = hostCenter,
                StepX = hostStepX,
                StepY = hostStepY
            };
            return true;
        }

        static List<ProxyAutoTarget> CollectLinkedProxyRotationTargets(ProxyAutoContext context)
        {
            var targets = new List<ProxyAutoTarget>();
            if (context == null || context.Grid == null)
                return targets;

            AutoCascadeBlocks.Clear();
            context.Grid.GetBlocks(AutoCascadeBlocks);

            for (int i = 0; i < AutoCascadeBlocks.Count; i++)
            {
                var panel = AutoCascadeBlocks[i].FatBlock as IMyTextPanel;
                if (panel == null ||
                    panel.EntityId == context.HostId)
                {
                    continue;
                }

                ScreenProviderConfig providerConfig;
                RenderProxyConfigComponent proxyConfig;
                BlockReferenceConfigComponent proxyReference;
                if (!TryGetLinkedProxyConfig(panel, out providerConfig, out proxyConfig, out proxyReference) ||
                    proxyReference.EntityId != context.HostId)
                {
                    continue;
                }

                if (!proxyConfig.EnableAutoAdjust)
                    continue;

                if (GetSelectedTextPanelRotationIndex(panel) == context.HostRotationIndex)
                    continue;

                int distance = Math.Abs(proxyConfig.XAxisOffset) + Math.Abs(proxyConfig.YAxisOffset);
                targets.Add(new ProxyAutoTarget(
                    panel,
                    proxyConfig.XAxisOffset,
                    proxyConfig.YAxisOffset,
                    context.HostId,
                    distance,
                    context.HostRotationIndex));
            }

            AutoCascadeBlocks.Clear();
            targets.Sort((a, b) =>
            {
                int distance = a.DistanceFromStart.CompareTo(b.DistanceFromStart);
                if (distance != 0)
                    return distance;

                return a.Block.EntityId.CompareTo(b.Block.EntityId);
            });
            return targets;
        }

        static bool TryGetLinkedProxyConfig(
            IMyTextPanel panel,
            out ScreenProviderConfig providerConfig,
            out RenderProxyConfigComponent proxyConfig,
            out BlockReferenceConfigComponent proxyReference)
        {
            providerConfig = null;
            proxyConfig = null;
            proxyReference = null;

            if (panel == null)
                return false;

            providerConfig = ConfigManager.GetConfigForBlock(panel) ?? ConfigManager.TryLoad(panel);
            if (providerConfig == null)
                return false;

            var surface = providerConfig.GetSurfaceConfig(0);
            if (surface == null || surface.AppTypeId != (int)AppType.RenderProxy)
                return false;

            proxyConfig = surface.TryGetComponent<RenderProxyConfigComponent>();
            proxyReference = surface.TryGet<BlockReferenceConfigComponent>(RENDER_PROXY_REFERENCE);
            return proxyConfig != null && proxyReference != null;
        }

        static void ScheduleLinkedProxyRotationTargets(List<ProxyAutoTarget> targets, int index, long scheduleKey)
        {
            if (targets == null || index >= targets.Count)
            {
                ActiveRotationCascadeHosts.Remove(scheduleKey);
                return;
            }

            LcdModClientComponent.RunNextFrame.Add(() =>
            {
                ApplyLinkedProxyRotationTarget(targets[index]);
                ScheduleLinkedProxyRotationTargets(targets, index + 1, scheduleKey);
            });
        }

        static void ApplyLinkedProxyRotationTarget(ProxyAutoTarget target)
        {
            if (target == null || target.Block == null || target.Block.MarkedForClose)
                return;

            ScreenProviderConfig providerConfig;
            RenderProxyConfigComponent proxyConfig;
            BlockReferenceConfigComponent proxyReference;
            if (!TryGetLinkedProxyConfig(target.Block, out providerConfig, out proxyConfig, out proxyReference) ||
                proxyReference.EntityId != target.HostId)
            {
                return;
            }

            if (!proxyConfig.EnableAutoAdjust)
                return;

            bool rotationChanged = SetSelectedTextPanelRotationIndex(target.Block, target.RotationIndex);
            EnsureProxyScript(target.Block, rotationChanged);
        }

        static void EnsureProxyScript(IMyTextPanel panel, bool defer)
        {
            if (panel == null)
                return;

            if (defer)
            {
                LcdModClientComponent.RunNextFrame.Add(() => EnsureProxyScript(panel, false));
                return;
            }

            if (panel.MarkedForClose)
                return;

            if (panel.ContentType != ContentType.SCRIPT)
                panel.ContentType = ContentType.SCRIPT;

            if (!string.Equals(panel.Script, ID, StringComparison.Ordinal))
                panel.Script = ID;
        }

        bool TryGetConfiguredHost(
            out IMyTerminalBlock hostBlock,
            out int hostRotationIndex,
            out SurfaceScriptBase host)
        {
            hostBlock = null;
            hostRotationIndex = -1;
            host = null;

            if (RenderProxyReferenceComponent.EntityId == 0L)
                return false;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(RenderProxyReferenceComponent.EntityId, out entity))
                return false;

            var targetBlock = entity as IMyTerminalBlock;
            if (targetBlock == null ||
                targetBlock.MarkedForClose ||
                !IsBasicReferenceBlockCandidate(targetBlock))
            {
                return false;
            }

            hostBlock = targetBlock;
            hostRotationIndex = GetSelectedTextPanelRotationIndex(targetBlock);
            host = GetActiveParentInstance(Instances.GetInstances(targetBlock), hostRotationIndex);
            if (host == null)
                host = null;

            return host != null;
        }

        static bool TryGetScreenGridFrame(
            IMyCubeBlock block,
            int surfaceIndex,
            out Vector3I right,
            out Vector3I up,
            out Vector3I forward,
            out Vector3D center,
            out double stepX,
            out double stepY)
        {
            right = Vector3I.Zero;
            up = Vector3I.Zero;
            forward = Vector3I.Zero;
            center = Vector3D.Zero;
            stepX = 1d;
            stepY = 1d;

            if (block == null || block.CubeGrid == null)
                return false;

            MatrixD screenWorld;
            if (!ScreenAreaGeometry.TryGetScreenWorldMatrix(block, surfaceIndex, out screenWorld))
                return false;

            var inverseGrid = MatrixD.Invert(block.CubeGrid.WorldMatrix);
            center = Vector3D.Transform(screenWorld.Translation, inverseGrid) / block.CubeGrid.GridSize;

            if (!TryGetGridAxis(Vector3D.TransformNormal(screenWorld.Right, inverseGrid), out right) ||
                !TryGetGridAxis(Vector3D.TransformNormal(screenWorld.Up, inverseGrid), out up) ||
                !TryGetGridAxis(Vector3D.TransformNormal(screenWorld.Forward, inverseGrid), out forward))
            {
                return false;
            }

            stepX = GetBlockSpanInGridCells(block, right);
            stepY = GetBlockSpanInGridCells(block, up);

            return stepX > 0.05d && stepY > 0.05d;
        }

        static double GetBlockSpanInGridCells(IMyCubeBlock block, Vector3I axis)
        {
            var slim = block?.SlimBlock;
            if (slim == null)
                return 1d;

            var min = slim.Min;
            var max = slim.Max;

            if (axis.X != 0)
                return Math.Abs(max.X - min.X) + 1d;
            if (axis.Y != 0)
                return Math.Abs(max.Y - min.Y) + 1d;
            if (axis.Z != 0)
                return Math.Abs(max.Z - min.Z) + 1d;

            return 1d;
        }

        static bool TryGetGridAxis(Vector3D vector, out Vector3I axis)
        {
            axis = Vector3I.Zero;
            if (vector.LengthSquared() <= 1e-12d)
                return false;

            vector.Normalize();
            double ax = Math.Abs(vector.X);
            double ay = Math.Abs(vector.Y);
            double az = Math.Abs(vector.Z);

            if (ax >= ay && ax >= az)
                axis = new Vector3I(vector.X >= 0d ? 1 : -1, 0, 0);
            else if (ay >= ax && ay >= az)
                axis = new Vector3I(0, vector.Y >= 0d ? 1 : -1, 0);
            else
                axis = new Vector3I(0, 0, vector.Z >= 0d ? 1 : -1);

            return Math.Abs(Dot(vector, axis)) > 0.999d;
        }

        static int GetSelectedTextPanelRotationIndex(IMyCubeBlock block)
        {
            var panel = block as IMyTextPanel;
            if (panel == null)
                return 0;

            foreach (var component in panel.Components)
            {
                var lcdSurfaceComponent = component as IMyLcdSurfaceComponent;
                if (lcdSurfaceComponent == null)
                    continue;

                return lcdSurfaceComponent.SelectedRotationIndex;
            }

            return 0;
        }

        static bool SetSelectedTextPanelRotationIndex(IMyTextPanel panel, int rotationIndex)
        {
            if (panel == null)
                return false;

            var property = panel.GetProperty("Rotate") as ITerminalProperty<float>;
            if (property == null)
                return false;

            rotationIndex = ((rotationIndex % 4) + 4) % 4;
            float degrees = rotationIndex * 90f;
            if (NormalizeRotationIndex(property.GetValue(panel)) == rotationIndex)
                return false;

            property.SetValue(panel, degrees);
            return true;
        }

        static int NormalizeRotationIndex(float degrees)
        {
            int index = (int)Math.Round(degrees / 90f) % 4;
            return index < 0 ? index + 4 : index;
        }

        static bool TryRoundCellOffset(double value, out int rounded)
        {
            rounded = (int)Math.Round(value);
            return Math.Abs(value - rounded) <= 0.05d;
        }

        static double Dot(Vector3D vector, Vector3I axis)
        {
            return vector.X * axis.X + vector.Y * axis.Y + vector.Z * axis.Z;
        }

        sealed class ProxyAutoContext
        {
            public long HostId;
            public int HostRotationIndex;
            public IMyCubeGrid Grid;
            public string ScreenSubtype;
            public Vector2 TextureSize;
            public Vector3I Right;
            public Vector3I Up;
            public Vector3I Forward;
            public Vector3D HostCenter;
            public double StepX;
            public double StepY;
        }

        sealed class ProxyAutoTarget
        {
            public readonly IMyTextPanel Block;
            public readonly sbyte X;
            public readonly sbyte Y;
            public readonly long HostId;
            public readonly int DistanceFromStart;
            public readonly int RotationIndex;

            public ProxyAutoTarget(
                IMyTextPanel block,
                sbyte x,
                sbyte y,
                long hostId,
                int distanceFromStart,
                int rotationIndex)
            {
                Block = block;
                X = x;
                Y = y;
                HostId = hostId;
                DistanceFromStart = distanceFromStart;
                RotationIndex = rotationIndex;
            }
        }

        bool IsParentAlive(SurfaceScriptBase parent)
        {
            return parent?.Block != null && !parent.Block.MarkedForClose;
        }

        SurfaceScriptBase GetActiveParentInstance(int hostRotationIndex)
        {
            return GetActiveParentInstance(_parentInstances, hostRotationIndex);
        }

        static SurfaceScriptBase GetActiveParentInstance(ISurfaceTssInstances parentInstances, int hostRotationIndex)
        {
            if (parentInstances == null)
                return null;

            var parent = parentInstances.GetInstance(hostRotationIndex);
            if (parent is RenderProxySurfaceScript)
                return null;

            return parent;
        }

        bool IsResolvedParentCurrent(
            SurfaceScriptBase parent,
            int hostRotationIndex)
        {
            return parent != null && ReferenceEquals(parent, GetActiveParentInstance(hostRotationIndex));
        }

        void InvalidateParentReference(SurfaceScriptBase parent)
        {
            if (parent == null)
                return;

            if (_renderEventParent == parent)
                UnsubscribeFromParentRender();

            if (_registeredParent == parent)
            {
                try
                {
                    _registeredParent.UnregisterProxy(_registeredProxyKey);
                }
                catch
                {
                    // The host may already be halfway through disposal.
                }

                _registeredParent = null;
                _registeredProxyKey = 0L;
            }

            if (_parent == parent)
                _parent = null;

            _resolvedHostRotationIndex = -1;
            _hostScriptUnsupported = false;

            if (MyAPIGateway.Session != null)
                _rebindBlockedUntilFrame = MyAPIGateway.Session.GameplayFrameCounter + 1;
        }

        bool TryGetParentInteractive(out InteractiveSurfaceScript parentInteractive)
        {
            ResolveParentFromConfig();

            var parent = _parent;
            parentInteractive = parent as InteractiveSurfaceScript;
            if (IsParentAlive(parent) && parentInteractive != null)
                return true;

            InvalidateParentReference(parent);
            parentInteractive = null;
            return false;
        }

        bool ShouldRefreshLinkedProxyMapping(IMyTerminalBlock hostBlock, int hostRotationIndex)
        {
            var panel = Block as IMyTextPanel;
            var hostPanel = hostBlock as IMyTextPanel;
            if (panel == null || hostPanel == null)
                return false;

            return RenderProxyComponent.EnableAutoAdjust && GetSelectedTextPanelRotationIndex(panel) != hostRotationIndex;
        }

        long GetProxyRegistrationId()
        {
            unchecked
            {
                var rotationOrSurfaceIndex = Block is IMyTextPanel
                    ? GetSelectedTextPanelRotationIndex(Block)
                    : RotationOrSurfaceIndex;
                return (Block.EntityId * 397L) ^ rotationOrSurfaceIndex;
            }
        }

        Vector2 GetCurrentProxyOffset()
        {
            float xOffset = RenderProxyComponent.XAxisOffset;
            float yOffset = RenderProxyComponent.YAxisOffset;
            return GetProxyPixelOffset(xOffset, yOffset);
        }

        Vector2 GetProxyPixelOffset(float xOffset, float yOffset)
        {
            var visibleRect = GetVisibleScreenRect();

            // Proxy offsets are logical screen-page offsets.
            // Cropped LCDs center the visible screen inside TextureSize,
            // (Yes, I'm talking to YOU "Sci-Fi LCD Panel 5x3" !!!!!!!!)
            // so one logical step must be one visible screen page, not one full texture page.
            // ex: 512x512 texture but 512x310 size, Y starts at 101 and ends at 411
            // so the step must be y:0 map to y:101, y:310 maps to y:411, skip 202px, then y:311 maps to y:613
            var hostSourceOrigin = new Vector2(
                visibleRect.X + visibleRect.Width * xOffset,
                visibleRect.Y + visibleRect.Height * yOffset);

            var localVisibleOrigin = new Vector2(visibleRect.X, visibleRect.Y);
            return hostSourceOrigin - localVisibleOrigin;
        }

        RectangleF GetVisibleScreenRect()
        {
            var textureSize = Surface.TextureSize;
            var screenSize = Surface.SurfaceSize;

            if (screenSize.X <= 0f || screenSize.Y <= 0f)
                screenSize = textureSize;

            if (screenSize.X > textureSize.X)
                screenSize.X = textureSize.X;

            if (screenSize.Y > textureSize.Y)
                screenSize.Y = textureSize.Y;

            var origin = (textureSize - screenSize) / 2f;
            return new RectangleF(origin.X, origin.Y, screenSize.X, screenSize.Y);
        }

        void SyncProxyRegistration()
        {
            if (_registeredParent != null && _registeredParent != _parent)
            {
                _registeredParent.UnregisterProxy(_registeredProxyKey);
                _registeredParent = null;
                _registeredProxyKey = 0L;
                UnsubscribeFromParentRender();
            }

            if (!IsParentAlive(_parent))
            {
                UnsubscribeFromParentRender();
                return;
            }

            float x = RenderProxyComponent.XAxisOffset;
            float y = RenderProxyComponent.YAxisOffset;
            long proxyKey = GetProxyRegistrationId();

            if (_registeredParent == _parent &&
                _registeredProxyKey == proxyKey &&
                _lastOffsetX.Equals(x) &&
                _lastOffsetY.Equals(y))
            {
                SubscribeToParentRender(_parent);
                return;
            }

            if (_registeredParent == _parent && _registeredProxyKey != 0L && _registeredProxyKey != proxyKey)
                _registeredParent?.UnregisterProxy(_registeredProxyKey);

            if (!_parent.RegisterProxy(proxyKey,
                    GetProxyPixelOffset(x, y)))
            {
                _hostScriptUnsupported = false;
                UnsubscribeFromParentRender();
                if (MyAPIGateway.Session != null)
                    _rebindBlockedUntilFrame = MyAPIGateway.Session.GameplayFrameCounter + 1;
                return;
            }

            _registeredParent = _parent;
            _registeredProxyKey = proxyKey;
            _lastOffsetX = x;
            _lastOffsetY = y;
            SubscribeToParentRender(_parent);
        }

        void SubscribeToParentRender(SurfaceScriptBase parent)
        {
            if (_renderEventParent == parent)
                return;

            UnsubscribeFromParentRender();

            _renderEventParent = parent;
            if (_renderEventParent != null)
                _renderEventParent.OnRender += HandleParentRender;
        }

        void UnsubscribeFromParentRender()
        {
            if (_renderEventParent == null)
                return;

            _renderEventParent.OnRender -= HandleParentRender;
            _renderEventParent = null;
        }

        void HandleParentRender(SurfaceScriptBase parent)
        {
            if (parent == null ||
                parent != _registeredParent ||
                !ReferenceEquals(parent, GetActiveParentInstance(_resolvedHostRotationIndex)))
            {
                return;
            }

            RenderSprites(force: true);
        }

        void ResolveParentFromConfig()
        {
            if (MyAPIGateway.Session != null &&
                MyAPIGateway.Session.GameplayFrameCounter < _rebindBlockedUntilFrame)
            {
                _parent = null;
                return;
            }

            long referenceBlockId = RenderProxyReferenceComponent.EntityId;
            if (referenceBlockId == 0L)
            {
                _parent = null;
                _parentInstances = null;
                _resolvedReferenceBlockId = 0L;
                _resolvedHostRotationIndex = -1;
                _hostScriptUnsupported = false;
                _hostResolutionUnsupported = false;
                return;
            }

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(referenceBlockId, out entity))
            {
                _parent = null;
                _parentInstances = null;
                _resolvedReferenceBlockId = referenceBlockId;
                _resolvedHostRotationIndex = -1;
                _hostScriptUnsupported = false;
                _hostResolutionUnsupported = false;
                return;
            }

            var targetBlock = entity as IMyTerminalBlock;
            if (targetBlock == null || targetBlock.MarkedForClose || !IsBasicReferenceBlockCandidate(targetBlock))
            {
                _parent = null;
                _parentInstances = null;
                _resolvedReferenceBlockId = referenceBlockId;
                _resolvedHostRotationIndex = -1;
                _hostScriptUnsupported = false;
                _hostResolutionUnsupported = false;
                return;
            }

            int hostRotationIndex = GetSelectedTextPanelRotationIndex(targetBlock);
            if (ShouldRefreshLinkedProxyMapping(targetBlock, hostRotationIndex))
                ScheduleLinkedProxyRotationUpdate(targetBlock, hostRotationIndex);

            if (_parentInstances == null || _resolvedReferenceBlockId != referenceBlockId)
                _parentInstances = Instances.GetInstances(targetBlock);

            if (_resolvedReferenceBlockId == referenceBlockId &&
                _resolvedHostRotationIndex == hostRotationIndex &&
                IsResolvedParentCurrent(_parent, hostRotationIndex))
            {
                if (!HasMatchingResolution(_parent))
                {
                    _parent = null;
                    _hostScriptUnsupported = false;
                    _hostResolutionUnsupported = true;
                    return;
                }

                _hostScriptUnsupported = false;
                _hostResolutionUnsupported = false;
                return;
            }

            _parent = null;
            _resolvedReferenceBlockId = referenceBlockId;
            _resolvedHostRotationIndex = hostRotationIndex;
            _parentInstances = Instances.GetInstances(targetBlock);
            _hostScriptUnsupported = false;
            _hostResolutionUnsupported = false;

            if (!HasMatchingResolution(targetBlock))
            {
                _hostResolutionUnsupported = true;
                return;
            }

            // Text panels keep one resident script per rotation; proxy to the host's
            // active rotation slot so proxy panels can use independent rotations.
            var scripts = ConfigManager.GetAppsForBlock(targetBlock).ToList();
            _parent = GetActiveParentInstance(hostRotationIndex);

            if (_parent != null)
            {
                _hostScriptUnsupported = false;
                return;
            }

            // Only mark unsupported when we can positively classify the host as unsupported.
            // During script swap/layout transitions there may be no live script for a frame.
            bool hasAnyNonProxyScript = scripts.Any(a => !(a is RenderProxySurfaceScript));
            _hostScriptUnsupported = !hasAnyNonProxyScript;
        }

        void UpdateRebindCooldown()
        {
            long referenceId = RenderProxyReferenceComponent.EntityId;
            float x = RenderProxyComponent.XAxisOffset;
            float y = RenderProxyComponent.YAxisOffset;

            bool changed = !_hasObservedConfig ||
                           _lastObservedReferenceId != referenceId ||
                           !_lastObservedOffsetX.Equals(x) ||
                           !_lastObservedOffsetY.Equals(y);
            if (!changed || MyAPIGateway.Session == null)
                return;

            _lastObservedReferenceId = referenceId;
            _lastObservedOffsetX = x;
            _lastObservedOffsetY = y;
            _hasObservedConfig = true;
            _rebindBlockedUntilFrame = MyAPIGateway.Session.GameplayFrameCounter + 1;
        }

        public override void SafeRun()
        {
            base.SafeRun();
            UpdateRebindCooldown();
            ResolveParentFromConfig();
            SyncProxyRegistration();

            if (IsParentAlive(_registeredParent) && _registeredParent == _parent)
                return;

            RenderSprites();
        }

        protected override bool RenderContinuouslyWhileLookedAt => false;

        protected override void OnLookAt(Vector2 onScreenCoordinates)
        {
            InteractiveSurfaceScript parentInteractive;
            if (TryGetParentInteractive(out parentInteractive))
            {
                try
                {
                    parentInteractive.LookAt(onScreenCoordinates + HitTestOffset);
                    return;
                }
                catch
                {
                    InvalidateParentReference(parentInteractive);
                }
            }

            base.OnLookAt(onScreenCoordinates);
        }

        public override bool HasTooltipInputAtCursor(bool rightClick)
        {
            InteractiveSurfaceScript parentInteractive;
            if (TryGetParentInteractive(out parentInteractive))
                return parentInteractive.HasTooltipInputAtCursor(rightClick);

            return base.HasTooltipInputAtCursor(rightClick);
        }

        public override bool TryHandleTooltipActivationClick(bool rightClick, out ControlTemplate tooltipParent)
        {
            InteractiveSurfaceScript parentInteractive;
            if (TryGetParentInteractive(out parentInteractive))
                return parentInteractive.TryHandleTooltipActivationClick(rightClick, out tooltipParent);

            return base.TryHandleTooltipActivationClick(rightClick, out tooltipParent);
        }

        public override bool IsInsideContainer(ControlTemplate entry, Vector2 position)
        {
            InteractiveSurfaceScript parentInteractive;
            if (TryGetParentInteractive(out parentInteractive))
                return parentInteractive.IsInsideContainer(entry, position);

            return base.IsInsideContainer(entry, position);
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();

            bool proxyingParent = _parent != null;
            if (!proxyingParent)
            {
                AddBackground(_sprites);
                DrawTitle(_sprites);
            }

            if (!(Block is IMyTextPanel))
            {
                var color = ColorComponent.ResolveErrorColor();
                DrawMessage(_sprites, "Unsupported Screen", "Cross", color, 0.9f);
                return _sprites;
            }

            if (_parent == null)
            {
                if (_hostResolutionUnsupported)
                {
                    var color = ColorComponent.ResolveWarningColor();
                    DrawMessage(_sprites, "Unsupported resolution", "Resolution", color, 0.9f);
                }
                else if (_hostScriptUnsupported)
                {
                    var color = ColorComponent.ResolveErrorColor();
                    DrawMessage(_sprites, "Unsupported script", "Cross", color, 0.9f);
                }
                else
                {
                    var color = ColorComponent.ResolveWarningColor();
                    DrawMessage(_sprites, "Screen Not Linked", "Warning", color, 0.9f);
                }

                return _sprites;
            }

            if (!IsParentAlive(_parent))
            {
                var color = ColorComponent.ResolveErrorColor();
                DrawMessage(_sprites, "Unsuported script", "Cross", color, 0.9f);
                return _sprites;
            }

            var offset = GetCurrentProxyOffset();
            List<MySprite> parentFrame;
            try
            {
                parentFrame = _parent.GetCachedFrame();
            }
            catch
            {
                var color = ColorComponent.ResolveErrorColor();
                DrawMessage(_sprites, "Unsuported script", "Cross", color, 0.9f);
                return _sprites;
            }

            foreach (var sprite in parentFrame)
                _sprites.Add(RemapParentSprite(sprite, offset));

            return _sprites;
        }

        static MySprite RemapParentSprite(MySprite sprite, Vector2 offset)
        {
            if (sprite.Type == SpriteType.CLIP_RECT)
                return RemapParentClipRect(sprite, offset);

            return new MySprite(
                sprite.Type,
                sprite.Data,
                sprite.Position - offset,
                sprite.Size,
                sprite.Color,
                sprite.FontId,
                sprite.Alignment,
                sprite.RotationOrScale);
        }

        static MySprite RemapParentClipRect(MySprite sprite, Vector2 offset)
        {
            if (!sprite.Position.HasValue)
                return sprite;

            return new MySprite(
                SpriteType.CLIP_RECT,
                sprite.Data,
                sprite.Position.Value - offset,
                sprite.Size,
                sprite.Color,
                sprite.FontId,
                sprite.Alignment,
                sprite.RotationOrScale);
        }

        protected override void OnMouseScroll(int delta, ref bool handled)
        {
            InteractiveSurfaceScript parentInteractive;
            if (TryGetParentInteractive(out parentInteractive))
            {
                try
                {
                    parentInteractive.MouseScroll(delta);
                    handled = true;
                    return;
                }
                catch
                {
                    InvalidateParentReference(parentInteractive);
                }
            }

            base.OnMouseScroll(delta, ref handled);
            AppInteractive?.OnMouseScroll(delta, ref handled);
        }

        protected override List<MySprite> RenderFrame(Func<List<MySprite>> sprites)
        {
            return sprites();
        }

        public override void Dispose()
        {
            UnsubscribeFromParentRender();

            if (_registeredParent != null)
            {
                _registeredParent.UnregisterProxy(_registeredProxyKey);
                _registeredParent = null;
                _registeredProxyKey = 0L;
            }

            base.Dispose();
        }
    }
}
