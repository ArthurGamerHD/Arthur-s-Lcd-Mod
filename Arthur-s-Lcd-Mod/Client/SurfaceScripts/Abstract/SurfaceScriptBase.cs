using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Generated;
using Adk.Gui.Animation;
using Adk.Gui.Lcd;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.Views;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.UserControls;
using LcdMod.Client.Helpers;
using LcdMod.Client.ScreenAreas;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;
using Sandbox.Game.Components;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using IMyCockpit = Sandbox.ModAPI.IMyCockpit;
using IMyShipController = Sandbox.ModAPI.IMyShipController;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
using NotImplementedException = LcdMod.Common.Exceptions.NotImplementedException;

namespace LcdMod.Client.SurfaceScripts.Abstract
{
    public abstract class SurfaceScriptBase : MyTSSCommon, IAppHost, IUsesTerminalControlGroup<BaseTerminalControlGroup>
    {
        public static SurfaceCollection Instances = new SurfaceCollection();
        public Dictionary<string, Color> Theme { get; set; }
        readonly List<MySprite> _backgroundGrids = new List<MySprite>();
        readonly Dictionary<long, Vector2> _registeredProxyOffsets = new Dictionary<long, Vector2>();
        readonly List<MySprite> _cachedFrame = new List<MySprite>();
        Color _backgroundColor;
        Color _foregroundColor;

        public IMyFaction Faction { get; protected set; }
        protected string Icon { get; set; }
        public new IMyCubeBlock Block { get; }

        public virtual bool AsyncRender => false;

        protected virtual bool ClipToBounds => _registeredProxyOffsets.Count > 0;

        protected long WaitForFrame;
        protected long LastRenderFrame;
        public long LastRunTick { get; private set; } = long.MinValue;

        public Vector2 TextureSize => Surface.TextureSize;

        /// <summary>
        /// Relative area of the <see cref="Sandbox.ModAPI.IMyTextSurface.TextureSize"/> That is Visible
        /// </summary>
        public virtual RectangleF ViewBox { get; protected set; }

        GridLogic _gridLogic;
        long _appGridEntityId;
        public GridLogic GridLogic
        {
            get
            {
                var cubeGrid = Block?.CubeGrid;
                if (cubeGrid == null || cubeGrid.Closed || cubeGrid.MarkedForClose)
                {
                    _gridLogic = null;
                    return null;
                }

                if (_gridLogic == null || _gridLogic.TargetGrid != cubeGrid.EntityId || !_gridLogic.IsAlive)
                    _gridLogic = LcdModSessionComponent.GetOrCreateGridLogic(cubeGrid);

                return _gridLogic;
            }
        }

        bool _init;
        int _rotationOrSurfaceIndex;

        protected float CaretY;
        protected float FooterHeight;

        protected const float TITLE_BAR_HEIGHT_BASE = 40f;

        protected string LocalizedTitleCache = string.Empty;

        string _customInfo;

        public virtual string Title
        {
            get
            {
                if (string.IsNullOrEmpty(LocalizedTitleCache))
                    LocalizedTitleCache = MyTexts.GetString(DefaultTitle);

                return LocalizedTitleCache;
            }
        }

        protected virtual string DefaultTitle => "<Title not Set>";

        public float ConfiguredScale => GeneralComponent.GetScale();
        protected float FontScale => _userFontScale <= 0f ? 1f : _userFontScale;
        protected float LayoutScale => ConfiguredScale * FontScale;

        float _userScale;
        float _userFontScale;
        string _userFont;
        protected float _userPadding;
        string _cachedTitleSource;
        string _cachedTitleText;
        float _cachedTitleAvailableWidth = -1f;
        float _cachedTitleFontSize = -1f;
        bool _cachedTitleLocalized;
        public bool TitleCanBeVisible { get; private set; } = true;
        public bool TitleVisible { get; private set; } = true;
        public string TitleIcon => string.IsNullOrWhiteSpace(Icon) ? "MissingIcon" : Icon;
        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public SurfaceConfig Config { get; protected set; }
        IComponentContainer IAppHost.Config => Config;
        public GeneralConfigComponent GeneralComponent => Config.GetComponent<GeneralConfigComponent>();
        public ColorConfigComponent ColorComponent => Config.GetComponent<ColorConfigComponent>();
        public InteractiveConfigComponent InteractionComponent => Config.GetComponent<InteractiveConfigComponent>();
        protected abstract AppType AppType { get; }

        public bool Dirty => _dirty;
        bool _dirty;
        bool _disposed;
        volatile bool _asyncRenderPending;
        IApp _publishedApp;
        App _retainedViewApp;
        LcdAppViewHost _retainedViewHost;

        public AnimationController Animations { get; private set; }
        public ScreenProviderConfig ProviderConfig { get; private set; }
        protected bool IsScreenReadyToRender { get; private set; }
        public event Action<SurfaceScriptBase> OnRender;
        public event Action<SurfaceScriptBase> OnUpdateStart;
        public event Action<SurfaceScriptBase> OnUpdateEnd;

        protected SurfaceScriptBase(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block,
            size)
        {
            Animations = new AnimationController(
                GetAnimationFrame,
                ScheduleAnimationFrame,
                RenderSprites);

            WaitForFrame = MyAPIGateway.Session.GameplayFrameCounter + 6 * 5; // minimum of 5 frames splash screen 
            Block = (IMyCubeBlock)base.Block;
            var terminalBlock = (IMyTerminalBlock)Block;
            terminalBlock.AppendingCustomInfo += OnAppendingCustomInfo;
            terminalBlock.SetDetailedInfoDirty();
            terminalBlock.RefreshCustomInfo();
            
            _textureSize = (Vector2I)Surface.TextureSize;
            var surfaceSize = Surface.SurfaceSize;
            _renderComp = (MyRenderComponentScreenAreas)Block.Render;

            _aspectRatio = surfaceSize.X > surfaceSize.Y
                ? new Vector2(1f, 1f * surfaceSize.Y / surfaceSize.X)
                : new Vector2(1f * surfaceSize.X / surfaceSize.Y, 1f);

            if (Block != null) Block.OnMarkForClose += HandleBlockMarkedForClose;
            UpdateFaction(FactionHelper.GetOwnerFaction(Block as IMyTerminalBlock));
            DrawSplash();
            LcdModSessionComponent.OnLanguageChanged += LayoutChanged;
        }

        public int RotationOrSurfaceIndex => _rotationOrSurfaceIndex;
        public int SurfaceIndex => Config == null ? RotationOrSurfaceIndex : Config.SurfaceIndex;

        public abstract IApp App { get; }

        protected int ResolveRotationOrSurfaceIndex()
        {
            if (Block.CubeGrid.Physics == null)
                return -1; // we can ignore surface that does not exist

            if (Block is IMyTextPanel)
            {
                foreach (var component in Block.Components)
                {
                    _lcdSurfaceComponent = component as IMyLcdSurfaceComponent;
                    if (_lcdSurfaceComponent == null)
                        continue;

                    return _lcdSurfaceComponent.SelectedRotationIndex;
                }
            }
            else
            {
                var surfaceProvider = Block as IMyTextSurfaceProvider;
                if (surfaceProvider != null)
                {
                    var currentSurfaceName = Surface.Name;

                    for (int i = 0; i < surfaceProvider.SurfaceCount; i++)
                    {
                        if (surfaceProvider.GetSurface(i).Name != currentSurfaceName)
                            continue;

                        return i;
                    }
                }
            }

            LogHelper.Log(MyLogSeverity.Warning, "Failed to find surface {0} for {1}", Surface.Name, Block);
            return -1;
        }

        static long GetAnimationFrame()
        {
            return MyAPIGateway.Session.GameplayFrameCounter;
        }

        static void ScheduleAnimationFrame(Action action)
        {
            // Animations can start while a screen renders on a worker thread.
            if (action != null)
                lock (LcdModClientComponent.RunNextFrame)
                    LcdModClientComponent.RunNextFrame.Add(action);
        }

