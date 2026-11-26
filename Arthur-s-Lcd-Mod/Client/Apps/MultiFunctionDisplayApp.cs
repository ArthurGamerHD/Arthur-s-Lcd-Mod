using System;
using System.Collections.Generic;
using System.Linq;
using Acrux.Controls;
using Generated;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.MultiFunction;
using LcdMod.Client.ClockDashboard;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps
{
    /// <summary>
    /// A display that runs other apps: full screen pages switched from a bar (MFD mode), or movable windows with a
    /// taskbar (windows mode). Every opened app keeps its own config inside this surface's config.
    /// </summary>
    [LcdApp(34, Name = "MultiFunctionDisplay")]
    [ConfigComponent(LcdMod.Common.Helpers.Constants.APP, typeof(MultiFunctionConfigComponent), PropertyName = "MultiFunctionComponent")]
    [ConfigComponent(LcdMod.Common.Helpers.Constants.TABS, typeof(TabContainerConfigComponent), PropertyName = "TabsComponent")]
    internal sealed partial class MultiFunctionDisplayApp : App
    {
        struct TerminalSettingControl
        {
            public TerminalSettingControl(TerminalControlsWrapper wrapper)
            {
                Wrapper = wrapper;
            }

            public readonly TerminalControlsWrapper Wrapper;
        }

        enum SnapZone
        {
            None,
            Full,
            Left,
            Right,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        readonly SurfaceScriptBase _script;
        readonly MultiFunctionViewModel _model;
        readonly List<HostedApp> _apps = new List<HostedApp>();
        AppSettingsViewModel _settingsModel;
        Window _root;
        MultiFunctionBarView _bar;
        StartMenuView _startMenu;
        SnapPreviewView _snapPreview;
        AppSettingsView _settingsWindow;
        HostedApp _settingsTarget;
        HostedApp _lastActive;
        HostedApp _current;
        HostedApp _dragged;
        HostedApp _resized;
        const long LAYOUT_SETTLE_FRAMES = 100;
        MultiFunctionMode _mode;
        MenuPlacement _placement;
        bool _hasLayout;
        Vector2 _viewport;
        RectangleF _workArea;
        float _barSize;
        bool _primaryDown;
        Vector2 _pointer = new Vector2(float.NaN);
        SnapZone _snap;
        long _startMenuClosedFrame = long.MinValue / 2;
        // A click on the start button deactivates, and so closes, the open menu before the button's command runs.
        const long START_MENU_REOPEN_FRAMES = 20;
        bool _configDirty;

        public MultiFunctionDisplayApp(SurfaceScriptBase script) : base(script)
        {
            _script = script;
            _model = new MultiFunctionViewModel(ToggleStartMenu, () => MovePage(-1), () => MovePage(1), OpenSettingsForActive,
                index =>
                {
                    if (index >= 0 && index < _apps.Count)
                        SelectPage(_apps[index]);
                });
            _model.StartMenuTitle = LocHelper.GetLoc(MOD_PREFIX + "MultiFunction_Applications");
            var entries = new List<StartMenuEntry>();
            foreach (var info in MultiFunctionAppRegistry.Apps)
                entries.Add(new StartMenuEntry(info, OpenApp));
            _model.StartEntries = entries;
            SetContent(new MultiFunctionDesktopView { DataContext = _model });
            SetContentLayout(true);
        }

        internal override float WindowRenderScaling => CardScale;

        public override bool IsDirty
        {
            get
            {
                if (base.IsDirty)
                    return true;
                for (int i = 0; i < _apps.Count; i++)
                    if (_apps[i].App.IsDirty)
                        return true;
                return false;
            }
        }

        // The hosted apps draw inside this app's retained view, so their frames complete with it.
        internal void CompleteHostedRender()
        {
            for (int i = 0; i < _apps.Count; i++)
                _apps[i].App.CompleteRetainedRender();
        }

        /// <summary>Takes the root window of this app's view, which every page, window and bar is shown above.</summary>
        internal void Attach(Window root)
        {
            if (ReferenceEquals(_root, root) || root == null || !root.IsOpen)
                return;
            CloseWindows();
            for (int i = 0; i < _apps.Count; i++)
                CloseHostedApp(_apps[i]);
            _apps.Clear();
            _model.OpenAppList.Clear();
            _current = null;
            _lastActive = null;
            _root = root;
            _bar = new MultiFunctionBarView { DataContext = _model, ShowActivated = false };
            _bar.Show(root);
            _hasLayout = false;
            RestoreApps();
        }

        public override void Update()
        {
            if (_root == null)
                return;
            RebindConfigs();
            ApplyConfig();
            UpdateLayout();
            for (int i = 0; i < _apps.Count; i++)
            {
                HostedApp app = _apps[i];
                SyncWindowState(app);
                if (app.Window != null)
                    app.Window.SetContentScale(app.App.WindowRenderScaling);
                UpdateViewBox(app);
                try
                {
                    app.App.Update();
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, app.App);
                }
            }
            UpdateClock();
            FlushSettingValues();
            if (_configDirty)
            {
                _configDirty = false;
                ConfigManager.Sync(Host.Block, Host.ProviderConfig);
            }
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            SetContentLayout(true);
            _hasLayout = false;
            for (int i = 0; i < _apps.Count; i++)
                SafeLayoutChanged(_apps[i]);
        }

        public override void Close()
        {
            CloseWindows();
            for (int i = 0; i < _apps.Count; i++)
                CloseHostedApp(_apps[i]);
            _apps.Clear();
            _model.OpenAppList.Clear();
            base.Close();
        }

        void CloseWindows()
        {
            if (_settingsWindow != null && _settingsWindow.IsOpen)
                _settingsWindow.Close();
            foreach (Window window in new Window[] { _bar, _startMenu, _snapPreview, _settingsWindow })
                if (window != null)
                    window.Dispose();
            _bar = null;
            _startMenu = null;
            _snapPreview = null;
            _settingsWindow = null;
            for (int i = 0; i < _apps.Count; i++)
            {
                // Cleared first: a closed window with its app still attached means the user closed the app.
                HostedAppView window = _apps[i].Window;
                _apps[i].Window = null;
                if (window != null)
                    window.Dispose();
            }
            _root = null;
        }


        // A config sync (from this client's own change too) replaces the surface's config with a copy: the hosted apps and
        // their settings rows must follow it, or what they change is no longer saved.
        void RebindConfigs()
        {
            var tabs = TabsComponent;
            if (tabs == null || tabs.Apps == null)
                return;
            for (int i = _apps.Count - 1; i >= 0; i--)
            {
                HostedApp app = _apps[i];
                if (tabs.Apps.Contains(app.Config))
                    continue;
                AppInstanceConfig current = null;
                foreach (var config in tabs.Apps)
                    if (config != null && config.InstanceId == app.Config.InstanceId && config.AppTypeId == app.Config.AppTypeId)
                        current = config;
                if (current == null)
                {
                    RemoveApp(app);
                    continue;
                }
                AppSchemaRegistry.EnsureSchema(current, app.Info.AppType);
                app.Rebind(current);
                SafeLayoutChanged(app);
                if (_settingsModel != null && ReferenceEquals(_settingsTarget, app) && _settingsWindow != null && _settingsWindow.IsOpen)
                    _settingsModel.Rows = BuildSettingRows(app);
            }
        }

        void ApplyConfig()
        {
            var component = MultiFunctionComponent;
            if (_hasLayout && component.Mode == _mode && component.MenuPlacement == _placement)
                return;
            bool modeChanged = component.Mode != _mode || !_hasLayout;
            _mode = component.Mode;
            _placement = component.MenuPlacement;
            _model.IsPagesMode = _mode == MultiFunctionMode.Pages;
            _hasLayout = false;
            if (modeChanged)
            {
                CloseStartMenu();
                for (int i = 0; i < _apps.Count; i++)
                    ApplyMode(_apps[i]);
                if (_current == null && _apps.Count > 0)
                    _current = _apps[0];
                ShowCurrentPage();
            }
            UpdateSettingsTarget();
        }

        void MarkConfigDirty()
        {
            _configDirty = true;
        }

        void UpdateLayout()
        {
            Vector2 viewport = Host.ViewBox.Size / RootScale;
            if (_hasLayout && viewport == _viewport)
                return;
            _hasLayout = true;
            _viewport = viewport;
            _barSize = MultiFunctionBarView.THICKNESS;
            _model.IsHorizontal = _placement == MenuPlacement.Bottom || _placement == MenuPlacement.Top;

            RectangleF bar;
            switch (_placement)
            {
                case MenuPlacement.Top:
                    bar = new RectangleF(0, 0, viewport.X, _barSize);
                    _workArea = new RectangleF(0, _barSize, viewport.X, viewport.Y - _barSize);
                    break;
                case MenuPlacement.Left:
                    bar = new RectangleF(0, 0, _barSize, viewport.Y);
                    _workArea = new RectangleF(_barSize, 0, viewport.X - _barSize, viewport.Y);
                    break;
                case MenuPlacement.Right:
                    bar = new RectangleF(viewport.X - _barSize, 0, _barSize, viewport.Y);
                    _workArea = new RectangleF(0, 0, viewport.X - _barSize, viewport.Y);
                    break;
                default:
                    bar = new RectangleF(0, viewport.Y - _barSize, viewport.X, _barSize);
                    _workArea = new RectangleF(0, 0, viewport.X, viewport.Y - _barSize);
                    break;
            }
            Place(_bar, bar);
            // Hosted windows maximize inside the root's padding, so a maximized window leaves the bar uncovered.
            _root.Padding = new Vector4(_workArea.X, _workArea.Y, viewport.X - _workArea.Right, viewport.Y - _workArea.Bottom);

            for (int i = 0; i < _apps.Count; i++)
                ApplyMode(_apps[i]);
            if (_startMenu != null && _startMenu.IsOpen)
                PlaceStartMenu();
            CloseSnapPreview();
        }

        // Surface pixels per unit of this app's view: its own scale setting.
        float RootScale
        {
            get
            {
                float scale = WindowRenderScaling;
                return scale > 0f && !float.IsNaN(scale) && !float.IsInfinity(scale) ? scale : 1f;
            }
        }

        // The size last asked for: Width and Height are set before the frame is laid out again.
        static Vector2 WindowSize(Window window)
        {
            return new Vector2(window.Width ?? window.FrameBounds.Width, window.Height ?? window.FrameBounds.Height);
        }

        static void Place(Window window, RectangleF rect)
        {
            if (window == null)
                return;
            window.Width = Math.Max(1f, rect.Width);
            window.Height = Math.Max(1f, rect.Height);
            window.Position = rect.Position;
        }

        void ApplyMode(HostedApp app)
        {
            HostedAppView window = app.Window;
            if (window == null)
                return;
            if (_mode == MultiFunctionMode.Pages)
            {
                window.WindowDecorations = WindowDecorations.None;
                window.CanResize = false;
                window.CanMinimize = false;
                window.CanMaximize = false;
                Place(window, _workArea);
                window.WindowState = ReferenceEquals(app, _current) ? WindowState.Normal : WindowState.Minimized;
                return;
            }
            window.WindowDecorations = WindowDecorations.Full;
            window.CanResize = true;
            window.CanMinimize = true;
            window.CanMaximize = true;
            Place(window, FromPlacement(app.Config.Window ?? (app.Config.Window = DefaultPlacement())));
            window.WindowState = app.Config.Window.Minimized ? WindowState.Minimized
                : app.Config.Window.Maximized ? WindowState.Maximized : WindowState.Normal;
        }

        AppWindowPlacement DefaultPlacement()
        {
            float offset = .05f * (_apps.Count % 5);
            return new AppWindowPlacement { X = .05f + offset, Y = .05f + offset, Width = .6f, Height = .65f };
        }

        RectangleF FromPlacement(AppWindowPlacement placement)
        {
            float width = MathHelper.Clamp(placement.Width, .1f, 1f) * _workArea.Width;
            float height = MathHelper.Clamp(placement.Height, .1f, 1f) * _workArea.Height;
            float x = _workArea.X + MathHelper.Clamp(placement.X, 0f, 1f) * _workArea.Width;
            float y = _workArea.Y + MathHelper.Clamp(placement.Y, 0f, 1f) * _workArea.Height;
            return Clamp(new RectangleF(x, y, width, height));
        }

        RectangleF Clamp(RectangleF rect)
        {
            float width = Math.Min(rect.Width, _workArea.Width);
            float height = Math.Min(rect.Height, _workArea.Height);
            float x = MathHelper.Clamp(rect.X, _workArea.X, _workArea.Right - width);
            float y = MathHelper.Clamp(rect.Y, _workArea.Y, _workArea.Bottom - height);
            return new RectangleF(x, y, width, height);
        }

        void StorePlacement(HostedApp app)
        {
            if (app.Window != null)
                StorePlacement(app, new RectangleF(app.Window.Position, WindowSize(app.Window)));
        }

        // The rect is passed in: a window's own frame only follows a move at its next layout.
        void StorePlacement(HostedApp app, RectangleF frame)
        {
            HostedAppView window = app.Window;
            if (window == null || _mode != MultiFunctionMode.Windows || _workArea.Width <= 0f || _workArea.Height <= 0f)
                return;
            var placement = app.Config.Window ?? (app.Config.Window = new AppWindowPlacement());
            placement.X = (frame.X - _workArea.X) / _workArea.Width;
            placement.Y = (frame.Y - _workArea.Y) / _workArea.Height;
            placement.Width = frame.Width / _workArea.Width;
            placement.Height = frame.Height / _workArea.Height;
            placement.Minimized = window.WindowState == WindowState.Minimized;
            placement.Maximized = window.WindowState == WindowState.Maximized;
            MarkConfigDirty();
        }

        // While the pointer is held (a window being resized) the app's own layout waits until the size has been stable for
        // LAYOUT_SETTLE_FRAMES or the pointer is released: rebuilding it on every move would stall the game.
        void UpdateViewBox(HostedApp app)
        {
            var content = app.Window == null ? null : app.Window.Content;
            if (content == null || content.Bounds.Width <= 0f || content.Bounds.Height <= 0f)
                return;
            long frame = MyAPIGateway.Session.GameplayFrameCounter;
            RectangleF bounds = content.Bounds;
            if (!app.PendingViewBox.HasValue || !SameRect(app.PendingViewBox.Value, bounds))
            {
                app.PendingViewBox = bounds;
                app.PendingSince = frame;
            }
            if (_primaryDown && frame - app.PendingSince < LAYOUT_SETTLE_FRAMES)
                return;
            app.PendingViewBox = null;
            // The view is laid out in units scaled around the surface's view box; the hosted app's area is in pixels.
            Vector2 origin = Host.ViewBox.Position;
            float scale = RootScale;
            bounds = new RectangleF(origin + (bounds.Position - origin) * scale, bounds.Size * scale);
            if (app.Host.SetViewBox(bounds))
                SafeLayoutChanged(app);
        }

        static bool SameRect(RectangleF left, RectangleF right)
        {
            return Math.Abs(left.X - right.X) < .5f && Math.Abs(left.Y - right.Y) < .5f &&
                   Math.Abs(left.Width - right.Width) < .5f && Math.Abs(left.Height - right.Height) < .5f;
        }

        static void SafeLayoutChanged(HostedApp app)
        {
            try
            {
                app.App.LayoutChanged();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, app.App);
            }
        }

        // The caption's minimize and maximize buttons change the state without an event.
        void SyncWindowState(HostedApp app)
        {
            WindowState state = app.Window == null ? WindowState.Normal : app.Window.WindowState;
            bool minimized = state == WindowState.Minimized;
            bool maximized = state == WindowState.Maximized;
            if (app.IsMinimized == minimized && app.IsMaximized == maximized)
                return;
            app.IsMinimized = minimized;
            app.IsMaximized = maximized;
            if (_mode == MultiFunctionMode.Windows)
                StorePlacement(app);
        }

        // The same time as the In-Game Clock app: local solar time on a planet. It follows the sun, so it is read again
        // every CLOCK_READ_FRAMES rather than once a game minute.
        const long CLOCK_READ_FRAMES = 60;
        readonly ClockEnvironmentReader _clockReader = new ClockEnvironmentReader();
        ClockDashboardSnapshot _clock;
        long _clockReadFrame = long.MinValue / 2;

        void UpdateClock()
        {
            if (MyAPIGateway.Session == null || !_model.IsWindowsMode)
                return;
            long frame = MyAPIGateway.Session.GameplayFrameCounter;
            if (frame - _clockReadFrame < CLOCK_READ_FRAMES)
                return;
            _clockReadFrame = frame;
            try
            {
                _clock = _clockReader.ReadClockOnly(Host.Block, _clock);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                return;
            }
            _model.ClockText = _clock.DisplayDateTime.ToString("HH:mm");
        }


        void RestoreApps()
        {
            var tabs = TabsComponent;
            tabs.NormalizeAppInstanceIds();
            foreach (var config in tabs.Apps.ToArray())
            {
                AppType appType;
                if (!AppSchemaRegistry.TryNormalizeAppType(config.AppTypeId, out appType))
                    continue;
                var info = MultiFunctionAppRegistry.Find(appType);
                if (info == null)
                    continue;
                if (FindOpen(info.AppType) != null)
                {
                    // Only one instance of an app type is open; an extra one written before that rule keeps its settings.
                    tabs.Apps.Remove(config);
                    if (!MultiFunctionComponent.HasClosedApp(config.AppTypeId))
                        MultiFunctionComponent.StoreClosedApp(config);
                    MarkConfigDirty();
                    continue;
                }
                var app = CreateHosted(config, info);
                if (app != null && config.InstanceId == tabs.ActiveAppInstanceId)
                    _current = app;
            }
            if (_current == null && _apps.Count > 0)
                _current = _apps[0];
            UpdatePageState();
        }

        HostedApp FindOpen(AppType appType)
        {
            for (int i = 0; i < _apps.Count; i++)
                if (_apps[i].Info.AppType == appType)
                    return _apps[i];
            return null;
        }

        void OpenApp(MultiFunctionAppInfo info)
        {
            CloseStartMenu();
            var open = FindOpen(info.AppType);
            if (open != null)
            {
                if (_mode == MultiFunctionMode.Pages)
                    SelectPage(open);
                else if (open.Window != null)
                {
                    open.Window.Activate();
                    SyncWindowState(open);
                }
                return;
            }
            var tabs = TabsComponent;
            var saved = MultiFunctionComponent.TakeClosedApp((int)info.AppType);
            var config = saved ?? new AppInstanceConfig
            {
                AppTypeId = (int)info.AppType,
                Title = info.NameKey
            };
            config.InstanceId = tabs.AllocateAppInstanceId();
            if (config.Window != null)
                config.Window.Minimized = false;
            AppSchemaRegistry.EnsureSchema(config, info.AppType);
            tabs.Apps.Add(config);
            var app = CreateHosted(config, info);
            if (app == null)
            {
                tabs.Apps.Remove(config);
                if (saved != null)
                    MultiFunctionComponent.StoreClosedApp(saved);
                return;
            }
            _hasLayout = false;
            UpdateLayout();
            ApplyMode(app);
            if (_mode == MultiFunctionMode.Pages)
                SelectPage(app);
            else
                app.Window.Activate();
            MarkConfigDirty();
        }

        HostedApp CreateHosted(AppInstanceConfig config, MultiFunctionAppInfo info)
        {
            AppSchemaRegistry.EnsureSchema(config, info.AppType);
            var host = new HostedAppHost(_script, config, info);
            App app;
            try
            {
                app = MultiFunctionAppRegistry.Create(info.AppType, host);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                return null;
            }
            if (app == null)
                return null;

            var hosted = new HostedApp(config, info, host, app, OnTaskClicked, SelectPage);
            var window = new HostedAppView(app) { Title = info.Name };
            hosted.Window = window;
            // Dialogs open on the app's own window: drawn and laid out with it, hidden with its page, blocking only it.
            host.DialogOwner = window;
            window.Activated += () => OnActivated(hosted);
            window.Deactivated += () => hosted.IsActive = false;
            window.PositionChanged += () => OnWindowMoved(hosted);
            window.Resized += () => OnWindowResized(hosted);
            window.Closed += () => OnWindowClosed(hosted);
            _apps.Add(hosted);
            _model.OpenAppList.Add(hosted);
            if (_root != null)
            {
                window.Show(_root);
                ApplyMode(hosted);
            }
            SafeLayoutChanged(hosted);
            UpdatePageState();
            return hosted;
        }

        void CloseApp(HostedApp app)
        {
            if (app == null || !_apps.Contains(app))
                return;
            TabsComponent.Apps.Remove(app.Config);
            MultiFunctionComponent.StoreClosedApp(app.Config);
            RemoveApp(app);
            MarkConfigDirty();
        }

        // Takes the app off the display without touching its config.
        void RemoveApp(HostedApp app)
        {
            if (app == null || !_apps.Contains(app))
                return;
            int index = _apps.IndexOf(app);
            _apps.Remove(app);
            _model.OpenAppList.Remove(app);
            if (ReferenceEquals(_settingsTarget, app) && _settingsWindow != null && _settingsWindow.IsOpen)
                _settingsWindow.Close();
            if (ReferenceEquals(_lastActive, app))
                _lastActive = null;
            if (ReferenceEquals(_current, app))
                _current = _apps.Count == 0 ? null : _apps[Math.Min(index, _apps.Count - 1)];
            CloseHostedApp(app);
            ShowCurrentPage();
            UpdateSettingsTarget();
            Host.RenderSprites();
        }

        void CloseHostedApp(HostedApp app)
        {
            HostedAppView window = app.Window;
            app.Window = null;
            if (window != null)
                window.Dispose();
            try
            {
                app.App.Close();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, app.App);
            }
        }

        void OnWindowClosed(HostedApp app)
        {
            // Disposing our own windows also closes them; only a close the user asked for removes the app.
            if (app.Window != null)
                CloseApp(app);
        }

        void OnActivated(HostedApp app)
        {
            app.IsActive = true;
            _lastActive = app;
            UpdateSettingsTarget();
        }

        void OnTaskClicked(HostedApp app)
        {
            HostedAppView window = app.Window;
            if (window == null)
                return;
            if (ReferenceEquals(_lastActive, app) && window.WindowState != WindowState.Minimized &&
                IsTopWindow(window))
                window.WindowState = WindowState.Minimized;
            else
                window.Activate();
            SyncWindowState(app);
        }

        bool IsTopWindow(Window window)
        {
            var windows = _root.OwnedWindows;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var candidate = windows[i] as HostedAppView;
                if (candidate == null || candidate.WindowState == WindowState.Minimized)
                    continue;
                return ReferenceEquals(candidate, window);
            }
            return false;
        }


        void MovePage(int step)
        {
            if (_apps.Count == 0)
                return;
            int index = _current == null ? 0 : _apps.IndexOf(_current);
            index = ((index + step) % _apps.Count + _apps.Count) % _apps.Count;
            SelectPage(_apps[index]);
        }

        void SelectPage(HostedApp app)
        {
            if (_mode == MultiFunctionMode.Windows)
            {
                OnTaskClicked(app);
                return;
            }
            _current = app;
            ShowCurrentPage();
            MarkConfigDirty();
        }

        void ShowCurrentPage()
        {
            if (_mode == MultiFunctionMode.Pages)
                for (int i = 0; i < _apps.Count; i++)
                    if (_apps[i].Window != null)
                        _apps[i].Window.WindowState = ReferenceEquals(_apps[i], _current) ? WindowState.Normal : WindowState.Minimized;
            if (_current != null)
                TabsComponent.ActiveAppInstanceId = _current.Config.InstanceId;
            if (_mode == MultiFunctionMode.Pages && _current != null)
                _lastActive = _current;
            UpdatePageState();
        }

        void UpdatePageState()
        {
            for (int i = 0; i < _apps.Count; i++)
                _apps[i].IsCurrent = ReferenceEquals(_apps[i], _current);
            _model.CanSwitchPage = _apps.Count > 1;
            _model.HasSettingsTarget = SettingsTarget != null;
            _model.RaisePagesChanged();
            _model.SetCurrentPageIndex(_current == null ? -1 : _apps.IndexOf(_current));
        }

        HostedApp SettingsTarget => _mode == MultiFunctionMode.Pages ? _current : _lastActive ?? _current;


        void ToggleStartMenu()
        {
            if (_root == null)
                return;
            if (_startMenu != null && _startMenu.IsOpen)
            {
                CloseStartMenu();
                return;
            }
            if (MyAPIGateway.Session.GameplayFrameCounter - _startMenuClosedFrame < START_MENU_REOPEN_FRAMES)
                return;
            if (_startMenu == null)
            {
                _startMenu = new StartMenuView { DataContext = _model };
                _startMenu.Deactivated += () =>
                {
                    if (_startMenu != null && _startMenu.IsOpen)
                        CloseStartMenu();
                };
            }
            PlaceStartMenu();
            _startMenu.Show(_root);
            _model.IsStartMenuOpen = true;
        }

        void CloseStartMenu()
        {
            if (_startMenu == null || !_startMenu.IsOpen)
                return;
            _startMenu.Close();
            _model.IsStartMenuOpen = false;
            _startMenuClosedFrame = MyAPIGateway.Session.GameplayFrameCounter;
        }

        void PlaceStartMenu()
        {
            float width = MathHelper.Clamp(_viewport.X * .5f, Math.Min(_viewport.X, 160f), Math.Max(160f, _viewport.X * .5f));
            float height = Math.Min(_workArea.Height, _barSize * (_model.StartEntries.Count + 1.6f) + 12f);
            float x = _workArea.X, y;
            switch (_placement)
            {
                case MenuPlacement.Top:
                    y = _workArea.Y;
                    break;
                case MenuPlacement.Left:
                case MenuPlacement.Right:
                    y = _workArea.Y;
                    if (_placement == MenuPlacement.Right)
                        x = _workArea.Right - width;
                    break;
                default:
                    y = _workArea.Bottom - height;
                    break;
            }
            Place(_startMenu, new RectangleF(x, y, Math.Min(width, _workArea.Width), height));
        }


        void UpdateSettingsTarget()
        {
            _model.HasSettingsTarget = SettingsTarget != null;
        }

        // Controls of the display's own surface, not of the app it hosts.
        static readonly HashSet<string> DisplayControls = new HashSet<string>
        {
            MOD_PREFIX + "TitleSwitch",
            MOD_PREFIX + "CursorScaleSlider",
            MOD_PREFIX + "SliderBrightness",
            MOD_PREFIX + "SwitchToggleRequiresAlt",
            MOD_PREFIX + "CopySettings",
            MOD_PREFIX + "PasteSettings",
            MOD_PREFIX + "ShowConfig"
        };

        // The terminal controls the app's own surface would show, in terminal order.
        List<TerminalSettingControl> VisibleControls(HostedApp app)
        {
            var visible = new List<TerminalSettingControl>();
            var block = Host.Block as IMyTerminalBlock;
            var manager = TerminalManager.Current;
            if (block == null || manager == null || string.IsNullOrEmpty(app.Info.ScriptId))
                return visible;
            ConfigManager.RunNested(block, app.Config, app.Info.ScriptId, () =>
            {
                foreach (var wrapper in manager.Controls)
                {
                    var control = wrapper == null ? null : wrapper.TerminalControl;
                    if (control == null || DisplayControls.Contains(control.Id) || !TerminalSettingRow.IsSupported(wrapper))
                        continue;
                    try
                    {
                        if (wrapper.IsVisibleForHostedApp(block, app.Info.ScriptId))
                            visible.Add(new TerminalSettingControl(wrapper));
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, wrapper);
                    }
                }
            });
            return visible;
        }

        List<TerminalSettingRow> BuildSettingRows(HostedApp app)
        {
            var rows = new List<TerminalSettingRow>();
            var block = Host.Block as IMyTerminalBlock;
            foreach (var visible in VisibleControls(app))
            {
                try
                {
                    rows.Add(new TerminalSettingRow(visible.Wrapper, block, app.Config, app.Info.ScriptId, () => OnSettingChanged(app)));
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, visible.Wrapper);
                }
            }
            return rows;
        }

        // How often a dragged settings slider applies its value while the pointer is held.
        const long SLIDER_APPLY_FRAMES = 15;

        void FlushSettingValues()
        {
            if (_settingsModel == null || _settingsWindow == null || !_settingsWindow.IsOpen)
                return;
            long frame = MyAPIGateway.Session.GameplayFrameCounter;
            var rows = _settingsModel.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.HasPendingValue && (!_primaryDown || frame - row.PendingSince >= SLIDER_APPLY_FRAMES))
                    row.FlushValue();
            }
        }

        void OnSettingChanged(HostedApp app)
        {
            MarkConfigDirty();
            SafeLayoutChanged(app);
            if (_settingsModel != null && ReferenceEquals(_settingsTarget, app))
                RefreshSettings(app);
            UpdateSettingsTarget();
            Host.RenderSprites();
        }

        // A change can show or hide other controls (as in the terminal); same controls keep their rows.
        void RefreshSettings(HostedApp app)
        {
            var visible = VisibleControls(app);
            var rows = _settingsModel.Rows;
            bool same = rows.Count == visible.Count;
            for (int i = 0; same && i < rows.Count; i++)
                same = ReferenceEquals(rows[i].Wrapper, visible[i].Wrapper);
            if (!same)
            {
                _settingsModel.Rows = BuildSettingRows(app);
                return;
            }
            foreach (var row in rows)
                row.Refresh();
        }

        void OpenSettingsForActive()
        {
            OpenSettings(SettingsTarget);
        }

        void OpenSettings(HostedApp app)
        {
            if (app == null || _root == null)
                return;
            if (_settingsWindow != null)
            {
                if (_settingsWindow.IsOpen)
                    _settingsWindow.Close();
                _settingsWindow.Dispose();
            }
            _settingsTarget = app;
            var model = new AppSettingsViewModel(
                string.Format(LocHelper.GetLoc(MOD_PREFIX + "MultiFunction_Settings"), app.Title),
                LocHelper.GetLoc(MOD_PREFIX + "MultiFunction_NoSettings"),
                LocHelper.GetLoc(MOD_PREFIX + "MultiFunction_CloseApp"),
                () => CloseApp(app));
            model.Rows = BuildSettingRows(app);
            _settingsModel = model;
            _settingsWindow = new AppSettingsView { DataContext = model, Topmost = true };
            var area = _workArea;
            Place(_settingsWindow, new RectangleF(area.X + area.Width * .1f, area.Y + area.Height * .1f, area.Width * .8f,
                area.Height * .8f));
            _settingsWindow.Show(_root);
        }


        /// <summary>The pointer on the surface, in surface pixels.</summary>
        internal void PointerMoved(Vector2 surfacePosition)
        {
            _pointer = (surfacePosition - Host.ViewBox.Position) / RootScale;
            if (_dragged != null)
                UpdateSnap();
        }

        // Map and 3D apps zoom with the wheel through their surface; here the app under the pointer gets it.
        public override void OnMouseScroll(int delta, ref bool handled)
        {
            base.OnMouseScroll(delta, ref handled);
            if (handled)
                return;
            HostedApp app = AppAtPointer();
            if (app == null)
                return;
            try
            {
                app.App.OnMouseScroll(delta, ref handled);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, app.App);
            }
        }

        HostedApp AppAtPointer()
        {
            if (_mode == MultiFunctionMode.Pages)
                return _current;
            if (_root == null || float.IsNaN(_pointer.X))
                return null;
            var windows = _root.OwnedWindows;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var window = windows[i];
                if (window == null || window.WindowState == WindowState.Minimized)
                    continue;
                RectangleF frame = window.FrameBounds;
                frame.Position -= _root.Bounds.Position;
                if (!frame.Contains(_pointer))
                    continue;
                for (int j = 0; j < _apps.Count; j++)
                    if (ReferenceEquals(_apps[j].Window, window))
                        return _apps[j];
                return null;
            }
            return null;
        }

        internal void PrimaryButton(bool pressed)
        {
            _primaryDown = pressed;
            if (pressed)
                return;
            for (int i = 0; i < _apps.Count; i++)
                if (_apps[i].PendingViewBox.HasValue)
                    UpdateViewBox(_apps[i]);
            for (int i = 0; i < _apps.Count; i++)
                SyncWindowState(_apps[i]);
            HostedApp resized = _resized;
            _resized = null;
            if (resized != null && resized.Window != null)
            {
                RectangleF rect = Clamp(new RectangleF(resized.Window.Position, WindowSize(resized.Window)));
                Place(resized.Window, rect);
                StorePlacement(resized, rect);
            }
            if (_dragged == null)
                return;
            HostedApp dragged = _dragged;
            _dragged = null;
            SnapZone zone = _snap;
            CloseSnapPreview();
            if (dragged.Window == null)
                return;
            RectangleF target = zone != SnapZone.None && zone != SnapZone.Full
                ? ZoneRect(zone)
                : Clamp(new RectangleF(dragged.Window.Position, WindowSize(dragged.Window)));
            Place(dragged.Window, target);
            if (zone == SnapZone.Full)
                dragged.Window.WindowState = WindowState.Maximized;
            StorePlacement(dragged, target);
            SyncWindowState(dragged);
        }

        void OnWindowResized(HostedApp app)
        {
            if (_mode != MultiFunctionMode.Windows || app.Window == null)
                return;
            if (_primaryDown)
                _resized = app;
            else
                OnWindowMoved(app);
        }

        void OnWindowMoved(HostedApp app)
        {
            if (_mode != MultiFunctionMode.Windows || app.Window == null)
                return;
            if (_primaryDown && ReferenceEquals(_resized, app))
                return;
            // Raised while the owner lays out its windows: the snap preview opens from the next pointer move instead.
            if (_primaryDown)
            {
                _dragged = app;
                return;
            }
            RectangleF frame = new RectangleF(app.Window.Position, WindowSize(app.Window));
            RectangleF clamped = Clamp(frame);
            if (clamped != frame)
                Place(app.Window, clamped);
        }

        void UpdateSnap()
        {
            SnapZone zone = ZoneAt(_pointer);
            if (zone == _snap)
                return;
            _snap = zone;
            if (zone == SnapZone.None)
            {
                CloseSnapPreview();
                return;
            }
            if (_snapPreview == null)
                _snapPreview = new SnapPreviewView { DataContext = _model, ShowActivated = false };
            Place(_snapPreview, ZoneRect(zone));
            if (!_snapPreview.IsOpen)
                _snapPreview.Show(_root);
        }

        void CloseSnapPreview()
        {
            _snap = SnapZone.None;
            if (_snapPreview != null && _snapPreview.IsOpen)
                _snapPreview.Close();
        }

        SnapZone ZoneAt(Vector2 pointer)
        {
            if (float.IsNaN(pointer.X) || _workArea.Width <= 0f)
                return SnapZone.None;
            float edge = Math.Max(8f, Math.Min(_workArea.Width, _workArea.Height) * .05f);
            bool left = pointer.X <= _workArea.X + edge;
            bool right = pointer.X >= _workArea.Right - edge;
            bool top = pointer.Y <= _workArea.Y + edge;
            bool bottom = pointer.Y >= _workArea.Bottom - edge;
            if (top && left) return SnapZone.TopLeft;
            if (top && right) return SnapZone.TopRight;
            if (bottom && left) return SnapZone.BottomLeft;
            if (bottom && right) return SnapZone.BottomRight;
            if (left) return SnapZone.Left;
            if (right) return SnapZone.Right;
            if (top) return SnapZone.Full;
            return SnapZone.None;
        }

        RectangleF ZoneRect(SnapZone zone)
        {
            RectangleF area = _workArea;
            float halfWidth = area.Width * .5f;
            float halfHeight = area.Height * .5f;
            switch (zone)
            {
                case SnapZone.Left: return new RectangleF(area.X, area.Y, halfWidth, area.Height);
                case SnapZone.Right: return new RectangleF(area.X + halfWidth, area.Y, halfWidth, area.Height);
                case SnapZone.TopLeft: return new RectangleF(area.X, area.Y, halfWidth, halfHeight);
                case SnapZone.TopRight: return new RectangleF(area.X + halfWidth, area.Y, halfWidth, halfHeight);
                case SnapZone.BottomLeft: return new RectangleF(area.X, area.Y + halfHeight, halfWidth, halfHeight);
                case SnapZone.BottomRight: return new RectangleF(area.X + halfWidth, area.Y + halfHeight, halfWidth, halfHeight);
                default: return area;
            }
        }
    }
}
