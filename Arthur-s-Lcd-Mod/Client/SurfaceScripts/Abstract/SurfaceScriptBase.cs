using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui.Controls;
using LcdMod.Client.Helpers;
using LcdMod.Client.ScreenAreas;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;
using Sandbox.Game.Components;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IMyCockpit = Sandbox.ModAPI.IMyCockpit;
using IMyShipController = Sandbox.ModAPI.IMyShipController;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using NotImplementedException = LcdMod.Common.NotImplementedException;
using ScreenConfigColorable = LcdMod.Common.Config.Models.ScreenConfigColorable;
using ScreenConfigGeneral = LcdMod.Common.Config.Models.ScreenConfigGeneral;

namespace LcdMod.Client.SurfaceScripts.Abstract
{
    public abstract class SurfaceScriptBase : MyTSSCommon, IAppHost, IUsesTerminalControlGroup<BaseTerminalControlGroup>
    {
        public static SurfaceCollection Instances = new SurfaceCollection();

        readonly List<MySprite> _backgroundGrids = new List<MySprite>();
        readonly Dictionary<long, Vector2> _registeredProxyOffsets = new Dictionary<long, Vector2>();
        readonly List<MySprite> _cachedFrame = new List<MySprite>();
        Color _backgroundColor;
        Color _foregroundColor;

        public IMyFaction Faction { get; protected set; }
        protected string Icon { get; set; }
        public new IMyCubeBlock Block { get; }

        protected virtual bool ClipToBounds => _registeredProxyOffsets.Count > 0;

        protected long WaitForFrame;
        protected long LastRenderFrame;
        public long LastRunTick { get; private set; } = long.MinValue;

        public Vector2 TextureSize => Surface.TextureSize;

        protected virtual SortMethod SortMethod => SortMethod.Amount;

        /// <summary>
        /// Relative area of the <see cref="Sandbox.ModAPI.IMyTextSurface.TextureSize"/> That is Visible
        /// </summary>
        public virtual RectangleF ViewBox { get; protected set; }

        public GridLogic GridLogic { get; private set; }

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

        public float Scale { get; set; } = 1;
        protected float FontScale => _userFontScale <= 0f ? 1f : _userFontScale;
        protected float LayoutScale => Scale * FontScale;

        float _userScale;
        float _userFontScale;
        protected float _userPadding;
        string _cachedTitleSource;
        string _cachedTitleText;
        float _cachedTitleAvailableWidth = -1f;
        float _cachedTitleFontSize = -1f;
        bool _cachedTitleLocalized;
        public bool TitleVisible { get; private set; } = true;
        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public ScreenConfigGeneral Config { get; protected set; }
        public ScreenConfigColorable ColorableConfig => Config as ScreenConfigColorable;
        protected abstract ConfigKind ConfigKind { get; }

        public bool Dirty => _dirty;
        bool _dirty;
        bool _disposed;

        public ScreenProviderConfig ProviderConfig { get; private set; }
        protected bool IsScreenReadyToRender { get; private set; }
        public event Action<SurfaceScriptBase> OnRender;

        protected SurfaceScriptBase(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block,
            size)
        {
            WaitForFrame = MyAPIGateway.Session.GameplayFrameCounter + 6 * 5; // minimum of 5 frames splash screen 
            Block = (IMyCubeBlock)base.Block;
            var terminalBlock = (IMyTerminalBlock)Block;
            terminalBlock.AppendingCustomInfo += CustomInfo;

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

        public abstract IApp App { get; }

        protected int ResolveRotationOrSurfaceIndex()
        {
            if (Block.CubeGrid.Physics == null)
                return -1;

            if (Block is IMyTextPanel)
            {
                foreach (var component in Block.Components)
                {
                    _lcdSurfaceComponent = component as IMyLcdSurfaceComponent;
                    if (_lcdSurfaceComponent == null)
                        continue;

                    return _lcdSurfaceComponent.SelectedRotationIndex;
                }

                return -1;
            }

            var surfaceProvider = Block as IMyTextSurfaceProvider;
            if (surfaceProvider == null)
                return -1;

            var currentSurfaceName = Surface.Name;

            for (int i = 0; i < surfaceProvider.SurfaceCount; i++)
            {
                if (surfaceProvider.GetSurface(i).Name != currentSurfaceName)
                    continue;

                return i;
            }

            LogHelper.Log(MyLogSeverity.Warning, "Failed to find surface {0} for {1}", Surface.Name, Block);
            return -1;
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
                "Warning", ColorableConfig.WarningColor, Config.Scale);
            DrawFooter(sprites);
        }

