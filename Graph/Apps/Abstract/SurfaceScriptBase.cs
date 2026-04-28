using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Generated;
using Graph.Extensions;
using Graph.Helpers;
using Graph.Panels;
using Graph.System;
using Graph.System.Config;
using Graph.System.TerminalControls.Groups;
using Graph.Apps.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.EntityComponents.Blocks;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace Graph.Apps.Abstract
{
    public abstract class SurfaceScriptBase : MyTSSCommon, IUsesTerminalControlGroup<BaseTerminalControlGroup>
    {
        static readonly Dictionary<string, Vector2> FontSizeCache = new Dictionary<string, Vector2>();
        static readonly StringBuilder StringBuilderBuffer = new StringBuilder();

        public static SurfaceCollection Instances = new SurfaceCollection();

        readonly List<MySprite> _backgroundGrids = new List<MySprite>();
        Color _backgroundColor;
        Color _foregroundColor;

        public IMyFaction Faction { get; protected set; }
        protected string Icon { get; set; }

        public Vector2 TextureSize => Surface.TextureSize;

        protected virtual SortMethod SortMethod => Config.SortMethod;

        /// <summary>
        /// Relative area of the <see cref="Sandbox.ModAPI.IMyTextSurface.TextureSize"/> That is Visible
        /// </summary>
        public RectangleF ViewBox { get; protected set; }

        protected GridLogic GridLogic;
        int _rotationOrSurfaceIndex;

        protected float CaretY;
        protected float FooterHeight;

        protected const float TITLE_BAR_HEIGHT_BASE = 40f;

        protected string LocalizedTitleCache = string.Empty;


        public virtual string Title
        {
            get
            {
                if (string.IsNullOrEmpty(LocalizedTitleCache))
                    LocalizedTitleCache = MyTexts.GetString(DefaultTitle);

                return LocalizedTitleCache;
            }
        }

        protected virtual string DefaultTitle => "|";

        protected float Scale = 1;
        protected float FontScale => _userFontScale <= 0f ? 1f : _userFontScale;
        protected float LayoutScale => Scale * FontScale;

        float _userScale;
        float _userFontScale;
        float _userPadding;
        string _cachedTitleSource;
        string _cachedTitleText;
        float _cachedTitleAvailableWidth = -1f;
        float _cachedTitleFontSize = -1f;
        bool _cachedTitleLocalized;
        public bool TitleVisible { get; private set; } = true;
        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public ScreenConfig Config { get; protected set; }

        public bool Dirty => _dirty;
        bool _dirty;
        bool _disposed;

        public ScreenProviderConfig ProviderConfig;
        protected bool IsScreenReadyToRender { get; private set; }

        protected SurfaceScriptBase(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block,
            size)
        {
            Instances.Add(this);
            if (Block != null)
                ((IMyEntity)Block).OnMarkForClose += HandleBlockMarkedForClose;
            ResolveRotationOrSurfaceIndex();
            UpdateViewBox();
            UpdateFaction(FactionHelper.GetOwnerFaction(Block as IMyTerminalBlock));
            DrawSplash();

            LcdModSessionComponent.OnLanguageChanged += LayoutChanged;
        }

        public int RotationOrSurfaceIndex => _rotationOrSurfaceIndex;

        protected void ResolveRotationOrSurfaceIndex()
        {
            if (Block is IMyTextPanel)
            {
                foreach (var component in Block.Components)
                {
                    var lcdSurfaceComponent = component as IMyLcdSurfaceComponent;
                    if (lcdSurfaceComponent == null)
                        continue;

                    _rotationOrSurfaceIndex = lcdSurfaceComponent.SelectedRotationIndex;
                    return;
                }

                _rotationOrSurfaceIndex = 0;
                return;
            }

            var surfaceProvider = Block as IMyTextSurfaceProvider;
            if (surfaceProvider == null)
                return;

            for (int i = 0; i < surfaceProvider.SurfaceCount; i++)
            {
                if (surfaceProvider.GetSurface(i) != Surface)
                    continue;

                _rotationOrSurfaceIndex = i;
                return;
            }

            MyLog.Default.Log(MyLogSeverity.Error, "Failed to find surface for {0}, defaulting to surface 0", Block);
            _rotationOrSurfaceIndex = 0;
        }


        public static Vector2 GetSizeInPixel(string text, string font, float fontSize,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            Vector2 size;
            var key = text + font + fontSize;
            if (FontSizeCache.TryGetValue(key, out size)) return size;
            StringBuilderBuffer.Clear();
            StringBuilderBuffer.Append(text);
            size = surface.MeasureStringInPixels(StringBuilderBuffer, font, fontSize);
            FontSizeCache[key] = size;
            return size;
        }

        void DrawSplash()
        {
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

        protected void EmptyWithFilters()
        {
            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                DrawMessage(sprites, LocHelper.GetLoc("ScreenBlueprintsRew_NoBlueprints"),
                    "Warning", Config.WarningColor, Config.Scale);
                DrawFooter(sprites);
                frame.AddRange(sprites);
            }
        }

        protected void Empty()
        {
            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                DrawMessage(sprites, LocHelper.Empty,
                    "Warning", Config.WarningColor, Config.Scale);
                DrawFooter(sprites);
                frame.AddRange(sprites);
            }
        }

        public void RequestRedraw()
        {
            LayoutChanged();
            _dirty = true;
            Run();
            _dirty = false;
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                if (Block != null)
                    ((IMyEntity)Block).OnMarkForClose -= HandleBlockMarkedForClose;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }

            try
            {
                if (Block != null && ProviderConfig != null)
                    ConfigManager.Save((IMyEntity)Block, ProviderConfig);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }

            // Ensure module hooks are detached even if the instance list is already out of sync.
            LcdModSessionComponent.UnhookSurfaceModules(this);
            Instances.Remove(this);
            LcdModSessionComponent.OnLanguageChanged -= LayoutChanged;
            base.Dispose();
        }

        void HandleBlockMarkedForClose(IMyEntity entity)
        {
            Dispose();
        }

        protected void UpdateViewBox()
        {
            var sizeOffset = (Surface.TextureSize - Surface.SurfaceSize) / 2;

            _userPadding = Surface.TextPadding;

            var padding = (Surface.TextPadding / 100) * Surface.SurfaceSize;
            sizeOffset += padding / 2;
            ViewBox = new RectangleF(sizeOffset.X, sizeOffset.Y, Surface.SurfaceSize.X - padding.X,
                Surface.SurfaceSize.Y - padding.Y);
        }

        public override void Run()
        {
            base.Run();
            IsScreenReadyToRender = false;

            if (Config == null)
            {
                GetSettings((IMyTextSurface)Surface, (IMyCubeBlock)Block);
                DrawLoadingScreen(1f, false);
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
                LcdModSessionComponent.Components.TryGetValue(Block.CubeGrid.EntityId, out GridLogic);

            if (GridLogic == null)
                GridLogic = LcdModSessionComponent.GetOrCreateGridLogic(Block?.CubeGrid as IMyCubeGrid);
            else
                GridLogic.MarkRequested();

            if (GridLogic == null)
            {
                DrawLoadingScreen(Config.Scale);
                return;
            }

            IsScreenReadyToRender = true;
        }

        void GetSettings(IMyTextSurface surface, IMyCubeBlock block)
        {
            var index = 0;
            IMyTextSurfaceProvider surfaceProvider = (IMyTextSurfaceProvider)block;
            while (index < surfaceProvider.SurfaceCount)
            {
                if (surface.Equals(surfaceProvider.GetSurface(index)))
                {
                    ScreenConfig config;
                    ConfigManager.LoadSettings(block, index, ref ProviderConfig, out config);
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
        protected virtual void DrawTitle(List<MySprite> frame)
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
                Color = Config.HeaderColor,
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
                Color = Config.HeaderColor,
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
                MyLog.Default.WriteLine($"[LCDMod] GetTimeStep error: {ex.Message}");
                return 0;
            }
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

        protected virtual void DrawMessage(List<MySprite> sprites, string message, string icon, Color color,
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

            var TextSprite = new MySprite
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

            sprites.Add(TextSprite.Shadow(2 * Scale));
            sprites.Add(TextSprite);
        }


        protected virtual void DrawCellBackground(List<MySprite> frame, KeyValuePair<MyItemType, double> item,
            float xStart, float xEnd, float yStart, float cellHeight, float cellPadding)
        {
            var rl = xStart + cellPadding / 2;
            var rr = xEnd - cellPadding / 2;
            var rt = yStart + cellPadding / 2;
            var rb = yStart + cellHeight - cellPadding / 2;

            var backgroundColor = item.Value == 0 ? Config.ErrorColor : Config.HeaderColor;
            var a = backgroundColor.MulValue(0.2f);
            var cellRect = new RectangleF(rl, rt, rr - rl, rb - rt);
            var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
            RectanglePanel.CreateSpritesFromRect(dropShadow, frame, a, .2f);
            RectanglePanel.CreateSpritesFromRect(cellRect, frame, backgroundColor, .2f);
        }

        protected static int GetMaxRows(IMyTextSurface surf, float listStartY, float lineHeight)
        {
            float surfH = 512f;
            try
            {
                surfH = surf.SurfaceSize.Y;
            }
            catch
            {
            }

            float available = Math.Max(0f, surfH - listStartY - 10f);
            int rows = (int)Math.Floor(available / Math.Max(1f, lineHeight));
            return rows < 1 ? 1 : rows;
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

        protected void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1)
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
            ResolveRotationOrSurfaceIndex();
            _backgroundGrids.Clear();
        }

        protected void DrawLoadingScreen(float scale = 1f, bool drawTitle = true)
        {
            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                if (drawTitle && Config != null)
                    DrawTitle(sprites);
                DrawLoadingFrame(sprites, scale);
                frame.AddRange(sprites);
            }
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


        protected void AddBackground(List<MySprite> frame, Color? color = null)
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
    }
}