        void DrawSplash()
        {
            if (ViewBox.Size == Vector2.Zero)
                UpdateViewBox();

            var offset = Math.Min(ViewBox.Width, ViewBox.Height) / 5;
            var frame = Surface.DrawFrame();
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", ViewBox.Center,
                new Vector2(Math.Max(ViewBox.Width, ViewBox.Height) * 2), FactionHelper.GetBackgroundColor(Faction)));
            frame.Add(new MySprite(SpriteType.TEXTURE, Icon,
                new Vector2(ViewBox.Center.X, ViewBox.Center.Y - offset / 2),
                new Vector2(Math.Min(ViewBox.Width, ViewBox.Height) / 1.5f), FactionHelper.GetIconColor(Faction)));
            frame.Add(new MySprite(SpriteType.TEXT, Title, new Vector2(ViewBox.Center.X, ViewBox.Center.Y + offset),
                null, FactionHelper.GetIconColor(Faction), "White", rotation: 1.6f * FontScale));
            frame.Dispose();
        }

        protected void AddEmptyWithFiltersSprites(List<MySprite> sprites)
        {
            AddBackground(sprites);
            DrawTitle(sprites);
            DrawMessage(sprites, LocHelper.GetLoc("ScreenBlueprintsRew_NoBlueprints"),
                "Warning", GetWarningColor(), ConfiguredScale);
        }

        protected void AddEmptySprites(List<MySprite> sprites)
        {
            AddBackground(sprites);
            DrawTitle(sprites);
            DrawMessage(sprites, LocHelper.Empty,
                "Warning", GetWarningColor(), ConfiguredScale);
        }

        public virtual void RequestRedraw()
        {
            LayoutChanged();
            _dirty = true;
            Run();
            _dirty = false;
        }

        public void UseProviderConfig(ScreenProviderConfig providerConfig)
        {
            if (providerConfig == null)
                return;

            ProviderConfig = providerConfig;

            var index = Config == null ? RotationOrSurfaceIndex : Config.SurfaceIndex;
            if (index < 0)
                return;

            providerConfig.EnsureSurfaceApp(index, AppType);
            var persistedSurface = providerConfig.GetSurfaceConfig(index);
            if (persistedSurface == null)
                return;

            Config = providerConfig.CanWriteConfig(persistedSurface)
                ? persistedSurface
                : persistedSurface.Clone();
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            UnpublishApp();

            if (_retainedViewHost != null)
                _retainedViewHost.Dispose();
            _retainedViewHost = null;
            _retainedViewApp = null;

            try
            {
                CloseApp(App);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }

            try
            {
                if (Animations != null)
                    Animations.Dispose();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }

            try
            {
                if (Block != null)
                {
                    Block.OnMarkForClose -= HandleBlockMarkedForClose;
                    var terminalBlock = (IMyTerminalBlock)Block;
                    terminalBlock.AppendingCustomInfo -= OnAppendingCustomInfo;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }

            try
            {
                if (Block != null && ProviderConfig != null)
                    ConfigManager.Save(Block, ProviderConfig);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }

            // Ensure module hooks are detached even if the instance list is already out of sync.
            LcdModSessionComponent.UnhookSurfaceModules(this);
            Instances.Remove(this);
            LcdModSessionComponent.OnLanguageChanged -= LayoutChanged;
            OnRender = null;
            OnUpdateStart = null;
            OnUpdateEnd = null;
            base.Dispose();
        }

        void HandleBlockMarkedForClose(IMyEntity entity)
        {
            Dispose();
        }

        static void CloseApp(IApp app)
        {
            var appBase = app as App;
            if (appBase == null)
                return;

            appBase.Close();
        }

        internal App RetainedViewApp => _retainedViewApp;

        internal LcdGuiHost RetainedAppGuiHost =>
            _retainedViewHost == null ? null : _retainedViewHost.GuiHost;

        protected void EnsureRetainedAppView(App app)
        {
            if (ReferenceEquals(_retainedViewApp, app) && _retainedViewHost != null)
                return;

            if (_retainedViewHost != null)
                _retainedViewHost.Dispose();
            _retainedViewApp = app;
            _retainedViewHost = app == null
                ? null
                : new LcdAppViewHost(Surface, app, Animations);
        }

        protected bool DrawRetainedAppView(List<MySprite> sprites)
        {
            if (_retainedViewHost == null)
                return false;
            _retainedViewHost.Draw(ViewBox, sprites);
            return true;
        }

        protected virtual void UpdateViewBox()
        {
            var sizeOffset = (Surface.TextureSize - Surface.SurfaceSize) / 2;

            _userPadding = Surface.TextPadding;

            var padding = (Surface.TextPadding / 100) * Surface.SurfaceSize;
            sizeOffset += padding / 2;

            ViewBox = ApplyProxyOffsets(new RectangleF(
                sizeOffset.X,
                sizeOffset.Y,
                Surface.SurfaceSize.X - padding.X,
                Surface.SurfaceSize.Y - padding.Y));
        }

        protected RectangleF ApplyProxyOffsets(RectangleF viewBox)
        {
            if (_registeredProxyOffsets.Count <= 0)
                return viewBox;

            float minOffsetX = 0f;
            float minOffsetY = 0f;
            float maxOffsetX = 0f;
            float maxOffsetY = 0f;

            foreach (var entry in _registeredProxyOffsets)
            {
                var offset = entry.Value;
                if (offset.X < minOffsetX)
                    minOffsetX = offset.X;
                if (offset.Y < minOffsetY)
                    minOffsetY = offset.Y;
                if (offset.X > maxOffsetX)
                    maxOffsetX = offset.X;
                if (offset.Y > maxOffsetY)
                    maxOffsetY = offset.Y;
            }

            return new RectangleF(
                viewBox.X + minOffsetX,
                viewBox.Y + minOffsetY,
                viewBox.Width + (maxOffsetX - minOffsetX),
                viewBox.Height + (maxOffsetY - minOffsetY));
        }

        public override void Run()
        {
            var currentFrame = MyAPIGateway.Session.GameplayFrameCounter;
            if (currentFrame < WaitForFrame || _disposed)
            {
                ReuploadBSoD();
                return;
            }

            if (currentFrame - LastRunTick > MaxRunGap)
                InvalidateUploadedFrame();
            LastRunTick = currentFrame;

            base.Run();

            if (ViewBox.Size == Vector2.Zero)
                UpdateViewBox();

            if (!_init)
                Init();

            IsScreenReadyToRender = false;

            if (Config == null)
            {
                GetSettings((IMyTextSurface)Surface, Block);
                return;
            }
            
            if (_init)
                Instances.RefreshActiveInstance(this);

            var titleCanBeVisible = SurfaceAspectRatioHelper.CanShowTitle(Surface);
            var titleVisible = GeneralComponent.TitleVisible && titleCanBeVisible;

            if (Math.Abs(_userPadding - Surface.TextPadding) > .01f ||
                Math.Abs(_userScale - GeneralComponent.GetScale()) > .001f ||
                Math.Abs(_userFontScale - Surface.FontSize) > .001f ||
                !string.Equals(_userFont, Surface.Font, StringComparison.Ordinal) ||
                BackgroundColor != _backgroundColor ||
                ForegroundColor != _foregroundColor ||
                TitleCanBeVisible != titleCanBeVisible ||
                TitleVisible != titleVisible)
                LayoutChanged();


            var cubeGrid = Block?.CubeGrid;
            if (cubeGrid == null || cubeGrid.Closed || cubeGrid.MarkedForClose)
            {
                _gridLogic = null;
                DrawLoadingScreen(GeneralComponent.GetScale());
                return;
            }

            if (_gridLogic == null || _gridLogic.TargetGrid != cubeGrid.EntityId || !_gridLogic.IsAlive)
                _gridLogic = LcdModSessionComponent.GetOrCreateGridLogic(cubeGrid);

            if (_gridLogic == null)
            {
                DrawLoadingScreen(GeneralComponent.GetScale());
                return;
            }

            ReplaceGridBoundAppIfNeeded(cubeGrid.EntityId);

            IsScreenReadyToRender = true;

            PublishAppChange();
            RaiseUpdateStart();
            try
            {
                SafeRun();
            }
            catch (Exception e)
            {
                OnException(e);
            }
            finally
            {
                RaiseUpdateEnd();
                PublishAppChange();
            }
        }

        void PublishAppChange()
        {
            IApp currentApp;
            try
            {
                currentApp = App;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                return;
            }

            if (ReferenceEquals(_publishedApp, currentApp))
                return;

            var previousApp = _publishedApp;
            _publishedApp = currentApp;
            Instances.NotifyAppChanged(this, previousApp, currentApp);
        }

        void UnpublishApp()
        {
            if (_publishedApp == null)
                return;

            var previousApp = _publishedApp;
            _publishedApp = null;
            Instances.NotifyAppChanged(this, previousApp, null);
        }

        void ReplaceGridBoundAppIfNeeded(long gridEntityId)
        {
            var previousGridEntityId = _appGridEntityId;
            _appGridEntityId = gridEntityId;
            if (previousGridEntityId == 0L || previousGridEntityId == gridEntityId)
                return;

            IApp detachedApp;
            try
            {
                detachedApp = DetachGridBoundApp();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                return;
            }

            if (detachedApp == null)
                return;

            UnpublishApp();
            try
            {
                CloseApp(detachedApp);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        /// <summary>
        /// Detaches an app whose subscriptions belong to the grid on which it was created.
        /// SafeRun creates its replacement against the current grid.
        /// </summary>
        protected virtual IApp DetachGridBoundApp()
        {
            return null;
        }

        void Init()
        {
            _rotationOrSurfaceIndex = ResolveRotationOrSurfaceIndex();
            var panel = Block as IMyTextPanel;
            
            if (panel != null && Instances.GetInstance(panel, _rotationOrSurfaceIndex) != null)
            {
                _cachedFrame.Clear();
                _customInfo = LocHelper.GetLoc(MOD_PREFIX + "IndexConflict");
                DrawMessage(_cachedFrame, _customInfo, "Warning", Color.Red);
                _customInfo += "\n" + LocHelper.GetLoc(MOD_PREFIX + "IndexConflictDetails");
                panel.RefreshCustomInfo();
                return;
            }

            Instances.Add(this);
            _init = true;
        }

        protected virtual string GetDetailedInfoCustomText() => string.Empty;

        void OnAppendingCustomInfo(IMyTerminalBlock block, StringBuilder detailedInfo)
        {
            if (_disposed)
                return;

            if (detailedInfo.Length > 0) 
                detailedInfo.AppendLine();

            if (!(block is IMyTextPanel)) 
                detailedInfo.AppendLine(Surface.DisplayName + ": ");
            
            if (!string.IsNullOrWhiteSpace(_customInfo))
                detailedInfo.AppendLine(_customInfo);

            var customText = GetDetailedInfoCustomText() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(customText))
                return;

            detailedInfo.AppendLine(customText);
        }

        readonly List<MySprite> _bSoDReupload = new List<MySprite>();

        // The crash frame is uploaded once; when it introduced the QR texture, SE drew it without the code.
        void ReuploadBSoD()
        {
            if (_bSoDReupload.Count == 0 || _disposed)
                return;
            try
            {
                _renderComp.RenderSpritesToTexture(RotationOrSurfaceIndex, _bSoDReupload, _textureSize, _aspectRatio,
                    Surface.ScriptBackgroundColor, Surface.BackgroundAlpha);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
            _bSoDReupload.Clear();
        }

        public void OnException(Exception e)
        {
            try
            {
                var bSoD = BSoD.ShowBSoD(this, e);

                _renderComp.RenderSpritesToTexture(RotationOrSurfaceIndex, bSoD.Frame, _textureSize, _aspectRatio,
                    Surface.ScriptBackgroundColor, Surface.BackgroundAlpha);
                _bSoDReupload.Clear();
                if (bSoD.NeedsReupload)
                    _bSoDReupload.AddRange(bSoD.Frame);
                
                CacheFrameForProxies(bSoD.Frame);
                NotifyRendered();
            }
            catch (Exception e2)
            {
                ErrorHandlerHelper.LogError(e, this);
                ErrorHandlerHelper.LogError(e2, this);
            }

            WaitForFrame = MyAPIGateway.Session.GameplayFrameCounter + 600;
        }

        void GetSettings(IMyTextSurface surface, IMyCubeBlock block)
        {
            var index = 0;
            IMyTextSurfaceProvider surfaceProvider = (IMyTextSurfaceProvider)block;
            while (index < surfaceProvider.SurfaceCount)
            {
                if (surface.Equals(surfaceProvider.GetSurface(index)))
                {
                    SurfaceConfig config;
                    var providerConfig = ProviderConfig;
                    ConfigManager.LoadSettings(block, index, AppType, ref providerConfig, out config);
                    ProviderConfig = providerConfig;
                    Config = config;
                    return;
                }

                index++;
            }
        }

        /// <summary>
        /// Resets the <see cref="CaretY"/> to the Top of the screen, if <see cref="TitleVisible"/>, draws the Tittle 
        /// </summary>
        /// <param name="frame"></param>
        public virtual void DrawTitle(List<MySprite> frame)
        {
            const float margin = 0f;
            float headerScale = LayoutScale;
            float titleBarHeight = TITLE_BAR_HEIGHT_BASE * headerScale;
            Vector2 position = ViewBox.Position;
            position.X += margin;

            CaretY = position.Y;

            if (!TitleVisible)
                return;

            AddHeaderSprite(frame, new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = Icon,
                Position = position + new Vector2(20f) * headerScale,
                Size = new Vector2(40f * headerScale),
                Color = GetHeaderColor(),
                Alignment = TextAlignment.CENTER
            });
            position.X += ViewBox.Width / 8f;

            frame.Add(MySprite.CreateClipRect(new Rectangle((int)position.X, (int)position.Y,
                (int)(ViewBox.Width - position.X + ViewBox.X),
                (int)(position.Y + 35f * headerScale))));

            var availableWidth = ViewBox.Width - position.X + ViewBox.X;
            var titleText = GetCachedTitleText(availableWidth, 1.3f, true);

            AddHeaderSprite(frame, new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = titleText,
                Position = position,
                RotationOrScale = ConfiguredScale * 1.3f * FontScale,
                Color = GetHeaderColor(),
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            frame.Add(MySprite.CreateClearClipRect());

            CaretY += titleBarHeight;
        }

        protected static readonly Regex RxGroup = new Regex(@"\(\s*G\s*:\s*(.+?)\s*\)", RegexOptions.IgnoreCase);
        protected static readonly Regex RxContainer = new Regex(@"\(\s*(?!G\s*:)(.+?)\s*\)", RegexOptions.IgnoreCase);

        protected static MySprite MakeText(IMyTextSurface surf, string s, Vector2 p, float scale,
            TextAlignment alignment = TextAlignment.LEFT)
        {
            return new MySprite
            {
                Type = SpriteType.TEXT,
                Data = s,
                Position = p,
                Color = surf.ScriptForegroundColor,
                Alignment = alignment,
                RotationOrScale = scale * surf.FontSize
            };
        }

        protected static int GetScrollStep(int secondsPerStep)
        {
            return GetTimeStep(secondsPerStep);
        }

        protected static int GetTimeStep(float secondsPerStep)
        {
            try
            {
                var sess = MyAPIGateway.Session;
                if (secondsPerStep <= 0f) secondsPerStep = 1f / 60f;

                // SE runs at 60 game ticks per second.
                int ticksPerStep = Math.Max(1, (int)Math.Round(secondsPerStep * 60f));
                long frameCounter = sess.GameplayFrameCounter;
                return (int)(frameCounter / ticksPerStep);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"[LcdMod] GetTimeStep error: {ex.Message}");
                return 0;
            }
        }

        public bool TryGetReferenceWorldMatrix(ReferenceMode mode, out MatrixD world,
            bool useBlockWorldForCockpitAuto = false)
        {
            world = MatrixD.Identity;

            switch (mode)
            {
                case ReferenceMode.Screen:
                    return ScreenAreaGeometry.TryGetScreenWorldMatrix(this, out world);
                case ReferenceMode.Controller:
                    return TryGetControllerWorldMatrix(out world);
                case ReferenceMode.Auto:
                default:
                    if (Block is IMyCockpit)
                    {
                        if (useBlockWorldForCockpitAuto)
                        {
                            world = Block.WorldMatrix;
                            return true;
                        }

                        if (TryGetCockpitWorldMatrix(out world))
                            return true;
                    }

                    return ScreenAreaGeometry.TryGetScreenWorldMatrix(this, out world);
            }
        }

        public bool TryGetReferenceWorldMatrix(int referenceModeValue, out MatrixD world,
            bool useBlockWorldForCockpitAuto = false)
        {
            var mode = (ReferenceMode)referenceModeValue;
            return TryGetReferenceWorldMatrix(mode, out world, useBlockWorldForCockpitAuto);
        }

        public bool TryGetCockpitWorldMatrix(out MatrixD world)
        {
            world = MatrixD.Identity;
            var cockpit = Block as IMyCockpit;
            if (cockpit == null)
                return false;

            world = cockpit.WorldMatrix;
            return true;
        }

        public bool TryGetControllerWorldMatrix(out MatrixD world)
        {
            world = MatrixD.Identity;
            var controller = ResolveShipController();
            if (controller == null)
                return false;

            world = controller.WorldMatrix;
            return true;
        }

        public IMyShipController ResolveShipController()
        {
            var myGrid = Block?.CubeGrid as MyCubeGrid;
            if (myGrid == null)
                return null;

            if (myGrid.MainCockpit != null)
                return myGrid.MainCockpit as IMyShipController;

            if (myGrid.MainRemoteControl != null)
                return myGrid.MainRemoteControl as IMyShipController;

            return null;
        }


        protected virtual RectangleF GetCellViewBox(float xStart, float xEnd, float yStart, float cellHeight,
            float cellPadding)
        {
            var innerLeft = xStart + cellPadding;
            var innerRight = xEnd - cellPadding;
            var innerTop = yStart + cellPadding;
            var innerBottom = yStart + cellHeight - cellPadding;
            return new RectangleF(innerLeft, innerTop, innerRight - innerLeft, innerBottom - innerTop);
        }

        protected virtual MyTuple<RectangleF, RectangleF, RectangleF> GetCellSlots(float innerLeft, float innerRight,
            float innerTop, float innerBottom, float spacing)
        {
            var topRowHeight = spacing * ConfiguredScale;
            var bottomRowTop = innerTop + topRowHeight;
            var bottomRowHeight = Math.Max(0f, innerBottom - bottomRowTop);
            var iconSize = innerBottom - innerTop;
            var contentLeft = innerLeft + iconSize;
            var contentWidth = Math.Max(0f, innerRight - contentLeft);

            var iconRect = new RectangleF(innerLeft, innerTop, iconSize, iconSize);
            var numberRect = new RectangleF(contentLeft, innerTop, contentWidth, topRowHeight);
            var nameRect = new RectangleF(contentLeft, bottomRowTop, contentWidth, bottomRowHeight);
            return new MyTuple<RectangleF, RectangleF, RectangleF>(iconRect, numberRect, nameRect);
        }

        public virtual void DrawMessage(List<MySprite> sprites, string message, string icon, Color color,
            float scale = 1f)
        {
            float contentTop = CaretY;
            float contentBottom = ViewBox.Bottom - FooterHeight;
            float contentHeight = Math.Max(0f, contentBottom - contentTop);
            if (contentHeight <= 0f)
                return;

            var center = new Vector2(ViewBox.Center.X, contentTop + contentHeight * 0.45f);
            float iconSize = Math.Min(ViewBox.Width, contentHeight) * .4f * scale;

            var iconSprite = new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = icon,
                Position = center,
                Size = new Vector2(iconSize),
                Color = color,
                Alignment = TextAlignment.CENTER
            };

            var textSprite = new MySprite
            {
                Type = SpriteType.TEXT,
                Data = message,
                Position = new Vector2(center.X, center.Y + (iconSize / 2)),
                Color = color,
                Alignment = TextAlignment.CENTER,
                FontId = "White",
                RotationOrScale = 1f * ConfiguredScale * FontScale
            };

            sprites.Add(iconSprite.Shadow(2 * ConfiguredScale));
            sprites.Add(iconSprite);

            sprites.Add(textSprite.Shadow(2 * ConfiguredScale));
            sprites.Add(textSprite);
        }

        public virtual void DrawLoading(List<MySprite> sprites, float scale = 1f)
        {
            DrawLoadingFrame(sprites, scale);
        }

        protected Color GetHeaderColor()
        {
            return ColorComponent.ResolveHeaderColor(Block as IMyTerminalBlock);
        }

        protected Color GetWarningColor()
        {
            return ColorComponent.ResolveWarningColor();
        }

        protected Color GetErrorColor()
        {
            return ColorComponent.ResolveErrorColor();
        }

        protected static void ParseFilter(IMyTerminalBlock lcd, out string mode, out string token)
        {
            mode = null;
            token = null;
            if (lcd == null) return;
            var name = lcd.CustomName ?? string.Empty;

            var mg = RxGroup.Match(name);
            if (mg.Success)
            {
                mode = "group";
                token = mg.Groups[1].Value.Trim();
                return;
            }

            var mc = RxContainer.Match(name);
            if (mc.Success)
            {
                mode = "container";
                token = mc.Groups[1].Value.Trim();
            }
        }

        public void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1)
        {
            Vector2 textSize = Surface.MeasureStringInPixels(sb, "White", fontSize * ConfiguredScale * FontScale);

            if (textSize.X > availableWidth)
            {
                var source = sb.ToString();
                for (int i = source.Length - 1; i > 0; i--)
                {
                    sb.Clear();
                    sb.Append(FormatingHelper.TrimName(source, i));
                    textSize = Surface.MeasureStringInPixels(sb, "White", fontSize * ConfiguredScale * FontScale);

                    if (textSize.X <= availableWidth)
                        break;
                }
            }
        }

        protected static List<KeyValuePair<string, double>> SortedItems(Dictionary<string, double> source)
        {
            var list = new List<KeyValuePair<string, double>>();
            if (source == null) return list;
            foreach (var kv in source) list.Add(kv);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }

        protected Vector2 ToScreenMargin(Vector2 absoluteCenterInViewBox)
        {
            return new Vector2(absoluteCenterInViewBox.X, 512f - absoluteCenterInViewBox.Y);
        }

        protected MySprite Text(string s, Vector2 p, float scale)
        {
            return new MySprite
            {
                Type = SpriteType.TEXT, Data = s, Position = p,
                Color = Surface.ScriptForegroundColor, Alignment = TextAlignment.LEFT,
                RotationOrScale = scale * FontScale
            };
        }

        protected MySprite Centered(string s, Vector2 p, float scale)
        {
            return new MySprite
            {
                Type = SpriteType.TEXT, Data = s, Position = p,
                Color = Surface.ScriptForegroundColor, Alignment = TextAlignment.CENTER,
                RotationOrScale = scale * FontScale
            };
        }

        protected virtual void LayoutChanged()
        {
            if(Surface == null)
                return; // we got disposed

            _userPadding = Surface.TextPadding;
            _userScale = GeneralComponent.GetScale();
            _userFontScale = Surface.FontSize;
            _userFont = Surface.Font;
            _backgroundColor = BackgroundColor;
            _foregroundColor = ForegroundColor;
            LocalizedTitleCache = string.Empty;
            TitleCanBeVisible = SurfaceAspectRatioHelper.CanShowTitle(Surface);
            TitleVisible = GeneralComponent.TitleVisible && TitleCanBeVisible;
            InvalidateTitleCache();
            UpdateViewBox();
            InvalidateUploadedFrame();
            _backgroundGrids.Clear();
            (Block as IMyTerminalBlock)?.RefreshTerminal();
        }


        protected void DrawLoadingScreen(float scale = 1f, bool drawTitle = true)
        {
            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddLoadingScreenSprites(sprites, scale, drawTitle);
                frame.AddRange(sprites);
            }
        }

        protected void AddLoadingScreenSprites(List<MySprite> sprites, float scale = 1f, bool drawTitle = true)
        {
            AddBackground(sprites);
            if (drawTitle)
                DrawTitle(sprites);
            DrawLoadingFrame(sprites, scale);
        }

        protected virtual void DrawLoadingFrame(List<MySprite> sprites, float scale = 1f)
        {
            float contentTop = CaretY;
            float contentBottom = ViewBox.Bottom - FooterHeight;
            float contentHeight = Math.Max(0f, contentBottom - contentTop);
            if (contentHeight <= 0f)
                return;

            var center = new Vector2(ViewBox.Center.X, contentTop + contentHeight * 0.45f);
            float wheelScale = Math.Max(0.05f, scale);
            float outerSize = Math.Min(ViewBox.Width, contentHeight) * 0.28f * wheelScale;
            float innerSize = outerSize * 0.6f;

            var session = MyAPIGateway.Session;
            double seconds = session.GameplayFrameCounter / 60.0;
            float outerRotation = (float)(seconds * 2.4);
            float innerRotation = -outerRotation;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Screen_LoadingBar",
                Position = center,
                Size = new Vector2(outerSize),
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = outerRotation
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Screen_LoadingBar",
                Position = center,
                Size = new Vector2(innerSize),
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = innerRotation
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = LocHelper.GetLoc("LoadingPleaseWait"),
                Position = new Vector2(center.X, center.Y + outerSize * 0.9f),
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White",
                RotationOrScale = ConfiguredScale * FontScale
            });
        }

        protected string GetCachedTitleText(float availableWidth, float fontSize = 1.3f, bool localizeTitle = false)
        {
            var source = localizeTitle ? MyTexts.GetString(Title) : Title;
            availableWidth = Math.Max(0f, availableWidth);

            if (_cachedTitleText != null &&
                _cachedTitleSource == source &&
                _cachedTitleLocalized == localizeTitle &&
                Math.Abs(_cachedTitleAvailableWidth - availableWidth) <= 0.1f &&
                Math.Abs(_cachedTitleFontSize - fontSize) <= 0.0001f)
            {
                return _cachedTitleText;
            }

            var sb = new StringBuilder(source ?? string.Empty);
            if (availableWidth > 0f)
                TrimText(ref sb, availableWidth, fontSize);

            _cachedTitleSource = source;
            _cachedTitleLocalized = localizeTitle;
            _cachedTitleAvailableWidth = availableWidth;
            _cachedTitleFontSize = fontSize;
            _cachedTitleText = sb.ToString();
            return _cachedTitleText;
        }

        protected void InvalidateTitleCache()
        {
            _cachedTitleSource = null;
            _cachedTitleText = null;
            _cachedTitleAvailableWidth = -1f;
            _cachedTitleFontSize = -1f;
            _cachedTitleLocalized = false;
        }


        public void AddBackground(List<MySprite> frame, Color? color = null)
        {
            EnsureBackgroundGrids(color);
            frame.AddRange(_backgroundGrids);
        }

        /// <summary>Builds the tiled background once; it asks the game for a draw frame, so parallel renders warm it first.</summary>
        void EnsureBackgroundGrids(Color? color)
        {
            if (_backgroundGrids.Count > 0)
                return;
            var backgroundColor = new Color(color ?? BackgroundColor, 0.66f);
            var frameTemp = Surface.DrawFrame();
            AddTiledBackground(frameTemp, backgroundColor);
            frameTemp.AddToList(_backgroundGrids);
        }

        void AddTiledBackground(MySpriteDrawFrame frame, Color color)
        {
            var tile = MyTextSurfaceHelper.DEFAULT_BACKGROUND;
            tile.Color = color;

            var tileSize = tile.Size ?? new Vector2(MyTextSurfaceHelper.BACKGROUND_SIZE);
            if (tileSize.X <= 0f || tileSize.Y <= 0f)
                tileSize = new Vector2(MyTextSurfaceHelper.BACKGROUND_SIZE);

            var shift = MyTextSurfaceHelper.BACKGROUND_SHIFT;
            float stepX = Math.Abs(shift.X) > 0.001f ? Math.Abs(shift.X) : tileSize.X;
            float stepY = Math.Abs(shift.Y) > 0.001f ? Math.Abs(shift.Y) : tileSize.Y;

            if (stepX <= 0f || stepY <= 0f)
                return;

            var basePosition = (tile.Position ?? Vector2.Zero) + Surface.TextureSize / 2f;
            var bounds = GetBackgroundBounds();
            var halfSize = tileSize * 0.5f;

            float startX = GetFirstBackgroundTileCenter(bounds.X, basePosition.X, halfSize.X, stepX);
            float startY = GetFirstBackgroundTileCenter(bounds.Y, basePosition.Y, halfSize.Y, stepY);

            for (float y = startY; y - halfSize.Y < bounds.Bottom; y += stepY)
            {
                for (float x = startX; x - halfSize.X < bounds.Right; x += stepX)
                {
                    tile.Position = new Vector2(x, y);
                    frame.Add(tile);
                }
            }
        }

        RectangleF GetBackgroundBounds()
        {
            var textureSize = Surface.TextureSize;
            var bounds = new RectangleF(
                0f,
                0f,
                Math.Max(1f, textureSize.X),
                Math.Max(1f, textureSize.Y));

            var viewBox = ViewBox;
            if (viewBox.Width <= 0f || viewBox.Height <= 0f)
                return bounds;

            float left = Math.Min(bounds.X, viewBox.X);
            float top = Math.Min(bounds.Y, viewBox.Y);
            float right = Math.Max(bounds.Right, viewBox.Right);
            float bottom = Math.Max(bounds.Bottom, viewBox.Bottom);

            return new RectangleF(left, top, Math.Max(1f, right - left), Math.Max(1f, bottom - top));
        }

        static float GetFirstBackgroundTileCenter(float boundsStart, float baseCenter, float halfSize, float step)
        {
            float baseStart = baseCenter - halfSize;
            return baseCenter + (float)Math.Floor((boundsStart - baseStart) / step) * step;
        }


        protected static void AddHeaderSprite(List<MySprite> frame, MySprite sprite)
        {
            frame.Add(sprite.Shadow(1f));
            frame.Add(sprite);
        }

        public void UpdateFaction(IMyFaction faction)
        {
            string previousIcon = Icon;
            Faction = faction;
            Icon = FactionHelper.GetIcon(faction);
            if (!string.Equals(previousIcon, Icon, StringComparison.Ordinal) && App != null)
            {
                App.LayoutChanged();
                RenderSprites();
            }
        }

        readonly Vector2I _textureSize;
        readonly Vector2 _aspectRatio;
        readonly MyRenderComponentScreenAreas _renderComp;
        IMyLcdSurfaceComponent _lcdSurfaceComponent;

        /// <summary>
        /// Calling this break the regular rendering of the Text surface, ensure ALL render call is routed here if the app needs to use it
        /// </summary>
        public void RenderSprites()
        {
            RenderSprites(false);
        }

        public void RenderSprites(bool force)
        {
            RequestRender(force, false);
        }

        static readonly List<SurfaceScriptBase> QueuedRenders = new List<SurfaceScriptBase>();
        static readonly List<SurfaceScriptBase> ParallelRenders = new List<SurfaceScriptBase>();
        static readonly List<SurfaceScriptBase> LateRenders = new List<SurfaceScriptBase>();
        static readonly List<SurfaceScriptBase> LateRendersSnapshot = new List<SurfaceScriptBase>();
        static readonly Action<int> RenderQueuedInParallel = RenderParallelItem;
        static bool _queueRenders;
        static volatile bool _renderingInParallel;

        internal static bool ParallelRendering = true;

        bool _renderQueued;
        bool _queuedForce;
        bool _queuedCursorOnly;
        bool _inParallelBatch;
        bool _parallelNotify;
        Exception _parallelError;
        protected bool RenderingCursorOnly { get; private set; }

        /// <summary>
        /// Whether this screen's whole render (layout, drawing and upload) touches only its own state, so it can run
        /// on a worker thread while the rest of the game waits.
        /// </summary>
        protected virtual bool RendersInParallel => false;

        internal virtual bool CanRenderInParallel => RendersInParallel && !_disposed;

        /// <summary>
        /// Renders now, or, during a game update, queues the screen for <see cref="FlushFrameRenders"/> so every
        /// request of the frame (look-at, script runs, animations) becomes one render.
        /// </summary>
        protected void RequestRender(bool force, bool cursorOnly)
        {
            if (_renderingInParallel)
            {
                lock (LateRenders)
                    if (!LateRenders.Contains(this))
                        LateRenders.Add(this);
                return;
            }

            if (_queueRenders)
            {
                if (_renderQueued)
                {
                    _queuedForce |= force;
                    _queuedCursorOnly &= cursorOnly;
                    return;
                }
                _renderQueued = true;
                _queuedForce = force;
                _queuedCursorOnly = cursorOnly;
                QueuedRenders.Add(this);
                return;
            }

            try
            {
                if (RenderNow(force, cursorOnly, true))
                    NotifyRendered();
            }
            catch (Exception e)
            {
                OnException(e);
            }
        }

        internal static void BeginFrameRenders()
        {
            _queueRenders = true;
        }

        /// <summary>
        /// Renders the queued screens: eligible ones on worker threads, then the rest in order, render proxies last
        /// so they mirror their hosts' new frames.
        /// </summary>
        internal static void FlushFrameRenders()
        {
            _queueRenders = false;
            if (QueuedRenders.Count == 0)
                return;

            ParallelRenders.Clear();
            if (ParallelRendering)
            {
                for (int i = 0; i < QueuedRenders.Count; i++)
                {
                    SurfaceScriptBase screen = QueuedRenders[i];
                    if (!screen.CanRenderInParallel)
                        continue;
                    screen.EnsureBackgroundGrids(null);
                    screen._inParallelBatch = true;
                    screen._parallelNotify = false;
                    screen._parallelError = null;
                    ParallelRenders.Add(screen);
                }
            }

            if (ParallelRenders.Count > 0)
            {
                _renderingInParallel = true;
                try
                {
                    MyAPIGateway.Parallel.For(0, ParallelRenders.Count, RenderQueuedInParallel);
                }
                finally
                {
                    _renderingInParallel = false;
                }
            }

            for (int i = 0; i < ParallelRenders.Count; i++)
            {
                SurfaceScriptBase screen = ParallelRenders[i];
                screen._renderQueued = false;
                screen._inParallelBatch = false;
                try
                {
                    if (screen._parallelError != null)
                        screen.OnException(screen._parallelError);
                    else if (screen._parallelNotify)
                        screen.NotifyRendered();
                }
                catch (Exception e)
                {
                    screen.OnException(e);
                }
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < QueuedRenders.Count; i++)
                {
                    SurfaceScriptBase screen = QueuedRenders[i];
                    if (!screen._renderQueued || (screen is RenderProxySurfaceScript) != (pass == 1))
                        continue;
                    screen._renderQueued = false;
                    screen.RequestRender(screen._queuedForce, screen._queuedCursorOnly);
                }
            }
            QueuedRenders.Clear();
            ParallelRenders.Clear();

            lock (LateRenders)
            {
                LateRendersSnapshot.AddRange(LateRenders);
                LateRenders.Clear();
            }
            for (int i = 0; i < LateRendersSnapshot.Count; i++)
                LateRendersSnapshot[i].RequestRender(false, false);
            LateRendersSnapshot.Clear();
        }

        internal static void ClearFrameRenders()
        {
            _queueRenders = false;
            QueuedRenders.Clear();
            ParallelRenders.Clear();
            lock (LateRenders)
                LateRenders.Clear();
        }

        static void RenderParallelItem(int index)
        {
            SurfaceScriptBase screen = ParallelRenders[index];
            try
            {
                screen._parallelNotify = screen.RenderNow(screen._queuedForce, screen._queuedCursorOnly, false);
            }
            catch (Exception e)
            {
                screen._parallelError = e;
            }
        }

        /// <summary>Builds and uploads a frame; returns whether render listeners (such as proxies) must be told.</summary>
        bool RenderNow(bool force, bool cursorOnly, bool allowAsync)
        {
            var currentFrame = MyAPIGateway.Session.GameplayFrameCounter;

            if ((!force && LastRenderFrame == currentFrame) || WaitForFrame > currentFrame || _disposed || _asyncRenderPending)
                return false;
#if DEBUG
            long profileStart = Debugging.FrameProfiler.Start();
            int profiledSprites = 0;
            bool profiledUpload = false;
#endif
            RenderingCursorOnly = cursorOnly;
            try
            {
                var spriteList = PrepareSpritesForRender(RenderFrame(GetSprites));
                CacheFrameForProxies(spriteList);
                var renderList = ClipToBounds ? ClipSpriteListToSurfaceBounds(spriteList) : spriteList;
                LastRenderFrame = currentFrame;
                var backgroundColor = Surface.ScriptBackgroundColor;
                var renderOpacity = GetRenderOpacity();
                bool published = PublishProxyFrame(spriteList);
                bool uploaded = UpdateUploadedFrame(renderList, backgroundColor, renderOpacity, currentFrame);
#if DEBUG
                profiledSprites = renderList.Count;
                profiledUpload = uploaded;
#endif
                if (!uploaded)
                    return published;

#if DEBUG
                AddSpriteCountDebug(renderList);
#endif

                if (AsyncRender && allowAsync)
                {
                    _asyncBuffer.Clear();
                    _asyncBuffer.AddRange(renderList);
                    _asyncRenderPending = true;

                    var surfaceIndex = RotationOrSurfaceIndex;
                    LcdModClientComponent.Blocker.Add(MyAPIGateway.Parallel.Start(() =>
                    {
                        try
                        {
                            _renderComp.RenderSpritesToTexture(
                                surfaceIndex, _asyncBuffer, _textureSize, _aspectRatio,
                                backgroundColor, renderOpacity);
                        }
                        finally
                        {
                            OnAsyncRendered();
                        }
                    }));
                }
                else
                {
                    _renderComp.RenderSpritesToTexture(RotationOrSurfaceIndex, renderList, _textureSize, _aspectRatio,
                        backgroundColor, renderOpacity);
                }
                return true;
            }
            finally
            {
                RenderingCursorOnly = false;
#if DEBUG
                Debugging.FrameProfiler.EndRender(profileStart,
                    GetType().Name + " @ " + ((Block as Sandbox.ModAPI.IMyTerminalBlock)?.CustomName ?? "?"),
                    profiledSprites, profiledUpload);
#endif
            }
        }

        /// <summary>
        /// Records the frame about to be uploaded and returns false when it matches the texture
        /// content already on the LCD. SE releases the texture while the block is out of range or
        /// not working and stops running the script meanwhile, so a gap between runs forces an upload.
        /// </summary>
        bool UpdateUploadedFrame(List<MySprite> sprites, Color backgroundColor, byte opacity, long frame)
        {
            bool same = _hasUploadedFrame &&
                        frame - _uploadedFrameTick < ForcedUploadInterval &&
                        backgroundColor == _uploadedBackground &&
                        opacity == _uploadedOpacity &&
                        sprites.Count == _uploadedFrame.Count;
            for (int i = 0; same && i < sprites.Count; i++)
                same = SameSprite(sprites[i], _uploadedFrame[i]);

            long textureVersion = _uploadedTextureVersion;
            if (!same || Adk.Gui.Lcd.LcdDynamicTexture.GlobalContentVersion != _uploadedGlobalTextureVersion)
            {
                textureVersion = 0;
                for (int i = 0; i < sprites.Count; i++)
                {
                    if (sprites[i].Type == SpriteType.TEXTURE)
                        textureVersion += Adk.Gui.Lcd.LcdDynamicTexture.GetContentVersion(sprites[i].Data);
                }
            }
            if (same && textureVersion == _uploadedTextureVersion)
            {
                _uploadedGlobalTextureVersion = Adk.Gui.Lcd.LcdDynamicTexture.GlobalContentVersion;
                return false;
            }

            _uploadedFrame.Clear();
            _uploadedFrame.AddRange(sprites);
            _uploadedBackground = backgroundColor;
            _uploadedOpacity = opacity;
            _uploadedTextureVersion = textureVersion;
            _uploadedGlobalTextureVersion = Adk.Gui.Lcd.LcdDynamicTexture.GlobalContentVersion;
            _uploadedFrameTick = frame;
            _hasUploadedFrame = true;
            return true;
        }

        protected void InvalidateUploadedFrame()
        {
            _hasUploadedFrame = false;
            _hasPublishedFrame = false;
        }

        /// <summary>
        /// Proxies render from this surface's unclipped frame, so they must be notified when content
        /// outside this surface changes (such as the cursor over a proxy) even if the clipped upload is identical.
        /// </summary>
        bool PublishProxyFrame(List<MySprite> sprites)
        {
            if (_registeredProxyOffsets.Count <= 0)
            {
                _hasPublishedFrame = false;
                return false;
            }

            bool same = _hasPublishedFrame && sprites.Count == _publishedFrame.Count;
            for (int i = 0; same && i < sprites.Count; i++)
                same = SameSprite(sprites[i], _publishedFrame[i]);
            if (same)
                return false;

            _publishedFrame.Clear();
            _publishedFrame.AddRange(sprites);
            _hasPublishedFrame = true;
            return true;
        }

        readonly List<MySprite> _publishedFrame = new List<MySprite>();
        bool _hasPublishedFrame;

        static bool SameSprite(MySprite left, MySprite right)
        {
            return left.Type == right.Type &&
                   left.Alignment == right.Alignment &&
                   left.RotationOrScale == right.RotationOrScale &&
                   left.Position == right.Position &&
                   left.Size == right.Size &&
                   left.Color == right.Color &&
                   string.Equals(left.Data, right.Data, StringComparison.Ordinal) &&
                   string.Equals(left.FontId, right.FontId, StringComparison.Ordinal);
        }

        const long ForcedUploadInterval = 600;
        const long MaxRunGap = 30;
        readonly List<MySprite> _uploadedFrame = new List<MySprite>();
        Color _uploadedBackground;
        byte _uploadedOpacity;
        long _uploadedTextureVersion;
        long _uploadedGlobalTextureVersion = -1;
        long _uploadedFrameTick;
        bool _hasUploadedFrame;

        void OnAsyncRendered()
        {
            _asyncBuffer?.Clear();
            _asyncRenderPending = false;
        }

        readonly List<MySprite> _asyncBuffer = new List<MySprite>();

        byte GetRenderOpacity()
        {
            if (ScreenAreaGeometry.IsTransparentScreenArea(Block, RotationOrSurfaceIndex))
                return GetDefaultOpacity();

            return GeneralComponent.BackgroundAlpha.Get(GetDefaultOpacity);
        }

        byte GetDefaultOpacity() => Surface.BackgroundAlpha;

        void NotifyRendered()
        {
            RaiseSurfaceEvent(OnRender);
        }

        void RaiseUpdateStart()
        {
            RaiseSurfaceEvent(OnUpdateStart);
        }

        void RaiseUpdateEnd()
        {
            RaiseSurfaceEvent(OnUpdateEnd);
        }


        void RaiseSurfaceEvent(Action<SurfaceScriptBase> handlers)
        {
            if (handlers == null)
                return;

            foreach (var @delegate in handlers.GetInvocationList())
            {
                var handler = (Action<SurfaceScriptBase>)@delegate;
                try
                {
                    handler(this);
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, this);
                }
            }
        }

        LcdTextMetrics _boundsTextMetrics;
        readonly List<MySprite> _clippedFrame = new List<MySprite>();

        /// <summary>
        /// Keeps only the sprites that can show on this surface: textures and text touching the active clip (the latest
        /// clip rect intersected with the surface), and the clip rects they need. A render proxy receives its host's
        /// whole frame, so without this any change elsewhere on the host made every proxy upload again.
        /// </summary>
        List<MySprite> ClipSpriteListToSurfaceBounds(List<MySprite> sprites)
        {
            if (!ClipToBounds || sprites == null || sprites.Count == 0)
                return sprites;

            var renderBounds = new RectangleF(0f, 0f, _textureSize.X, _textureSize.Y);
            // Used only within this render; the upload and async paths copy it.
            var renderSprites = _clippedFrame;
            renderSprites.Clear();
            bool skipClippedSprites = false;
            RectangleF activeBounds = renderBounds;
            if (_boundsTextMetrics == null && Surface != null)
                _boundsTextMetrics = new LcdTextMetrics(Surface);

            for (int i = 0; i < sprites.Count; i++)
            {
                MySprite sprite;
                var sourceSprite = sprites[i];
                if (!TryPrepareHostSpriteForRender(
                        sourceSprite,
                        renderBounds,
                        _boundsTextMetrics,
                        ref activeBounds,
                        ref skipClippedSprites,
                        out sprite))
                {
#if DEBUG
                    AddInvalidClipDebugSprite(sourceSprite, renderBounds, renderSprites);
#endif
                    continue;
                }

                // A clip rect followed by nothing visible is superseded by the next one.
                if (sprite.Type == SpriteType.CLIP_RECT && renderSprites.Count > 0 &&
                    renderSprites[renderSprites.Count - 1].Type == SpriteType.CLIP_RECT)
                    renderSprites.RemoveAt(renderSprites.Count - 1);
                renderSprites.Add(sprite);
#if DEBUG
                AddVisibleClipDebugSprites(sourceSprite, sprite, renderSprites);
#endif
            }

            return renderSprites;
        }

