using System;
using System.Collections.Generic;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Layout;
using Acrux.Rendering;
using Acrux.Styling;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts;
using VRageMath;

namespace LcdMod.Client.Apps.Views
{
    /// <summary>
    /// The IDE preview's host for an app's view (named by <c>AcruxPreviewDesignHost</c> in the project file): the view as
    /// the LCD shows it, on the surface's tiled grid background and inside the app shell (<see cref="AppShellView"/>)
    /// with the app's title, background layer and footer. Views that are not an app's main view (parts, dialogs) are
    /// left as they are. A *.AcruxDesign.cs file: compiled for the preview, not packed by MDK.
    /// </summary>
    public sealed class DesignAppHost
    {
        /// <summary>How an app shows a view: its title (a localization key) and the layers it adds around it.</summary>
        sealed class AppShape
        {
            public string TitleKey;
            public Func<object, VisualElement> Background;
            public Func<object, VisualElement> Footer;
            // The view is the app's background layer itself (it has no content of its own).
            public bool ViewIsBackground;
        }

        static readonly Dictionary<string, AppShape> Apps = new Dictionary<string, AppShape>(StringComparer.Ordinal)
        {
            { "LcdMod.Client.Apps.Antenna.AntennaView", Titled(AntennaSurfaceScript.TITLE) },
            { "LcdMod.Client.Apps.ButtonPad.ButtonPadView", Titled(SurfaceScripts.ButtonPad.TITLE) },
            { "LcdMod.Client.Apps.CargoFilled.CargoFilledView", Titled(CargoFilledSurfaceScript.TITLE) },
            { "LcdMod.Client.Apps.DefenseDashboard.DefenseDashboardView", Titled(DefenseDashboardSurfaceScript.TITLE) },
            {
                "LcdMod.Client.Apps.DigitalPictureFrames.DigitalPictureFramesView",
                new AppShape { TitleKey = DigitalPictureFramesSurfaceScript.TITLE, ViewIsBackground = true }
            },
            { "LcdMod.Client.Apps.DockingAlignment.DockingAlignmentView", Titled(SurfaceScripts.DockingAlignment.TITLE) },
            { "LcdMod.Client.Apps.EnergyDashboard.EnergyDashboardView", Titled(EnergyDashboardSurfaceScript.TITLE) },
            { "LcdMod.Client.Apps.Farm.FarmView", Titled(FarmSurfaceScript.TITLE) },
            { "LcdMod.Client.Apps.Games.GamesView", Titled(GameSurfaceScript.TITLE) },
            { "LcdMod.Client.Apps.Gas.GasView", Titled(GasSurfaceScript.TITLE) },
            {
                "LcdMod.Client.Apps.Generators.GeneratorsView",
                new AppShape
                {
                    TitleKey = GeneratorsSurfaceScript.TITLE,
                    Footer = context => new Generators.GeneratorsFooterView { DataContext = context },
                }
            },
            { "LcdMod.Client.Apps.InGameClockDashboard.InGameClockDashboardView", Titled(InGameClockDashboardSurfaceScript.TITLE) },
            { "LcdMod.Client.Apps.IntegrityMonitor.IntegrityMonitorView", Titled(PreviewGridSurfaceScript.TITLE) },
            { "LcdMod.Client.Apps.SelfDiagnostic.SelfDiagnosticView", Titled(SelfDiagnosticSurfaceScript.TITLE) },
            { "LcdMod.Client.Apps.TargetInfo.TargetInfoView", Titled(TargetInfoSurfaceScript.TITLE) },
            { "LcdMod.Client.Apps.Inventory.InventoryView", Titled(InventoryLcdSurfaceScript.NAME) },
            { "LcdMod.Client.Apps.Markdown.MarkdownView", Titled(MarkdownSurfaceScript.TITLE) },
            {
                "LcdMod.Client.Apps.MediaPlayer.MediaPlayerView",
                new AppShape
                {
                    TitleKey = MediaPlayerSurfaceScript.TITLE,
                    Background = context => new MediaPlayer.MediaVisualizerView { DataContext = context },
                }
            },
            { "LcdMod.Client.Apps.NpcMarket.NpcMarketView", Titled(NpcMarketSurfaceScript.TITLE) },
            {
                "LcdMod.Client.Apps.PlanetaryMap.PlanetaryMapView",
                new AppShape
                {
                    TitleKey = PlanetaryMapSurfaceScript.TITLE,
                    Background = context => new PlanetaryMap.PlanetaryMapSceneView { DataContext = context },
                }
            },
            { "LcdMod.Client.Apps.PowerFilled.PowerFilledView", Titled(PowerFilledSurfaceScript.TITLE) },
            {
                "LcdMod.Client.Apps.Radar.RadarView",
                new AppShape
                {
                    TitleKey = RadarSurfaceScript.TITLE,
                    Background = context => new Radar.RadarSceneView { DataContext = context },
                }
            },
            {
                "LcdMod.Client.Apps.StarMap.StarMapView",
                new AppShape
                {
                    TitleKey = StarMapSurfaceScript.TITLE,
                    Background = context => new StarMap.StarMapSceneView { DataContext = context },
                }
            },
            { "LcdMod.Client.Apps.Thrust.ThrustView", Titled(ThrustSurfaceScript.TITLE) },
        };