        protected void AddEmptySprites(List<MySprite> sprites)
        {
            AddBackground(sprites);
            DrawTitle(sprites);
            DrawMessage(sprites, LocHelper.Empty,
                "Warning", ColorableConfig.WarningColor, Config.Scale);
            DrawFooter(sprites);
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

            if (Config == null)
                return;

            var index = Config.ScreenIndex;
            if (index < 0 || providerConfig.Screens == null || index >= providerConfig.Screens.Count)
                return;

            Config = providerConfig.Screens[index];
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                if (Block != null)
                {
                    Block.OnMarkForClose -= HandleBlockMarkedForClose;
                    ((IMyTerminalBlock)Block).AppendingCustomInfo -= CustomInfo;
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
            base.Dispose();
        }

        void HandleBlockMarkedForClose(IMyEntity entity)
        {
            Dispose();
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
                return;

            LastRunTick = currentFrame;

            base.Run();

            if (ViewBox.Size == Vector2.Zero)
                UpdateViewBox();

            if (!_init)
                Init();

            if (_init)
                Instances.RefreshActiveInstance(this);

            IsScreenReadyToRender = false;

            if (Config == null)
            {
                GetSettings((IMyTextSurface)Surface, Block);
                return;
            }

            if (Math.Abs(_userPadding - Surface.TextPadding) > .01f ||
                Math.Abs(_userScale - Config.Scale) > .001f ||
                Math.Abs(_userFontScale - Surface.FontSize) > .001f ||
                BackgroundColor != _backgroundColor ||
                ForegroundColor != _foregroundColor ||
                TitleVisible != Config.TitleVisible)
                LayoutChanged();

            if (GridLogic == null)
            {
                GridLogic gridLogic;
                if (LcdModSessionComponent.Components.TryGetValue(Block.CubeGrid.EntityId, out gridLogic))
                    GridLogic = gridLogic;
            }

            if (GridLogic == null)
                GridLogic = LcdModSessionComponent.GetOrCreateGridLogic(Block?.CubeGrid);
            else
                GridLogic.MarkRequested();

            if (GridLogic == null)
            {
                DrawLoadingScreen(Config.Scale);
                return;
            }

            IsScreenReadyToRender = true;

            try
            {
                SafeRun();
            }
            catch (Exception e)
            {
                OnException(e);
            }
        }

        void Init()
        {
            try
            {
                throw new Exception("Hello DNSpy");
            }
            catch
            {
                /* workaround for Debugger.Attach() not available for Mods */
            }
            _rotationOrSurfaceIndex = ResolveRotationOrSurfaceIndex();
            var panel = Block as IMyTextPanel;
            
            if (panel != null && Instances.GetInstance(panel, _rotationOrSurfaceIndex) != null)
            {
                _cachedFrame.Clear();
                _customInfo = LocHelper.GetLoc("LcdMod_IndexConflict");
                DrawMessage(_cachedFrame, _customInfo, "Warning", Color.Red, 1);
                _customInfo += "\n" + LocHelper.GetLoc("LcdMod_IndexConflictDetails");
                panel.RefreshCustomInfo();
                return;
            }

            Instances.Add(this);
            _init = true;
        }

        void CustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            arg2.AppendLine(_customInfo);
        }

