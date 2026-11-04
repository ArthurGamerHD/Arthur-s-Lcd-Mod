using System;
using System.Collections.Generic;
using Acrux.Core.Input;
using Acrux.Render3D;
using Acrux.Rendering;
using Acrux.Styling;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.Maps;
using LcdMod.Client.Apps.Views;
using LcdMod.Client.Helpers;
using Adk.Mvvm;
using Sandbox.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps.IntegrityMonitor
{
    [PreviewWith("Status.Text", "")]
    [PreviewWith("Status.IsVisible", false)]
    [PreviewWith("Status.HasIcon", false)]
    [PreviewWith("Status.Color", "#FFFFFF")]
    internal sealed class IntegrityMonitorViewModel : ObservableObject, IMapSurface, IAsyncMapSurface, IDisposable
    {
        readonly IAppHost _host;
        readonly List<IntegrityLegendEntry> _legend = new List<IntegrityLegendEntry>();
        readonly List<XRayLayerItem> _xRayLayers = new List<XRayLayerItem>();
        readonly List<XRaySwatch> _xRaySwatches = new List<XRaySwatch>();
        readonly List<AxisItem> _axes = new List<AxisItem>();
        GridSceneRenderer _renderer;
        Vector2I _canvasSize;
        Color?[,] _cells;
        int _version;
        int _fitSide = 1;
        float _rotation;
        float _scale = 1f;
        int _sceneVersion;
        bool _hasGrid;
        bool _is3D;
        bool _xRayOpen;
        bool _cameraLocked = true;
        bool _axesOpen;
        bool _infoOpen;
        // A locked camera's scale, fitted to the grid's side (as the 2D map frames it) once the 3D view knows the
        // grid; NaN when nothing waits.
        float _fitScale = float.NaN;
        float _lockedScale = 1f;
        // A refused or 3D-loading frame keeps asking for ticks until one is accepted and complete.
        bool _framePending;
        Color _legendBackground;
        string _sideWarning = string.Empty;
        string _alert = string.Empty;
        string _lockLabel = string.Empty;
        string _targetClass = string.Empty;
        string _targetCode = string.Empty;
        bool _showSnapshotButton;
        bool _canSnapshot = true;
        bool _fixedCamera;

        public IntegrityMonitorViewModel(IAppHost host)
        {
            _host = host;
            // Outlines over everything: what a projection still needs and what is damaged show through the hull,
            // and a damaged block keeps its own paint under its outline.
            State.MissingStyle = GridHighlightStyle.SeeThroughWireframe;
            State.DamagedStyle = GridHighlightStyle.SeeThroughWireframe;
            // X-ray keeps the damage: in full on the highlighted categories, muted on the rest of the hull, as the
            // radar mutes the contacts it is not locked on.
            State.XRayShowsDamage = true;
            State.XRayHullDamageSaturation = .5f;
            State.XRayHullDamageValue = .5f;
            ProjectionCommand = new DelegateCommand(_ => Set3D(!_is3D, true));
            XRayToggleCommand = new DelegateCommand(_ => SetXRayOpen(!_xRayOpen));
            // Locked, the button opens the axes to lock to; free, it locks back to the configured one.
            CameraLockCommand = new DelegateCommand(_ =>
            {
                if (_cameraLocked)
                {
                    SetAxesOpen(!_axesOpen);
                    // The toggle flips itself on click; the camera stays locked.
                    RaisePropertyChanged<bool>(nameof(IsCameraLocked));
                    return;
                }
                SetCameraLocked(true);
                CameraChanged?.Invoke();
                CameraRelocked?.Invoke();
                _host.RequestFrames();
            });
            for (int i = 0; i < AxisItem.Labels.Length; i++)
                _axes.Add(new AxisItem(this, i));
            InfoCommand = new DelegateCommand(_ =>
            {
                RaiseAndSetIfChanged(ref _infoOpen, !_infoOpen, nameof(IsInfoOpen));
                RaisePropertyChanged<bool>(nameof(ShowInfo));
                _host.RequestFrames();
                if (_infoOpen)
                    InfoOpened?.Invoke();
            });
            SnapshotCommand = new DelegateCommand(_ => SnapshotRequested?.Invoke());
            LockCommand = new DelegateCommand(_ => LockRequested?.Invoke());
            for (int i = 0; i < XRayLayerItem.Categories.Length; i++)
            {
                var item = new XRayLayerItem(this, XRayLayerItem.Categories[i]);
                State.SetXRayColor(item.Category, item.Color);
                _xRayLayers.Add(item);
            }
            RefreshXRayLayers();
        }

        // The user switched the view mode or the X-ray layers on the screen; the app saves it to the config.
        public event Action ViewChanged;
        // The user moved the camera, or locked it back to the configured view; the app saves or applies it.
        public event Action CameraChanged;
        // The user picked an axis (a View value) to lock the camera to; the app makes it the configured axis.
        public event Action<int> AxisSelected;
        // The user asked for a snapshot of the grid on the screen.
        public event Action SnapshotRequested;
        // The lock button: lock on the grid it names.
        public event Action LockRequested;
        // The scene published a new scan (GetMapCells and the counts changed).
        public event Action ScanChanged;
        // The user's own actions, for the FTUE tips: the 3D view or the flat map opened, an X-ray layer picked, the
        // camera locked back or to another side, the legend opened.
        public event Action Opened3D;
        public event Action Opened2D;
        public event Action XRayPicked;
        public event Action CameraRelocked;
        public event Action InfoOpened;

        public StatusMessage Status { get; } = new StatusMessage();
        internal GridSceneState State { get; } = new GridSceneState();
        public IMapSurface Surface => this;

        public Color?[,] Cells => _cells;
        [PreviewWith(1)]
        public int Version => _version;
        [PreviewWith(24)]
        public int FitSide => _fitSide;
        public float MapRotation => _rotation;
        [PreviewWith(1f)]
        public float MapScale => _scale;
        [PreviewWith(true)]
        public bool HasMap => _cells != null || (_is3D && _hasGrid);
        [PreviewWith(false)]
        public bool Show2D => !_is3D && _cells != null;

        [PreviewWith(1)]
        public int SceneVersion => _sceneVersion;
        [PreviewWith(true)]
        public bool Show3D => _is3D && _hasGrid;
        [PreviewWith(true)]
        public bool Is3D => _is3D;
        public ICommand ProjectionCommand { get; }
        [PreviewWith("Map3D Uniform")]
        public BackgroundBrush ProjectionIcon => _is3D ? Map3DIcon : Map2DIcon;
        [PreviewWith("3D view\nThe ship in 3D. Drag to orbit, scroll to zoom.")]
        public string ProjectionTip => Tooltip(_is3D ? "View3D" : "View2D");
        [PreviewWith(7)]
        public IReadOnlyList<XRayLayerItem> XRayLayers => _xRayLayers;
        // The head tile shows the first selected layer's icon, one color strip per selected layer and their count.
        XRayLayerItem FirstSelectedLayer
        {
            get
            {
                for (int i = 1; i < _xRayLayers.Count; i++)
                    if (_xRayLayers[i].IsSelected)
                        return _xRayLayers[i];
                return _xRayLayers[0];
            }
        }
        // Several layers at once show the game's block-group icon.
        [PreviewWith("MyObjectBuilder_CubeBlock/LargeBlockArmorBlock Uniform")]
        public BackgroundBrush XRayIcon => _xRaySwatches.Count > 1 ? GroupIcon : FirstSelectedLayer.Icon;
        [PreviewWith(1)]
        public IReadOnlyList<XRaySwatch> XRaySwatches => _xRaySwatches;
        [PreviewWith("")]
        public string XRayCount => _xRaySwatches.Count > 1 ? _xRaySwatches.Count.ToString() : string.Empty;
        [PreviewWith(false)]
        public bool HasXRayCount => _xRaySwatches.Count > 1;
        [PreviewWith("Full view\nThe ship as it is, with damaged and missing blocks highlighted.")]
        public string XRayTip
        {
            get
            {
                if (_xRaySwatches.Count <= 1)
                    return FirstSelectedLayer.Tip;
                var names = new List<string>();
                for (int i = 1; i < _xRayLayers.Count; i++)
                    if (_xRayLayers[i].IsSelected)
                        names.Add(_xRayLayers[i].Name);
                return string.Join(", ", names) + "\n" + LocHelper.GetLoc(MOD_PREFIX + "Integrity_XRay_Tooltip");
            }
        }
        [PreviewWith(true)]
        public bool IsCameraLocked => _cameraLocked;
        [PreviewWith(6)]
        public IReadOnlyList<AxisItem> Axes => _axes;
        [PreviewWith(false)]
        public bool AreAxesOpen => _axesOpen;
        public ICommand CameraLockCommand { get; }
        [PreviewWith("Lock Uniform")]
        public BackgroundBrush CameraIcon => _cameraLocked ? LockIcon : FreeIcon;
        [PreviewWith("Locked camera\nThe camera follows the configured axis, rotation and scale.")]
        public string CameraTip => Tooltip(_cameraLocked ? "CameraLocked" : "CameraFree");
        [PreviewWith(false)]
        public bool IsXRayOpen => _xRayOpen;
        public ICommand XRayToggleCommand { get; }

        [PreviewWith(4)]
        public IReadOnlyList<IntegrityLegendEntry> Legend => _legend;
        [PreviewWith(false)]
        public bool HasLegend => _legend.Count > 0;
        [PreviewWith(true)]
        public bool IsInfoOpen => _infoOpen;
        [PreviewWith(true)]
        public bool ShowInfo => _infoOpen && _legend.Count > 0;
        public ICommand InfoCommand { get; }
        [PreviewWith("Legend")]
        public string InfoTip => LocHelper.GetLoc(MOD_PREFIX + "Integrity_Legend");
        [PreviewWith("#80004679")]
        public Color LegendBackground => _legendBackground;

        public int MissingCount => _renderer == null ? 0 : _renderer.MissingCount;
        public int DamagedCount => _renderer == null ? 0 : _renderer.DamagedCount;
        internal bool ReferenceMismatch => _renderer != null && _renderer.ReferenceMismatch;
        internal bool IsShowingMemory => _renderer != null && _renderer.IsShowingMemory;
        internal int[] MapCells => _renderer == null ? null : _renderer.GetMapCells();
        internal long[] LastHits => _renderer == null ? null : _renderer.GetLastHits();

        [PreviewWith("No projector selected")]
        public string SideWarning => _sideWarning;
        public bool HasSideWarning => !string.IsNullOrEmpty(_sideWarning);
        public ICommand SnapshotCommand { get; }
        [PreviewWith("Target loss!")]
        public string Alert => _alert;
        [PreviewWith(true)]
        public bool ShowAlert => !string.IsNullOrEmpty(_alert);
        // The app points the camera; the user can zoom and pan, not orbit, and there is no lock to change.
        public bool CanOrbit => !_fixedCamera;

        public void SetFixedCamera(bool value)
        {
            if (RaiseAndSetIfChanged(ref _fixedCamera, value, nameof(CanOrbit)))
                RaisePropertyChanged<bool>(nameof(CanOrbit));
        }

        public bool ShowSnapshotButton => _showSnapshotButton;
        public bool CanSnapshot => _canSnapshot;
        [PreviewWith("Snapshot current grid")]
        public string SnapshotLabel => LocHelper.GetLoc(MOD_PREFIX + "Integrity_Snapshot_Title");
        public string SnapshotTip => LocHelper.GetLoc(MOD_PREFIX + "Integrity_Snapshot_Tooltip");

        public void SetSideWarning(string text)
        {
            text = text ?? string.Empty;
            if (string.Equals(text, _sideWarning, StringComparison.Ordinal))
                return;
            bool had = HasSideWarning;
            _sideWarning = text;
            RaisePropertyChanged<string>(nameof(SideWarning));
            if (had != HasSideWarning)
                RaisePropertyChanged<bool>(nameof(HasSideWarning));
            _host.RequestFrames();
        }

        [PreviewWith("Cancer-Class")]
        public string TargetClass => _targetClass;
        [PreviewWith("400 m [CMX-047]")]
        public string TargetCode => _targetCode;
        [PreviewWith(true)]
        public bool HasTargetInfo => !string.IsNullOrEmpty(_targetClass);

        // What a scan read of the target: its class, and its code with how far it is; null hides them.
        public void SetTargetInfo(string className, string code)
        {
            className = className ?? string.Empty;
            code = code ?? string.Empty;
            if (string.Equals(className, _targetClass, StringComparison.Ordinal) &&
                string.Equals(code, _targetCode, StringComparison.Ordinal))
                return;
            bool had = HasTargetInfo;
            _targetClass = className;
            _targetCode = code;
            RaisePropertyChanged<string>(nameof(TargetClass));
            RaisePropertyChanged<string>(nameof(TargetCode));
            if (had != HasTargetInfo)
                RaisePropertyChanged<bool>(nameof(HasTargetInfo));
            _host.RequestFrames();
        }

        public ICommand LockCommand { get; }
        [PreviewWith("Lock: Red Ship")]
        public string LockLabel => _lockLabel;
        [PreviewWith(true)]
        public bool ShowLockButton => !string.IsNullOrEmpty(_lockLabel);

        // A button to lock on a grid, labelled with it; null hides it.
        public void SetLockButton(string label)
        {
            label = label ?? string.Empty;
            if (string.Equals(label, _lockLabel, StringComparison.Ordinal))
                return;
            bool had = ShowLockButton;
            _lockLabel = label;
            RaisePropertyChanged<string>(nameof(LockLabel));
            if (had != ShowLockButton)
                RaisePropertyChanged<bool>(nameof(ShowLockButton));
            _host.RequestFrames();
        }

        public void SetAlert(string text)
        {
            text = text ?? string.Empty;
            if (string.Equals(text, _alert, StringComparison.Ordinal))
                return;
            bool had = ShowAlert;
            _alert = text;
            RaisePropertyChanged<string>(nameof(Alert));
            if (had != ShowAlert)
                RaisePropertyChanged<bool>(nameof(ShowAlert));
            _host.RequestFrames();
        }

        // canSnapshot false greys the button out while a snapshot is being taken.
        public void SetSnapshotButton(bool shown, bool canSnapshot)
        {
            bool changed = RaiseAndSetIfChanged(ref _showSnapshotButton, shown, nameof(ShowSnapshotButton));
            changed |= RaiseAndSetIfChanged(ref _canSnapshot, canSnapshot, nameof(CanSnapshot));
            if (changed)
                _host.RequestFrames();
        }

        static readonly BackgroundBrush Map2DIcon = new ImageBrush("Map2D") { Stretch = Stretch.Uniform };
        static readonly BackgroundBrush Map3DIcon = new ImageBrush("Map3D") { Stretch = Stretch.Uniform };
        BackgroundBrush _groupIcon;
        BackgroundBrush GroupIcon => _groupIcon ??
            (_groupIcon = new ImageBrush(Acrux.Sprites.BlockSprites.RegisterGroup()) { Stretch = Stretch.Uniform });
        static readonly BackgroundBrush LockIcon = new ImageBrush("Lock") { Stretch = Stretch.Uniform };
        static readonly BackgroundBrush FreeIcon = new ImageBrush("RotationPlane") { Stretch = Stretch.Uniform };

        // The camera was pointed at a configured side: frame the grid from it at this scale.
        public void RequestFit(float scale)
        {
            _lockedScale = scale;
            _fitScale = scale;
        }

        public void SetCameraLocked(bool locked)
        {
            if (!locked)
                SetAxesOpen(false);
            if (!RaiseAndSetIfChanged(ref _cameraLocked, locked, nameof(IsCameraLocked)))
                return;
            RaisePropertyChanged<BackgroundBrush>(nameof(CameraIcon));
            RaisePropertyChanged<string>(nameof(CameraTip));
        }

        void SetAxesOpen(bool open)
        {
            if (RaiseAndSetIfChanged(ref _axesOpen, open, nameof(AreAxesOpen)))
                _host.RequestFrames();
        }

        // The configured axis, marked in the flyout.
        public void SetAxis(int view)
        {
            for (int i = 0; i < _axes.Count; i++)
                _axes[i].Update(i == view);
        }

        internal void SelectAxis(int view)
        {
            SetAxesOpen(false);
            SetAxis(view);
            AxisSelected?.Invoke(view);
            CameraRelocked?.Invoke();
        }

        // The user moved the camera: it no longer follows the configured view.
        void CameraMoved()
        {
            SetCameraLocked(false);
            InvalidateScene();
            _host.RequestFrames();
            CameraChanged?.Invoke();
        }

        static string Tooltip(string name)
        {
            string key = MOD_PREFIX + "Integrity_" + name;
            return LocHelper.GetLoc(key + "_Title") + "\n" + LocHelper.GetLoc(key + "_Tooltip");
        }

        public void Set3D(bool value, bool fromUser = false)
        {
            if (_is3D == value)
                return;
            bool hadMap = HasMap;
            bool hadLegend = HasLegend;
            _is3D = value;
            if (!value)
                SetAxesOpen(false);
            RaisePropertyChanged<bool>(nameof(Is3D));
            RaisePropertyChanged<bool>(nameof(Show2D));
            RaisePropertyChanged<bool>(nameof(Show3D));
            RaisePropertyChanged<BackgroundBrush>(nameof(ProjectionIcon));
            RaisePropertyChanged<string>(nameof(ProjectionTip));
            if (hadMap != HasMap)
                RaisePropertyChanged<bool>(nameof(HasMap));
            if (hadLegend != HasLegend)
                RaisePropertyChanged<bool>(nameof(HasLegend));
            InvalidateScene();
            if (!fromUser)
                return;
            _host.RequestFrames();
            ViewChanged?.Invoke();
            if (value)
                Opened3D?.Invoke();
            else
                Opened2D?.Invoke();
        }

        // Categories as GridSceneState.XRayCategories bits; zero is the plain view.
        public void SetXRay(int categories, bool fromUser = false)
        {
            categories &= XRayLayerItem.AllBits;
            if (State.XRayCategories == categories)
                return;
            State.XRayCategories = categories;
            RefreshXRayLayers();
            InvalidateScene();
            if (!fromUser)
                return;
            _host.RequestFrames();
            ViewChanged?.Invoke();
            if (categories != 0)
                XRayPicked?.Invoke();
        }

        // A click picks one layer and closes the row; with Ctrl it adds or removes the layer and keeps the row open.
        internal void SelectXRay(GridBlockCategory category)
        {
            var input = MyAPIGateway.Input;
            bool combine = input != null && input.IsAnyCtrlKeyPressed();
            int bit = category == GridBlockCategory.None ? 0 : GridSceneState.CategoryBit(category);
            if (combine && bit != 0)
            {
                SetXRay(State.XRayCategories ^ bit, true);
                return;
            }
            SetXRayOpen(false);
            SetXRay(bit, true);
        }

        void SetXRayOpen(bool open)
        {
            if (!RaiseAndSetIfChanged(ref _xRayOpen, open, nameof(IsXRayOpen)))
                return;
            RefreshXRayLayers();
            _host.RequestFrames();
        }

        void RefreshXRayLayers()
        {
            int categories = State.XRayCategories;
            _xRaySwatches.Clear();
            for (int i = 0; i < _xRayLayers.Count; i++)
            {
                XRayLayerItem item = _xRayLayers[i];
                bool selected = item.Category == GridBlockCategory.None
                    ? categories == 0
                    : (categories & GridSceneState.CategoryBit(item.Category)) != 0;
                item.Update(selected, _xRayOpen);
                if (selected && item.Category != GridBlockCategory.None)
                    _xRaySwatches.Add(new XRaySwatch(item.Color));
            }
            if (_xRaySwatches.Count == 0)
                _xRaySwatches.Add(new XRaySwatch(_xRayLayers[0].Color));
            float width = XRaySwatch.TILE_WIDTH / _xRaySwatches.Count;
            for (int i = 0; i < _xRaySwatches.Count; i++)
                _xRaySwatches[i].Width = width;
            RaisePropertyChanged<IReadOnlyList<XRaySwatch>>(nameof(XRaySwatches));
            RaisePropertyChanged<BackgroundBrush>(nameof(XRayIcon));
            RaisePropertyChanged<string>(nameof(XRayCount));
            RaisePropertyChanged<bool>(nameof(HasXRayCount));
            RaisePropertyChanged<string>(nameof(XRayTip));
        }

        // The icons follow the blocks this ship has most of in each category, from the scene's scan of the grid.
        void RefreshXRayIcons()
        {
            for (int i = 0; i < _xRayLayers.Count; i++)
            {
                XRayLayerItem item = _xRayLayers[i];
                if (item.Category != GridBlockCategory.None)
                    item.SetBlock(_renderer.GetCommonBlock(item.Category));
            }
            RaisePropertyChanged<BackgroundBrush>(nameof(XRayIcon));
        }

        public void SetMap(Color?[,] cells, int fitSide, float rotation, float scale)
        {
            bool hadMap = HasMap;
            bool had2D = Show2D;
            _cells = cells;
            _version++;
            RaisePropertyChanged<Color?[,]>(nameof(Cells));
            RaisePropertyChanged<int>(nameof(Version));
            RaiseAndSetIfChanged(ref _fitSide, fitSide, nameof(FitSide));
            RaiseAndSetIfChanged(ref _rotation, rotation, nameof(MapRotation));
            RaiseAndSetIfChanged(ref _scale, scale, nameof(MapScale));
            if (hadMap != HasMap)
                RaisePropertyChanged<bool>(nameof(HasMap));
            if (had2D != Show2D)
                RaisePropertyChanged<bool>(nameof(Show2D));
        }

        // The grid to show (or, while it is not found, memory: the grid as last seen) and what it should be: a live
        // grid (a projection) or, while that is not found, a reference snapshot.
        public void ShowGrids(long gridId, long referenceGridId, GridSnapshot memory = null, GridSnapshot reference = null)
        {
            bool gridChanged = State.GridId != gridId || !SameSnapshot(State.MemorySnapshot, memory);
            if (gridChanged || State.ReferenceGridId != referenceGridId || !SameSnapshot(State.ReferenceSnapshot, reference))
            {
                if (_cameraLocked && gridChanged)
                    _fitScale = _lockedScale;
                State.GridId = gridId;
                State.ReferenceGridId = referenceGridId;
                State.MemorySnapshot = memory;
                State.ReferenceSnapshot = reference;
                InvalidateScene();
            }
            SetHasGrid(gridId != 0 || memory != null);
        }

        static bool SameSnapshot(GridSnapshot a, GridSnapshot b)
        {
            return a == null ? b == null : b != null && Acrux.Toolkit.Ui.Native(a) == Acrux.Toolkit.Ui.Native(b);
        }

        public void ClearMap()
        {
            State.GridId = 0;
            State.ReferenceGridId = 0;
            State.MemorySnapshot = null;
            State.ReferenceSnapshot = null;
            SetHasGrid(false);
            if (_cells == null)
                return;
            bool had2D = Show2D;
            _cells = null;
            RaisePropertyChanged<Color?[,]>(nameof(Cells));
            if (had2D)
                RaisePropertyChanged<bool>(nameof(Show2D));
            RaisePropertyChanged<bool>(nameof(HasMap));
        }

        void SetHasGrid(bool value)
        {
            if (_hasGrid == value)
                return;
            bool hadMap = HasMap;
            _hasGrid = value;
            RaisePropertyChanged<bool>(nameof(Show3D));
            if (hadMap != HasMap)
                RaisePropertyChanged<bool>(nameof(HasMap));
            InvalidateScene();
        }

        public void Tick()
        {
            if (_renderer == null)
                _renderer = new GridSceneRenderer();
            // The scan feeds the 2D map too, so it runs in both views.
            if (_renderer.Update(State))
            {
                InvalidateScene();
                RefreshXRayIcons();
                ScanChanged?.Invoke();
            }
            if (!_is3D)
                return;
            // Before this tick's draw requests the next frame; publishing from the draw would ask for another render.
            _renderer.CompleteFinishedRender();
            if (_renderer.UpdateMissiles(State))
                InvalidateScene();
            if (!float.IsNaN(_fitScale) && _renderer.HasGrid)
            {
                State.Zoom = MathHelper.Clamp(_fitScale * _renderer.GetFitZoom(State), GridSceneRenderer.MINIMUM_ZOOM,
                    GridSceneRenderer.MAXIMUM_ZOOM);
                _fitScale = float.NaN;
                InvalidateScene();
            }
            if (_hasGrid && (_framePending || _renderer.IsSceneLoading))
            {
                if (_renderer.IsSceneLoading)
                    InvalidateScene();
                _host.RequestFrames();
            }
        }

        public void InvalidateScene()
        {
            _framePending = true;
            RaiseAndSetIfChanged(ref _sceneVersion, _sceneVersion + 1, nameof(SceneVersion));
        }

        public void SetLegend(List<IntegrityLegendEntry> entries, Color background)
        {
            bool had = HasLegend;
            _legend.Clear();
            _legend.AddRange(entries);
            RaisePropertyChanged<IReadOnlyList<IntegrityLegendEntry>>(nameof(Legend));
            if (had != HasLegend)
                RaisePropertyChanged<bool>(nameof(HasLegend));
            RaisePropertyChanged<bool>(nameof(ShowInfo));
            RaiseAndSetIfChanged(ref _legendBackground, background, nameof(LegendBackground));
        }

        void IMapSurface.SetCanvasSize(Vector2I size)
        {
            _canvasSize = size;
            InvalidateScene();
        }

        void IMapSurface.Paint(PixelCanvas canvas)
        {
            if (_renderer == null)
            {
                canvas.Clear(Color.Transparent);
                return;
            }
            _renderer.Render(canvas, State);
        }

        bool IAsyncMapSurface.RequestTexture(PixelCanvas canvas, Action<Exception> completed)
        {
            if (_renderer == null)
                return false;
            bool accepted = _renderer.RequestRender(canvas, State, error =>
            {
                completed(error);
                _host.RequestFrames();
            });
            _framePending = !accepted;
            if (!accepted)
                _host.RequestFrames();
            return accepted;
        }

        void IMapSurface.Drag(Vector2 delta)
        {
            if (_renderer == null || _fixedCamera)
                return;
            _renderer.Orbit(State, delta);
            CameraMoved();
        }

        bool IMapSurface.Scroll(int delta)
        {
            if (_renderer == null || !_renderer.Scroll(State, delta))
                return false;
            CameraMoved();
            return true;
        }

        void IMapSurface.MiddleDrag(Vector2 delta)
        {
            if (_renderer == null || _canvasSize.X <= 0 || _canvasSize.Y <= 0)
                return;
            _renderer.Pan(State, delta, Math.Min(_canvasSize.X, _canvasSize.Y));
            CameraMoved();
        }

        void IMapSurface.MiddleClick(Vector2 pixel)
        {
        }

        void IMapSurface.Click(Vector2 pixel)
        {
        }

        void IMapSurface.DoubleClick(Vector2 pixel)
        {
        }

        void IMapSurface.MarkerClicked(MapMarker marker)
        {
        }

        void IMapSurface.StreetView(Vector2 pixel)
        {
        }

        public void Dispose()
        {
            if (_renderer != null)
                _renderer.Dispose();
            _renderer = null;
        }
    }

    // One X-ray layer of the 3D view: a block category drawn through the hull, or None for the plain view.
    internal sealed class XRayLayerItem : ObservableObject
    {
        internal static readonly GridBlockCategory[] Categories =
        {
            GridBlockCategory.None,
            GridBlockCategory.Thrusters,
            GridBlockCategory.Power,
            GridBlockCategory.Gyroscopes,
            GridBlockCategory.Weapons,
            GridBlockCategory.Gravity,
            GridBlockCategory.Production,
            GridBlockCategory.Cargo,
            GridBlockCategory.Conveyors
        };

        internal static readonly int AllBits = BitsOf(Categories);

        bool _isSelected;
        bool _isShown;
        string _block;
        BackgroundBrush _icon;

        public XRayLayerItem(IntegrityMonitorViewModel owner, GridBlockCategory category)
        {
            Category = category;
            Command = new DelegateCommand(_ => owner.SelectXRay(category));
            SetBlock(null);
            Color = ColorOf(category);
            Name = category == GridBlockCategory.None
                ? LocHelper.GetLoc(MOD_PREFIX + "Integrity_XRayOff_Title")
                : LocHelper.GetLoc(NameKeyOf(category));
            Tip = category == GridBlockCategory.None
                ? Name + "\n" + LocHelper.GetLoc(MOD_PREFIX + "Integrity_XRayOff_Tooltip")
                : Name + "\n" + LocHelper.GetLoc(MOD_PREFIX + "Integrity_XRay_Tooltip") + "\n" +
                  LocHelper.GetLoc(MOD_PREFIX + "Integrity_XRay_Combine_Tooltip");
        }

        static int BitsOf(GridBlockCategory[] categories)
        {
            int bits = 0;
            for (int i = 0; i < categories.Length; i++)
                if (categories[i] != GridBlockCategory.None)
                    bits |= GridSceneState.CategoryBit(categories[i]);
            return bits;
        }

        // A block definition id the ship has, or null for the category's stock block.
        internal void SetBlock(string blockDefinitionId)
        {
            string block = blockDefinitionId ?? BlockOf(Category);
            if (_icon != null && string.Equals(block, _block, StringComparison.Ordinal))
                return;
            _block = block;
            string sprite = Acrux.Sprites.BlockSprites.Register(block) ??
                            Acrux.Sprites.BlockSprites.Register(BlockOf(Category)) ?? "MissingIcon";
            _icon = new ImageBrush(sprite) { Stretch = Stretch.Uniform };
            RaisePropertyChanged<BackgroundBrush>(nameof(Icon));
        }

        [PreviewWith("None", "Thrusters", "Power", "Gyroscopes", "Weapons", "Gravity", "Production")]
        public GridBlockCategory Category { get; }
        [PreviewWith("MyObjectBuilder_Thrust/LargeBlockLargeThrust Uniform")]
        public BackgroundBrush Icon => _icon;
        [PreviewWith("#808080", "#0040FF", "#00FF00", "#FFFF00", "#FF4000", "#A028DC", "#00AA6E")]
        public Color Color { get; }
        [PreviewWith("Full view", "Thrusters", "Power", "Gyroscopes", "Weapons", "Gravity", "Production")]
        public string Name { get; }
        [PreviewWith("Full view\nThe ship as it is.", "Thrusters\nX-ray: these blocks are shown through the hull.")]
        public string Tip { get; }
        public ICommand Command { get; }
        [PreviewWith(true, false, false, false, false, false, false)]
        public bool IsSelected => _isSelected;
        [PreviewWith(false, true, true, true, true, true, true)]
        public bool IsShown => _isShown;

        internal void Update(bool selected, bool shown)
        {
            RaiseAndSetIfChanged(ref _isSelected, selected, nameof(IsSelected));
            RaiseAndSetIfChanged(ref _isShown, shown, nameof(IsShown));
        }

        // The same colors as the 2D map's legend, opaque.
        internal static Color ColorOf(GridBlockCategory category)
        {
            switch (category)
            {
                case GridBlockCategory.Thrusters:
                    return new Color(0, 64, 255);
                case GridBlockCategory.Power:
                    return new Color(0, 255, 0);
                case GridBlockCategory.Gyroscopes:
                    return new Color(255, 255, 0);
                case GridBlockCategory.Weapons:
                    return new Color(255, 64, 0);
                case GridBlockCategory.Gravity:
                    return new Color(160, 40, 220);
                case GridBlockCategory.Production:
                    return new Color(0, 170, 110);
                case GridBlockCategory.Cargo:
                    return new Color(220, 160, 40);
                case GridBlockCategory.Conveyors:
                    return new Color(230, 60, 180);
                default:
                    return Color.Gray;
            }
        }

        // A stock block of the category, for a ship without one. Block icons are registered as LCD sprites on use.
        static string BlockOf(GridBlockCategory category)
        {
            switch (category)
            {
                case GridBlockCategory.Thrusters:
                    return "MyObjectBuilder_Thrust/LargeBlockLargeThrust";
                case GridBlockCategory.Power:
                    return "MyObjectBuilder_Reactor/LargeBlockLargeGenerator";
                case GridBlockCategory.Gyroscopes:
                    return "MyObjectBuilder_Gyro/LargeBlockGyro";
                case GridBlockCategory.Weapons:
                    return "MyObjectBuilder_SmallMissileLauncher/LargeMissileLauncher";
                case GridBlockCategory.Gravity:
                    return "MyObjectBuilder_VirtualMass/VirtualMassLarge";
                case GridBlockCategory.Production:
                    return "MyObjectBuilder_Assembler/LargeAssembler";
                case GridBlockCategory.Cargo:
                    return "MyObjectBuilder_CargoContainer/LargeBlockLargeContainer";
                case GridBlockCategory.Conveyors:
                    return "MyObjectBuilder_Conveyor/LargeBlockConveyor";
                default:
                    return "MyObjectBuilder_CubeBlock/LargeBlockArmorBlock";
            }
        }

        // The game's own category names, the ones the 2D map's legend uses.
        internal static string NameKeyOf(GridBlockCategory category)
        {
            switch (category)
            {
                case GridBlockCategory.Thrusters:
                    return "ControlMenuItemLabel_Thrusts";
                case GridBlockCategory.Power:
                    return "RadialMenuGroupTitle_Power";
                case GridBlockCategory.Gyroscopes:
                    return "DisplayName_BlockGroup_Gyroscopes";
                case GridBlockCategory.Weapons:
                    return "DisplayName_BlueprintClass_Weapons";
                case GridBlockCategory.Gravity:
                    return "DisplayName_TSS_Gravity";
                case GridBlockCategory.Production:
                    return "RadialMenuGroupTitle_Production";
                case GridBlockCategory.Cargo:
                    return "DisplayName_BlockGroup_CargoContainers";
                case GridBlockCategory.Conveyors:
                    return "HudEnergyGroupConveyors";
                default:
                    return "DisplayName_Category_ArmorBlocks";
            }
        }
    }

    // One axis of the camera-lock flyout; its index is a View value (X+, X-, Y+, Y-, Z+, Z-).
    internal sealed class AxisItem : ObservableObject
    {
        internal static readonly string[] Labels = { "X+", "X-", "Y+", "Y-", "Z+", "Z-" };
        static readonly string[] NameKeys =
        {
            "Axis_X_Positive", "Axis_X_Negative", "Axis_Y_Positive", "Axis_Y_Negative", "Axis_Z_Positive",
            "Axis_Z_Negative"
        };

        bool _isSelected;

        public AxisItem(IntegrityMonitorViewModel owner, int view)
        {
            Label = Labels[view];
            Tip = LocHelper.GetLoc(MOD_PREFIX + NameKeys[view]);
            Command = new DelegateCommand(_ => owner.SelectAxis(view));
        }

        [PreviewWith("X+", "X-", "Y+", "Y-", "Z+", "Z-")]
        public string Label { get; }
        [PreviewWith("Positive X", "Negative X", "Positive Y", "Negative Y", "Positive Z", "Negative Z")]
        public string Tip { get; }
        public ICommand Command { get; }
        [PreviewWith(true, false, false, false, false, false)]
        public bool IsSelected => _isSelected;

        internal void Update(bool selected)
        {
            RaiseAndSetIfChanged(ref _isSelected, selected, nameof(IsSelected));
        }
    }

    // One color strip of the X-ray head tile: the tile's width shared by the selected layers.
    internal sealed class XRaySwatch : ObservableObject
    {
        internal const float TILE_WIDTH = 42f;

        float _width = TILE_WIDTH;

        public XRaySwatch(Color color)
        {
            Color = color;
        }

        [PreviewWith("#808080")]
        public Color Color { get; }
        [PreviewWith(42f)]
        public float Width
        {
            get { return _width; }
            set { RaiseAndSetIfChanged(ref _width, value, nameof(Width)); }
        }
    }

    internal sealed class IntegrityLegendEntry : ObservableObject
    {
        public IntegrityLegendEntry(Color color, string caption)
        {
            Color = color;
            Caption = caption ?? string.Empty;
        }

        [PreviewWith("#DC4646", "#E6AA3C", "#5AC878", "#78A0BE")]
        public Color Color { get; }
        [PreviewWith("Missing", "Damaged", "Functional", "Armor")]
        public string Caption { get; }
    }
}
