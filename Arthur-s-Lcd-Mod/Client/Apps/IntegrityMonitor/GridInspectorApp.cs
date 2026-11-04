using System;
using System.Collections.Generic;
using Acrux.Controls;
using Acrux.Render3D;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps.IntegrityMonitor
{
    // A screen showing one grid in 3D or as a flat map, compared with what it should be (Integrity Monitor, Target
    // Info): the view, its camera, X-ray and legend, configured through the app's DiagnosticConfigComponent. A
    // subclass picks the grid and its reference each update (UpdateSources).
    internal abstract class GridInspectorApp : App
    {
        public static readonly List<MyTerminalControlComboBoxItem> IntegrityAxes = new List<MyTerminalControlComboBoxItem>
        {
            new MyTerminalControlComboBoxItem { Key = 0, Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_X_Positive") },
            new MyTerminalControlComboBoxItem { Key = 1, Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_X_Negative") },
            new MyTerminalControlComboBoxItem { Key = 2, Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_Y_Positive") },
            new MyTerminalControlComboBoxItem { Key = 3, Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_Y_Negative") },
            new MyTerminalControlComboBoxItem { Key = 4, Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_Z_Positive") },
            new MyTerminalControlComboBoxItem { Key = 5, Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_Z_Negative") }
        };

        static readonly Dictionary<CellKind, string> LegendCategoryLocKeys = new Dictionary<CellKind, string>
        {
            { CellKind.None, "DisplayName_Category_ArmorBlocks" },
            { CellKind.Thruster, "ControlMenuItemLabel_Thrusts" },
            { CellKind.Power, "RadialMenuGroupTitle_Power" },
            { CellKind.Gyro, "DisplayName_BlockGroup_Gyroscopes" },
            { CellKind.Weapons, "DisplayName_BlueprintClass_Weapons" },
            { CellKind.Gravity, "DisplayName_TSS_Gravity" },
            { CellKind.Production, "RadialMenuGroupTitle_Production" },
            { CellKind.Cargo, "DisplayName_BlockGroup_CargoContainers" },
            { CellKind.Conveyor, "HudEnergyGroupConveyors" }
        };

        const string MISSING_BLOCK_LOCKEY = "MissingBlock";
        const string DAMAGED_BLOCK_LOCKEY = "UnfinishedBlock";

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly IntegrityMapBuilder _mapBuilder = new IntegrityMapBuilder();
        // The 2D map waits to be shown again: a new result, a new layout or a mode switch.
        bool _presentPending = true;
        float _presentedScale = float.NaN;
        // The configured 3D camera, applied again only when the configuration changes; a camera the user moved
        // stays until then, or until the lock button.
        int _knownView = -1;
        float _knownRotation = float.NaN;
        float _knownScale = float.NaN;
        bool _syncConfigNextRun;
        // What the 3D legend last showed; it is rebuilt only when this changes.
        string _legend3D;
        string _customTitle;

        protected IMyCubeBlock Block => Host.Block;
        protected IntegrityMonitorViewModel ViewModel { get; }
        protected DiagnosticConfigComponent Settings => Config.GetComponent<DiagnosticConfigComponent>(APP);
        protected abstract string DefaultTitle { get; }
        // The subclass points the 3D camera itself (State yaw, pitch, roll) instead of the configured axis; the user
        // cannot orbit it. The 2D map keeps the configured axis.
        protected virtual bool FixedCamera => false;
        // Opens in 3D until the user picks the flat map.
        protected virtual bool Starts3D => false;

        // The settings changed by the subclass are saved with the screen on the next run.
        protected void SaveSettings()
        {
            _syncConfigNextRun = true;
        }

        public override string Title => _customTitle ?? LocHelper.GetLoc(DefaultTitle);
        internal override float WindowRenderScaling => CardScale;

        protected GridInspectorApp(IAppHost host) : base(host)
        {
            ViewModel = new IntegrityMonitorViewModel(host);
            ViewModel.ViewChanged += OnViewChanged;
            ViewModel.CameraChanged += OnCameraChanged;
            ViewModel.AxisSelected += OnAxisSelected;
            SetContent(CreateView());
        }

        // Runs in the constructor: an override may use ViewModel only.
        protected virtual UserControl CreateView()
        {
            return new IntegrityMonitorView { DataContext = ViewModel };
        }

        // For the FTUE tips: what the screen shows and the user's actions on it.
        public bool HasMap => ViewModel.HasMap;
        public bool Is3D => ViewModel.Is3D;
        public bool StartsIn3D => Starts3D;
        public bool CanOrbit => ViewModel.CanOrbit;
        public bool IsCameraLocked => ViewModel.IsCameraLocked;
        public bool HasLegend => ViewModel.HasLegend;
        public bool IsInfoOpen => ViewModel.IsInfoOpen;
        public bool ShowsSnapshotButton => ViewModel.ShowSnapshotButton;

        public event Action Opened3D
        {
            add { ViewModel.Opened3D += value; }
            remove { ViewModel.Opened3D -= value; }
        }

        public event Action Opened2D
        {
            add { ViewModel.Opened2D += value; }
            remove { ViewModel.Opened2D -= value; }
        }

        public event Action XRayPicked
        {
            add { ViewModel.XRayPicked += value; }
            remove { ViewModel.XRayPicked -= value; }
        }

        public event Action CameraRelocked
        {
            add { ViewModel.CameraRelocked += value; }
            remove { ViewModel.CameraRelocked -= value; }
        }

        public event Action InfoOpened
        {
            add { ViewModel.InfoOpened += value; }
            remove { ViewModel.InfoOpened -= value; }
        }

        public event Action SnapshotRequested
        {
            add { ViewModel.SnapshotRequested += value; }
            remove { ViewModel.SnapshotRequested -= value; }
        }

        // Game thread, every update: points the view model at the grid and its reference (ShowGrids) and returns
        // true, or shows why there is nothing to compare (ShowWithoutMap) and returns false.
        protected abstract bool UpdateSources();

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            _presentPending = true;
            ViewModel.InvalidateScene();
        }

        public override void Update()
        {
            ApplyConfig();
            UpdateMap();
            ViewModel.Tick();
            if (ViewModel.Is3D)
                UpdateLegend3D();
            else
                _legend3D = null;
            if (!_syncConfigNextRun)
                return;
            _syncConfigNextRun = false;
            if (Host.Block != null && Host.ProviderConfig != null)
                ConfigManager.Sync(Host.Block, Host.ProviderConfig);
        }

        public override void Close()
        {
            ViewModel.ViewChanged -= OnViewChanged;
            ViewModel.CameraChanged -= OnCameraChanged;
            ViewModel.AxisSelected -= OnAxisSelected;
            ViewModel.Dispose();
            _mapBuilder.Reset();
            base.Close();
        }

        void OnViewChanged()
        {
            DiagnosticConfigComponent config = Settings;
            if (Starts3D)
                config.View2D = !ViewModel.Is3D;
            else
                config.View3D = ViewModel.Is3D;
            config.XRay = ViewModel.State.XRayCategories;
            _presentPending = true;
            _syncConfigNextRun = true;
        }

        void OnCameraChanged()
        {
            if (FixedCamera)
                return;
            DiagnosticConfigComponent config = Settings;
            GridSceneState state = ViewModel.State;
            if (ViewModel.IsCameraLocked)
            {
                ApplyConfiguredCamera(state, config);
                ViewModel.InvalidateScene();
            }
            config.CameraFree = !ViewModel.IsCameraLocked;
            config.CameraYaw = state.Yaw;
            config.CameraPitch = state.Pitch;
            config.CameraZoom = state.Zoom;
            config.CameraPanX = state.Pan.X;
            config.CameraPanY = state.Pan.Y;
            config.CameraPanZ = state.Pan.Z;
            _syncConfigNextRun = true;
        }

        // The next ApplyConfig sees the new axis and locks the camera to it; the 2D map follows it too.
        void OnAxisSelected(int view)
        {
            GeneralComponent.DisplayMode = view;
            _syncConfigNextRun = true;
        }

        void ApplyConfiguredCamera(GridSceneState state, DiagnosticConfigComponent config)
        {
            SetCamera(state, (View)GeneralComponent.DisplayMode, MathHelper.ToRadians(config.Rotation));
            float scale = GeneralComponent.GetScale();
            state.Zoom = MathHelper.Clamp(scale, GridSceneRenderer.MINIMUM_ZOOM, GridSceneRenderer.MAXIMUM_ZOOM);
            ViewModel.RequestFit(scale);
        }

        void ApplyConfig()
        {
            DiagnosticConfigComponent config = Settings;
            bool view3D = Starts3D ? !config.View2D : config.View3D;
            if (view3D != ViewModel.Is3D)
            {
                ViewModel.Set3D(view3D);
                _presentPending = true;
            }
            ViewModel.SetXRay(config.XRay);

            GridSceneState state = ViewModel.State;
            bool changed = false;
            int view = GeneralComponent.DisplayMode;
            ViewModel.SetAxis(view);
            float rotation = config.Rotation;
            float scale = GeneralComponent.GetScale();
            bool first = _knownView < 0;
            if (!FixedCamera && (view != _knownView || rotation != _knownRotation || scale != _knownScale))
            {
                _knownView = view;
                _knownRotation = rotation;
                _knownScale = scale;
                if (first && config.CameraFree)
                {
                    SetCamera(state, (View)view, MathHelper.ToRadians(rotation));
                    state.Yaw = config.CameraYaw;
                    state.Pitch = config.CameraPitch;
                    state.Zoom = MathHelper.Clamp(config.CameraZoom, GridSceneRenderer.MINIMUM_ZOOM,
                        GridSceneRenderer.MAXIMUM_ZOOM);
                    state.Pan = new Vector3(config.CameraPanX, config.CameraPanY, config.CameraPanZ);
                    ViewModel.SetCameraLocked(false);
                }
                else
                {
                    // A new axis, rotation or scale from the terminal takes the camera back to it.
                    ApplyConfiguredCamera(state, config);
                    ViewModel.SetCameraLocked(true);
                    if (!first && config.CameraFree)
                    {
                        config.CameraFree = false;
                        _syncConfigNextRun = true;
                    }
                }
                changed = true;
            }
            var cutMinimum = new Vector3(config.CutMinX, config.CutMinY, config.CutMinZ) / 100f;
            var cutMaximum = new Vector3(config.CutMaxX, config.CutMaxY, config.CutMaxZ) / 100f;
            if (cutMinimum != state.CutMinimum || cutMaximum != state.CutMaximum ||
                config.HighlightMissing != state.HighlightMissing || config.HighlightDamaged != state.HighlightDamaged)
            {
                state.CutMinimum = cutMinimum;
                state.CutMaximum = cutMaximum;
                state.HighlightMissing = config.HighlightMissing;
                state.HighlightDamaged = config.HighlightDamaged;
                changed = true;
            }
            if (changed)
                ViewModel.InvalidateScene();
        }

        // The configured axis is the side the camera looks from.
        static void SetCamera(GridSceneState state, View view, float roll)
        {
            float yaw = 0f;
            float pitch = 0f;
            switch (view)
            {
                case View.Xpos:
                    yaw = MathHelper.PiOver2;
                    break;
                case View.Xneg:
                    yaw = -MathHelper.PiOver2;
                    break;
                case View.Ypos:
                    pitch = MathHelper.PiOver2;
                    break;
                case View.Yneg:
                    pitch = -MathHelper.PiOver2;
                    break;
                case View.Zneg:
                    yaw = MathHelper.Pi;
                    break;
            }
            state.Yaw = yaw;
            state.Pitch = pitch;
            state.Roll = roll;
            state.Pan = Vector3.Zero;
        }

        void UpdateMap()
        {
            try
            {
                if (!UpdateSources())
                    return;
                if (ViewModel.Is3D)
                {
                    ViewModel.Status.Hide();
                    return;
                }
                if (_mapBuilder.Update(ViewModel.MapCells, (View)GeneralComponent.DisplayMode,
                        MathHelper.ToRadians(Settings.Rotation), ViewModel.State.XRayCategories))
                    _presentPending = true;
                if (_mapBuilder.LastFailure != null && _mapBuilder.Result == null)
                    throw _mapBuilder.LastFailure;
                IntegrityMapResult result = _mapBuilder.Result;
                if (result == null)
                {
                    if (ViewModel.HasMap)
                        ViewModel.Status.Hide();
                    else
                        ViewModel.Status.ShowLoading(LocHelper.GetLoc("LoadingPleaseWait"), Host.Surface.ScriptForegroundColor);
                    return;
                }

                ViewModel.Status.Hide();
                float scale = GeneralComponent.GetScale();
                if (!_presentPending && scale == _presentedScale)
                    return;
                _presentPending = false;
                _presentedScale = scale;
                PresentMap(result, scale);
            }
            catch (Exception e)
            {
                ShowWithoutMap(LocHelper.GetLoc("ScreenDebugOfficial_ErrorLogCaption") + "\n" + e.Message, "Warning",
                    ColorComponent.ResolveErrorColor());
            }
        }

        // The 3D view's legend: the X-ray layers in their colors, or the highlighted missing and damaged blocks.
        void UpdateLegend3D()
        {
            GridSceneState state = ViewModel.State;
            int missing = state.HighlightMissing ? ViewModel.MissingCount : 0;
            int damaged = state.HighlightDamaged ? ViewModel.DamagedCount : 0;
            string signature = state.XRayCategories + "/" + missing + "/" + damaged + "/" + LegendSignature;
            if (signature == _legend3D)
                return;
            _legend3D = signature;
            var entries = new List<IntegrityLegendEntry>();
            if (state.XRayCategories != 0)
            {
                foreach (XRayLayerItem layer in ViewModel.XRayLayers)
                    if (layer.IsSelected && layer.Category != GridBlockCategory.None)
                        entries.Add(new IntegrityLegendEntry(layer.Color, layer.Name));
            }
            else
            {
                if (missing > 0)
                    entries.Add(new IntegrityLegendEntry(state.MissingColor,
                        LocHelper.GetLoc(MISSING_BLOCK_LOCKEY) + " (" + missing + ")"));
                if (damaged > 0)
                    entries.Add(new IntegrityLegendEntry(state.DamagedColor,
                        LocHelper.GetLoc(DAMAGED_BLOCK_LOCKEY) + " (" + damaged + ")"));
            }
            AddLegendEntries(entries);
            ViewModel.SetLegend(entries, Color.Transparent);
        }

        void PresentMap(IntegrityMapResult result, float scale)
        {
            if (result.IsEmpty)
            {
                ShowWithoutMap(LocHelper.Empty, "Warning", ColorComponent.ResolveWarningColor());
                ViewModel.SetLegend(new List<IntegrityLegendEntry>(), Color.Transparent);
                return;
            }
            ViewModel.SetMap(result.Cells, result.FitSide, result.Rotation, Math.Max(.05f, scale));
            ViewModel.SetLegend(BuildLegend(result.UsedKinds, result.HasMissing, result.HasDamaged),
                new Color(Host.BackgroundColor.MulValue(0.8f), 0.5f));
        }

        // A subclass's own 3D legend entries, rebuilt whenever the signature changes.
        protected virtual string LegendSignature => string.Empty;

        protected virtual void AddLegendEntries(List<IntegrityLegendEntry> entries)
        {
        }

        protected void ShowWithoutMap(string message, string icon, Color color)
        {
            ViewModel.ClearMap();
            _mapBuilder.Reset();
            ViewModel.Status.Show(message, icon, color);
        }

        protected void SetTitle(string title)
        {
            if (string.Equals(_customTitle, title, StringComparison.Ordinal))
                return;
            _customTitle = title;
            RaisePropertyChanged(nameof(Title));
        }

        public override List<MySprite> GetSprites()
        {
            return _sprites;
        }

        protected static int GetTimeStep(float secondsPerStep)
        {
            var session = MyAPIGateway.Session;
            if (secondsPerStep <= 0f)
                secondsPerStep = 1f / 60f;
            var ticksPerStep = Math.Max(1, (int)Math.Round(secondsPerStep * 60f));
            return session.GameplayFrameCounter / ticksPerStep;
        }

        List<IntegrityLegendEntry> BuildLegend(HashSet<CellKind> usedKinds, bool hasMissing, bool hasDamaged)
        {
            var entries = new List<IntegrityLegendEntry>();
            if (!hasMissing && usedKinds.Count == 0)
                return entries;
            if (usedKinds.Contains(CellKind.None))
                entries.Add(new IntegrityLegendEntry(Color.Gray, GetLegendCaption(CellKind.None)));
            if (hasMissing)
                entries.Add(new IntegrityLegendEntry(IntegrityMapBuilder.MissingColor, LocHelper.GetLoc(MISSING_BLOCK_LOCKEY)));
            if (hasDamaged)
                entries.Add(new IntegrityLegendEntry(IntegrityMapBuilder.DamagedColor, LocHelper.GetLoc(DAMAGED_BLOCK_LOCKEY)));
            foreach (var pair in IntegrityMapBuilder.KindColors)
                if (usedKinds.Contains(pair.Key))
                    entries.Add(new IntegrityLegendEntry(pair.Value, GetLegendCaption(pair.Key)));
            return entries;
        }

        static string GetLegendCaption(CellKind kind)
        {
            string key;
            return LegendCategoryLocKeys.TryGetValue(kind, out key) ? LocHelper.GetLoc(key) : kind.ToString();
        }
    }
}