        public void OnException(Exception e)
        {
            try
            {
                var bSoD = BSoD.ShowBSoD(this, e);

                _renderComp.RenderSpritesToTexture(RotationOrSurfaceIndex, bSoD.Frame, _textureSize, _aspectRatio,
                    Surface.ScriptBackgroundColor, Surface.BackgroundAlpha);
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
                    ScreenConfigGeneral config;
                    var providerConfig = ProviderConfig;
                    ConfigManager.LoadSettings(block, index, ConfigKind, ref providerConfig, out config);
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
                Color = ColorableConfig.HeaderColor,
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
                RotationOrScale = Scale * 1.3f * FontScale,
                Color = ColorableConfig.HeaderColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            frame.Add(MySprite.CreateClearClipRect());

            CaretY += titleBarHeight;
        }

        protected virtual void DrawFooter(List<MySprite> frame)
        {
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
                if (sess == null) return 0;
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
            var topRowHeight = spacing * Scale;
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
                RotationOrScale = 1f * Scale * FontScale
            };

            sprites.Add(iconSprite.Shadow(2 * Scale));
            sprites.Add(iconSprite);

            sprites.Add(textSprite.Shadow(2 * Scale));
            sprites.Add(textSprite);
        }

        public virtual void DrawLoading(List<MySprite> sprites, float scale = 1f)
        {
            DrawLoadingFrame(sprites, scale);
        }