#if DEBUG
        static void AddVisibleClipDebugSprites(MySprite sourceSprite, MySprite renderSprite, List<MySprite> renderSprites)
        {
            if (!LocalConfigManager.VisibleClip ||
                renderSprites == null ||
                sourceSprite.Type != SpriteType.CLIP_RECT ||
                !renderSprite.Position.HasValue ||
                !renderSprite.Size.HasValue)
            {
                return;
            }

            var size = renderSprite.Size.Value;
            if (size.X <= 0f || size.Y <= 0f)
                return;

            var rect = new RectangleF(renderSprite.Position.Value, size);
            AddVisibleClipRect(rect, new Color(255, 0, 0, 55), renderSprites);
            AddVisibleClipBorder(rect, new Color(255, 0, 0, 255), renderSprites);
        }

        static void AddInvalidClipDebugSprite(MySprite sourceSprite, RectangleF renderBounds, List<MySprite> renderSprites)
        {
            if (!LocalConfigManager.VisibleClip ||
                renderSprites == null ||
                sourceSprite.Type != SpriteType.CLIP_RECT ||
                !sourceSprite.Position.HasValue ||
                !sourceSprite.Size.HasValue)
            {
                return;
            }

            var clip = NormalizeRect(new RectangleF(sourceSprite.Position.Value, sourceSprite.Size.Value));
            if (clip.Width <= 0f || clip.Height <= 0f)
                return;

            float markerSize = Math.Max(8f, Math.Min(renderBounds.Width, renderBounds.Height) * 0.025f);
            var markerCenter = new Vector2(
                MathHelper.Clamp(clip.Center.X, renderBounds.X + markerSize, renderBounds.Right - markerSize),
                MathHelper.Clamp(clip.Center.Y, renderBounds.Y + markerSize, renderBounds.Bottom - markerSize));

            renderSprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Cross",
                Position = markerCenter,
                Size = new Vector2(markerSize * 2f),
                Color = new Color(255, 0, 0, 255),
                Alignment = TextAlignment.CENTER
            });
        }

        static void AddVisibleClipRect(RectangleF rect, Color color, List<MySprite> renderSprites)
        {
            renderSprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = rect.Center,
                Size = rect.Size,
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        static void AddVisibleClipBorder(RectangleF rect, Color color, List<MySprite> renderSprites)
        {
            float thickness = Math.Max(2f, Math.Min(rect.Width, rect.Height) * 0.015f);
            AddVisibleClipRect(new RectangleF(rect.X, rect.Y, rect.Width, thickness), color, renderSprites);
            AddVisibleClipRect(new RectangleF(rect.X, rect.Bottom - thickness, rect.Width, thickness), color, renderSprites);
            AddVisibleClipRect(new RectangleF(rect.X, rect.Y, thickness, rect.Height), color, renderSprites);
            AddVisibleClipRect(new RectangleF(rect.Right - thickness, rect.Y, thickness, rect.Height), color, renderSprites);
        }
#endif

        static bool TryPrepareHostSpriteForRender(
            MySprite sprite,
            RectangleF renderBounds,
            LcdTextMetrics textMetrics,
            ref RectangleF activeBounds,
            ref bool skipClippedSprites,
            out MySprite renderSprite)
        {
            renderSprite = sprite;

            if (sprite.Type == SpriteType.CLIP_RECT)
            {
                bool keepClip = TryClipRectToRenderBounds(sprite, renderBounds, out renderSprite);
                skipClippedSprites = !keepClip;
                activeBounds = keepClip && renderSprite.Position.HasValue && renderSprite.Size.HasValue
                    ? new RectangleF(renderSprite.Position.Value, renderSprite.Size.Value)
                    : renderBounds;
                return keepClip;
            }

            if (skipClippedSprites)
                return false;

            if (sprite.Type == SpriteType.TEXTURE)
            {
                sprite = NormalizeTextureSprite(sprite, renderBounds);
                renderSprite = sprite;
                return TextureTouches(sprite, activeBounds);
            }

            if (sprite.Type == SpriteType.TEXT && textMetrics != null)
                return TextTouches(sprite, textMetrics, activeBounds);

            return true;
        }

        static bool TextTouches(MySprite sprite, LcdTextMetrics textMetrics, RectangleF bounds)
        {
            if (!sprite.Position.HasValue || string.IsNullOrEmpty(sprite.Data))
                return true;

            Vector2 size = textMetrics.Measure(sprite.FontId ?? "White", sprite.Data, sprite.RotationOrScale);
            Vector2 position = sprite.Position.Value;
            float left = position.X;
            if (sprite.Alignment == TextAlignment.CENTER)
                left -= size.X * 0.5f;
            else if (sprite.Alignment == TextAlignment.RIGHT)
                left -= size.X;
            return RectanglesTouch(new RectangleF(left, position.Y, size.X, size.Y), bounds);
        }

        static bool TextureTouches(MySprite sprite, RectangleF renderBounds)
        {
            var bounds = GetTextureBounds(sprite, renderBounds);
            return RectanglesTouch(bounds, renderBounds);
        }

        static RectangleF GetTextureBounds(MySprite sprite, RectangleF renderBounds)
        {
            var position = sprite.Position ?? renderBounds.Center;
            var size = sprite.Size ?? renderBounds.Size;
            var bounds = GetAlignedBounds(position, size, sprite.Alignment);
            if (Math.Abs(sprite.RotationOrScale) <= 0.0001f)
                return bounds;

            return GetRotatedBounds(bounds, position, sprite.RotationOrScale);
        }

        static MySprite NormalizeTextureSprite(MySprite sprite, RectangleF renderBounds)
        {
            var position = sprite.Position ?? renderBounds.Center;
            var size = sprite.Size ?? renderBounds.Size;
            if (size.X >= 0f && size.Y >= 0f)
                return sprite;

            var bounds = NormalizeRect(GetAlignedBounds(position, size, sprite.Alignment));
            var normalizedPosition = GetAlignedPosition(bounds, sprite.Alignment);

            return new MySprite(
                sprite.Type,
                sprite.Data,
                normalizedPosition,
                bounds.Size,
                sprite.Color,
                sprite.FontId,
                sprite.Alignment,
                sprite.RotationOrScale);
        }

        static RectangleF GetAlignedBounds(Vector2 position, Vector2 size, TextAlignment alignment)
        {
            switch (alignment)
            {
                case TextAlignment.CENTER:
                    return new RectangleF(position.X - size.X * 0.5f, position.Y - size.Y * 0.5f, size.X, size.Y);
                case TextAlignment.RIGHT:
                    return new RectangleF(position.X - size.X, position.Y - size.Y * 0.5f, size.X, size.Y);
                case TextAlignment.LEFT:
                default:
                    return new RectangleF(position.X, position.Y - size.Y * 0.5f, size.X, size.Y);
            }
        }

        static Vector2 GetAlignedPosition(RectangleF bounds, TextAlignment alignment)
        {
            switch (alignment)
            {
                case TextAlignment.CENTER:
                    return bounds.Center;
                case TextAlignment.RIGHT:
                    return new Vector2(bounds.Right, bounds.Center.Y);
                case TextAlignment.LEFT:
                default:
                    return new Vector2(bounds.X, bounds.Center.Y);
            }
        }

        static RectangleF GetRotatedBounds(RectangleF bounds, Vector2 pivot, float rotation)
        {
            float cos = (float)Math.Cos(rotation);
            float sin = (float)Math.Sin(rotation);

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            AddRotatedPoint(bounds.X, bounds.Y, pivot, cos, sin, ref minX, ref minY, ref maxX, ref maxY);
            AddRotatedPoint(bounds.Right, bounds.Y, pivot, cos, sin, ref minX, ref minY, ref maxX, ref maxY);
            AddRotatedPoint(bounds.Right, bounds.Bottom, pivot, cos, sin, ref minX, ref minY, ref maxX, ref maxY);
            AddRotatedPoint(bounds.X, bounds.Bottom, pivot, cos, sin, ref minX, ref minY, ref maxX, ref maxY);

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        static void AddRotatedPoint(
            float x,
            float y,
            Vector2 pivot,
            float cos,
            float sin,
            ref float minX,
            ref float minY,
            ref float maxX,
            ref float maxY)
        {
            float relativeX = x - pivot.X;
            float relativeY = y - pivot.Y;
            float rotatedX = pivot.X + relativeX * cos - relativeY * sin;
            float rotatedY = pivot.Y + relativeX * sin + relativeY * cos;

            if (rotatedX < minX)
                minX = rotatedX;
            if (rotatedY < minY)
                minY = rotatedY;
            if (rotatedX > maxX)
                maxX = rotatedX;
            if (rotatedY > maxY)
                maxY = rotatedY;
        }

        static bool TryClipRectToRenderBounds(MySprite sprite, RectangleF renderBounds, out MySprite clippedSprite)
        {
            clippedSprite = sprite;
            if (!sprite.Position.HasValue || !sprite.Size.HasValue)
                return true;

            var clip = NormalizeRect(new RectangleF(sprite.Position.Value, sprite.Size.Value));
            RectangleF clipped;
            RectangleF.Intersect(ref clip, ref renderBounds, out clipped);
            if (clipped.Width <= 0f || clipped.Height <= 0f)
                return false;

            clippedSprite = new MySprite(
                SpriteType.CLIP_RECT,
                sprite.Data,
                clipped.Position,
                clipped.Size,
                sprite.Color,
                sprite.FontId,
                sprite.Alignment,
                sprite.RotationOrScale);
            return true;
        }

        static RectangleF NormalizeRect(RectangleF rect)
        {
            float left = Math.Min(rect.X, rect.Right);
            float top = Math.Min(rect.Y, rect.Bottom);
            float right = Math.Max(rect.X, rect.Right);
            float bottom = Math.Max(rect.Y, rect.Bottom);
            return new RectangleF(left, top, right - left, bottom - top);
        }

        static bool RectanglesTouch(RectangleF a, RectangleF b)
        {
            return a.Right > b.X &&
                   a.X < b.Right &&
                   a.Bottom > b.Y &&
                   a.Y < b.Bottom;
        }

        static List<MySprite> PrepareSpritesForRender(List<MySprite> sprites)
        {
            if (sprites == null || sprites.Count == 0)
                return sprites ?? new List<MySprite>();

            List<MySprite> prepared = null;
            for (int i = 0; i < sprites.Count; i++)
            {
                var sprite = sprites[i];
                if (sprite.Type == SpriteType.TEXTURE && !string.IsNullOrWhiteSpace(sprite.Data) && char.IsNumber(sprite.Data[0]))
                {
                    if (!TextureHelper.CanRenderTexture(sprite.Data))
                    {
                        ApplyMissingTextureFallback(ref sprite);
                    }
                    else if (!TextureHelper.IsKnownTexture(sprite.Data))
                    {
                        if (TextureHelper.HasTextureParseFailed(sprite.Data))
                        {
                            ApplyMissingTextureFallback(ref sprite);
                        }
                        else
                        {
                            ulong ownerId;
                            string textureName;
                            if (TextureTransferHelper.TryParseTextureKey(sprite.Data, out ownerId, out textureName))
                            {
                                var registrationName = TextureTransferHelper.BuildTextureKey(ownerId, textureName);
                                var isKnownTexture = TextureHelper.IsKnownTexture(registrationName);
                                var isPendingTexture = TextureHelper.HasPendingTextureRequest(registrationName);

                                if (!isKnownTexture && !isPendingTexture)
                                    TextureHelper.TryQueueTextureRequest(ownerId, textureName, registrationName);

                                if (!isKnownTexture)
                                    ApplyMissingTextureFallback(ref sprite);
                            }
                            else
                            {
                                TextureHelper.MarkTextureParseFailed(sprite.Data);
                                ApplyMissingTextureFallback(ref sprite);
                            }
                        }
                    }
                }

                if (CanRenderSprite(sprite))
                {
                    prepared?.Add(sprite);
                    continue;
                }

                if (prepared == null)
                {
                    prepared = new List<MySprite>(sprites.Count);
                    for (int j = 0; j < i; j++)
                        prepared.Add(sprites[j]);
                }
            }

            return prepared ?? sprites;
        }

        static void ApplyMissingTextureFallback(ref MySprite sprite)
        {
            sprite.Data = "MissingIcon";
            if (!sprite.Size.HasValue)
                return;

            var size = sprite.Size.Value;
            var uniformSize = Math.Min(Math.Abs(size.X), Math.Abs(size.Y));
            sprite.Size = new Vector2(uniformSize, uniformSize);
        }

        static bool CanRenderSprite(MySprite sprite)
        {
            if (!IsFinite(sprite.RotationOrScale))
                return false;

            if (sprite.Position.HasValue && !IsFinite(sprite.Position.Value))
                return false;

            if (sprite.Size.HasValue && !IsFinite(sprite.Size.Value))
                return false;

            return true;
        }

        static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.X) && IsFinite(value.Y);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

#if DEBUG
        void AddSpriteCountDebug(List<MySprite> spriteList)
        {
            if (!LocalConfigManager.SpriteCountDebug || spriteList == null)
                return;

            int finalCount = spriteList.Count + 1;
            float scale = 2;
            spriteList.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = finalCount.ToString(),
                Position = Surface.TextureSize / 2f,
                Color = Color.Red,
                Alignment = TextAlignment.CENTER,
                FontId = "White",
                RotationOrScale = scale
            });
        }