        static AppShape Titled(string titleKey)
        {
            return new AppShape { TitleKey = titleKey };
        }

        /// <summary>The previewed view's x:Class.</summary>
        public string ViewClass { get; set; }

        /// <summary>The previewed view's design data context (what the app gives the view, and its other layers).</summary>
        public object DesignContext { get; set; }

        /// <summary>The previewed view's root element (a dialog's Window, for example).</summary>
        public VisualElement ViewRoot { get; set; }

        /// <summary>The previewed view's content.</summary>
        public VisualElement Content { get; set; }

        /// <summary>The surface with the app around <see cref="Content"/>; null for views that are not an app's main view.</summary>
        public VisualElement View
        {
            get
            {
                AppShape shape;
                if (ViewClass == null || !Apps.TryGetValue(ViewClass, out shape))
                    return DialogView();
                var shell = new DesignShell
                {
                    Title = LocHelper.GetLoc(shape.TitleKey),
                    Content = shape.ViewIsBackground ? null : Content,
                    BackgroundContent = shape.ViewIsBackground ? Content : shape.Background == null ? null : shape.Background(DesignContext),
                    Footer = shape.Footer == null ? null : shape.Footer(DesignContext),
                };
                // As AppView: the app resources above the shell before its bindings apply.
                var surface = new Grid();
                AppViewResources.AddTo((key, value) => surface.Resources.Add(key, value));
                surface.Add(GridBackground());
                var shellView = new AppShellView();
                surface.Add(shellView);
                shellView.DataContext = shell;
                return surface;
            }
        }

        // A dialog (a Window view) as an app shows it on the surface (AppBase.ShowDialog, DialogService.ShowModal):
        // over the surface's background, with the dialog font size and the compact full-screen layout on short screens.
        VisualElement DialogView()
        {
            var dialog = ViewRoot as Window;
            if (dialog == null)
                return null;
            Dialogs.DialogService.Prepare(dialog, .65f * DesignSurface.FontScale);
            var surface = new Grid();
            AppViewResources.AddTo((key, value) => surface.Resources.Add(key, value));
            surface.Add(GridBackground());
            surface.Add(new DesignDialogSurface(Dialogs.DialogService.CompactLayout.For(dialog)) { Content = Content });
            return surface;
        }

        // The game's LCD background, as the surface script draws it under every app.
        static VisualElement GridBackground()
        {
            return new DesignSurfaceGrid { Tint = new Color(DesignSurface.Background, 0.66f) };
        }

        /// <summary>An app shell's state for a previewed view: its title shown, content in the middle row.</summary>
        sealed class DesignShell : Adk.Mvvm.ObservableObject, IAppShell
        {
            public string Title { get; set; }
            public float SurfaceFontSize => DesignSurface.FontScale;
            public bool ChromeVisible => true;
            public VisualElement Content { get; set; }
            public VisualElement BackgroundContent { get; set; }
            public VisualElement Footer { get; set; }
            public bool FooterVisible => Footer != null;
            public int ContentRow => 1;
            public int RowSpan => 1;
        }
    }
}

namespace LcdMod.Client.Apps.Views
{
    /// <summary>
    /// The game's default LCD background (MyTextSurfaceHelper.DEFAULT_BACKGROUND: the "Grid" sprite) tiled over the
    /// element and tinted, with the surface script's tile size and offsets (SurfaceScriptBase.AddTiledBackground).
    /// A *.AcruxDesign.cs file: compiled for the preview, not packed by MDK.
    /// </summary>
    internal sealed class DesignSurfaceGrid : VisualElement
    {
        const float TileSize = 682.6667f;
        const float TileOffsetX = -341.33334f;

        public Color Tint { get; set; } = Color.White;

        protected override void DrawSelf(RenderContext context)
        {
            if (Bounds.Width <= 0f || Bounds.Height <= 0f)
                return;
            TextureSource grid = TextureSource.FromKey("Grid");
            float half = TileSize * .5f;
            var baseCenter = new Vector2(Bounds.Center.X + TileOffsetX, Bounds.Center.Y);
            float startX = FirstCenter(Bounds.X, baseCenter.X, half);
            float startY = FirstCenter(Bounds.Y, baseCenter.Y, half);
            for (float y = startY; y - half < Bounds.Bottom; y += TileSize)
                for (float x = startX; x - half < Bounds.Right; x += TileSize)
                    context.DrawTexture(grid, new RectangleF(x - half, y - half, TileSize, TileSize), Tint);
        }

        static float FirstCenter(float boundsStart, float baseCenter, float half)
        {
            return baseCenter + (float)Math.Floor((boundsStart - (baseCenter - half)) / TileSize) * TileSize;
        }
    }
}

namespace LcdMod.Client.Apps.Views
{
    /// <summary>
    /// Holds a previewed dialog and switches it to the compact full-screen layout on short surfaces, as
    /// DialogService.CompactLayout does with the dialog's owner window in game. A *.AcruxDesign.cs file.
    /// </summary>
    internal sealed class DesignDialogSurface : UserControl
    {
        readonly Dialogs.DialogService.CompactLayout _layout;

        public DesignDialogSurface(Dialogs.DialogService.CompactLayout layout)
        {
            _layout = layout;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _layout.Apply(availableSize);
            return base.MeasureOverride(availableSize);
        }
    }
}