        protected virtual void DrawCellBackground(List<MySprite> frame, KeyValuePair<MyItemType, double> item,
            float xStart, float xEnd, float yStart, float cellHeight, float cellPadding)
        {
            var rl = xStart + cellPadding / 2;
            var rr = xEnd - cellPadding / 2;
            var rt = yStart + cellPadding / 2;
            var rb = yStart + cellHeight - cellPadding / 2;

            var backgroundColor = item.Value == 0 ? ColorableConfig.ErrorColor : ColorableConfig.HeaderColor;
            var a = backgroundColor.MulValue(0.2f);
            var cellRect = new RectangleF(rl, rt, rr - rl, rb - rt);
            var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
            RectanglePanel.CreateSpritesFromRect(dropShadow, frame, a, .2f);
            RectanglePanel.CreateSpritesFromRect(cellRect, frame, backgroundColor, .2f);
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
            Vector2 textSize = Surface.MeasureStringInPixels(sb, "White", fontSize * Scale * FontScale);

            if (textSize.X > availableWidth)
            {
                var source = sb.ToString();
                for (int i = source.Length - 1; i > 0; i--)
                {
                    sb.Clear();
                    sb.Append(FormatingHelper.TrimName(source, i));
                    textSize = Surface.MeasureStringInPixels(sb, "White", fontSize * Scale * FontScale);

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

        protected Vector2 GetAutoScale2D(float logicalWidth = 512f, float logicalHeight = 512f)
        {
            if (logicalWidth <= 0f) logicalWidth = 512f;
            if (logicalHeight <= 0f) logicalHeight = 512f;
            return new Vector2(ViewBox.Size.X / logicalWidth, ViewBox.Size.Y / logicalHeight);
        }

        protected float GetAutoScaleUniform(float logicalWidth = 512f, float logicalHeight = 512f)
        {
            var s = GetAutoScale2D(logicalWidth, logicalHeight);
            return Math.Min(s.X, s.Y) * Config.Scale;
        }

        protected virtual void LayoutChanged()
        {
            _userPadding = Surface.TextPadding;
            _userScale = Config.Scale;
            _userFontScale = Surface.FontSize;
            _backgroundColor = BackgroundColor;
            _foregroundColor = ForegroundColor;
            LocalizedTitleCache = string.Empty;
            TitleVisible = Config.TitleVisible;
            InvalidateTitleCache();
            Scale = GetAutoScaleUniform();
            UpdateViewBox();
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
            if (drawTitle && Config != null)
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
            double seconds = session != null ? session.GameplayFrameCounter / 60.0 : 0.0;
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
                RotationOrScale = Scale * FontScale
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
            if (!_backgroundGrids.Any())
            {
                color = new Color(color ?? BackgroundColor, 0.66f);
                var frameTemp = Surface.DrawFrame();
                AddBackground(frameTemp, color);
                frameTemp.AddToList(_backgroundGrids);
            }

            frame.AddRange(_backgroundGrids);
        }


        protected static void AddHeaderSprite(List<MySprite> frame, MySprite sprite)
        {
            frame.Add(sprite.Shadow(1f));
            frame.Add(sprite);
        }

        public void UpdateFaction(IMyFaction faction)
        {
            Faction = faction;
            Icon = FactionHelper.GetIcon(faction);
            FactionHelper.GetIcon(faction);
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
            var currentFrame = MyAPIGateway.Session.GameplayFrameCounter;

            if ((!force && LastRenderFrame == currentFrame) || WaitForFrame > currentFrame || _disposed)
                return;
            try
            {
                var spriteList = PrepareSpritesForRender(RenderFrame(GetSprites));
                CacheFrameForProxies(spriteList);
                var renderList = ClipToBounds ? ClipSpriteListToSurfaceBounds(spriteList) : spriteList;
                LastRenderFrame = currentFrame;
                NotifyRendered();

#if DEBUG
                AddSpriteCountDebug(renderList);
#endif

                _renderComp.RenderSpritesToTexture(RotationOrSurfaceIndex, renderList, _textureSize, _aspectRatio,
                    Surface.ScriptBackgroundColor, Surface.BackgroundAlpha);
            }
            catch (Exception e)
            {
                OnException(e);
            }
        }

        void NotifyRendered()
        {
            var handlers = OnRender;
            if (handlers == null)
                return;

            foreach (Action<SurfaceScriptBase> handler in handlers.GetInvocationList())
            {
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

        List<MySprite> ClipSpriteListToSurfaceBounds(List<MySprite> sprites)
        {
            if (!ClipToBounds || sprites == null || sprites.Count == 0)
                return sprites;

            var renderBounds = new RectangleF(0f, 0f, _textureSize.X, _textureSize.Y);
            var renderSprites = new List<MySprite>(sprites.Count);
            bool skipClippedSprites = false;

            for (int i = 0; i < sprites.Count; i++)
            {
                MySprite sprite;
                var sourceSprite = sprites[i];
                if (!TryPrepareHostSpriteForRender(
                        sourceSprite,
                        renderBounds,
                        ref skipClippedSprites,
                        out sprite))
                {
#if DEBUG
                    AddInvalidClipDebugSprite(sourceSprite, renderBounds, renderSprites);
#endif
                    continue;
                }

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
            ref bool skipClippedSprites,
            out MySprite renderSprite)
        {
            renderSprite = sprite;

            if (sprite.Type == SpriteType.CLIP_RECT)
            {
                bool keepClip = TryClipRectToRenderBounds(sprite, renderBounds, out renderSprite);
                skipClippedSprites = !keepClip;
                return keepClip;
            }

            if (skipClippedSprites)
                return false;

            if (sprite.Type == SpriteType.TEXTURE)
            {
                sprite = NormalizeTextureSprite(sprite, renderBounds);
                renderSprite = sprite;
                return TextureTouches(sprite, renderBounds);
            }

            return true;
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
                if (CanRenderSprite(sprite))
                {
                    if (prepared != null)
                        prepared.Add(sprite);
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

        public virtual List<MySprite> GetSprites()
        {
            throw new NotImplementedException();
        }

        protected virtual List<MySprite> RenderFrame(Func<List<MySprite>> sprites)
        {
            return sprites();
        }

        void CacheFrameForProxies(List<MySprite> spriteList)
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