#endif

        readonly List<MySprite> _frameSprites = new List<MySprite>();

        /// <summary>
        /// This script's sprite list for the frame being built, cleared and reused so a redraw does not reallocate and
        /// regrow it. Every consumer copies the frame before the next one is built.
        /// </summary>
        protected List<MySprite> BeginFrameSprites()
        {
            _frameSprites.Clear();
            return _frameSprites;
        }

        public virtual List<MySprite> GetSprites()
        {
            throw new NotImplementedException();
        }

        protected virtual List<MySprite> RenderFrame(Func<List<MySprite>> sprites)
        {
            return sprites();
        }

        void CacheFrameForProxies(IEnumerable<MySprite> spriteList)
        {
            if (_registeredProxyOffsets.Count <= 0 || spriteList == null)
                return;

            _cachedFrame.Clear();
            _cachedFrame.AddRange(spriteList);
        }

        public List<MySprite> GetCachedFrame()
        {
            if (_cachedFrame.Count > 0)
                return _cachedFrame;

            return GetSprites();
        }

        public bool RegisterProxy(long proxyKey, Vector2 offset)
        {
            Vector2 existing;
            bool hadExisting = _registeredProxyOffsets.TryGetValue(proxyKey, out existing);
            if (hadExisting && existing == offset)
                return true;

            _registeredProxyOffsets[proxyKey] = offset;
            if (!TryLayoutChangedForProxy())
            {
                if (hadExisting)
                    _registeredProxyOffsets[proxyKey] = existing;
                else
                    _registeredProxyOffsets.Remove(proxyKey);

                return false;
            }

            return true;
        }

        public bool UnregisterProxy(long proxyKey)
        {
            Vector2 existing;
            if (!_registeredProxyOffsets.TryGetValue(proxyKey, out existing))
                return true;

            _registeredProxyOffsets.Remove(proxyKey);
            if (!TryLayoutChangedForProxy())
            {
                _registeredProxyOffsets[proxyKey] = existing;
                return false;
            }

            return true;
        }

        bool TryLayoutChangedForProxy()
        {
            try
            {
                LayoutChanged();
                return true;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        public abstract void SafeRun();
    }
}
