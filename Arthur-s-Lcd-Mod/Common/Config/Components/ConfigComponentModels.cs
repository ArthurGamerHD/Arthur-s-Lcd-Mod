// ReSharper disable RedundantUsingDirective
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using ProtoBuf;
using Generated;
using LcdMod.Common.Layout;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Common.Config.Components
{
    [ProtoContract]
    [ProtoInclude(101, typeof(GeneralConfigComponent))]
    [ProtoInclude(102, typeof(ColorConfigComponent))]
    [ProtoInclude(103, typeof(InteractiveConfigComponent))]
    [ProtoInclude(104, typeof(FilterConfigComponent))]
    [ProtoInclude(105, typeof(BlockSelectionConfigComponent))]
    [ProtoInclude(106, typeof(ItemSelectionConfigComponent))]
    [ProtoInclude(107, typeof(BlockReferenceConfigComponent))]
    [ProtoInclude(108, typeof(PowerConfigComponent))]
    [ProtoInclude(109, typeof(RadarConfigComponent))]
    [ProtoInclude(110, typeof(StarMapConfigComponent))]
    [ProtoInclude(111, typeof(DiagnosticConfigComponent))]
    [ProtoInclude(112, typeof(RaycastConfigComponent))]
    [ProtoInclude(113, typeof(RenderProxyConfigComponent))]
    [ProtoInclude(114, typeof(MarkdownConfigComponent))]
    [ProtoInclude(115, typeof(ButtonPanelConfigComponent))]
    [ProtoInclude(116, typeof(DigitalPictureFramesConfigComponent))]
    [ProtoInclude(117, typeof(CargoActionsConfigComponent))]
    [ProtoInclude(118, typeof(NpcMarketConfigComponent))]
    [ProtoInclude(119, typeof(ClockDashboardConfigComponent))]
    [ProtoInclude(120, typeof(VisibleTreeDebugConfigComponent))]
    [ProtoInclude(121, typeof(TabContainerConfigComponent))]
    [ProtoInclude(122, typeof(MediaPlayerConfigComponent))]
    [ProtoInclude(123, typeof(PlanetaryMapConfigComponent))]
    [XmlInclude(typeof(GeneralConfigComponent))]
    [XmlInclude(typeof(ColorConfigComponent))]
    [XmlInclude(typeof(InteractiveConfigComponent))]
    [XmlInclude(typeof(FilterConfigComponent))]
    [XmlInclude(typeof(BlockSelectionConfigComponent))]
    [XmlInclude(typeof(ItemSelectionConfigComponent))]
    [XmlInclude(typeof(BlockReferenceConfigComponent))]
    [XmlInclude(typeof(PowerConfigComponent))]
    [XmlInclude(typeof(RadarConfigComponent))]
    [XmlInclude(typeof(StarMapConfigComponent))]
    [XmlInclude(typeof(DiagnosticConfigComponent))]
    [XmlInclude(typeof(RaycastConfigComponent))]
    [XmlInclude(typeof(RenderProxyConfigComponent))]
    [XmlInclude(typeof(MarkdownConfigComponent))]
    [XmlInclude(typeof(ButtonPanelConfigComponent))]
    [XmlInclude(typeof(DigitalPictureFramesConfigComponent))]
    [XmlInclude(typeof(CargoActionsConfigComponent))]
    [XmlInclude(typeof(NpcMarketConfigComponent))]
    [XmlInclude(typeof(ClockDashboardConfigComponent))]
    [XmlInclude(typeof(VisibleTreeDebugConfigComponent))]
    [XmlInclude(typeof(TabContainerConfigComponent))]
    [XmlInclude(typeof(MediaPlayerConfigComponent))]
    [XmlInclude(typeof(PlanetaryMapConfigComponent))]
    public abstract class ConfigComponent
    {
        public abstract ConfigComponent Clone();
    }

    [ProtoContract]
    public sealed class ConfigComponentEntry
    {
        public ConfigComponentEntry()
        {
        }

        public ConfigComponentEntry(string slot, ConfigComponent value)
        {
            Slot = slot;
            Value = value;
        }

        [ProtoMember(1)] public string Slot { get; set; }
        [ProtoMember(2)] public ConfigComponent Value { get; set; }

        public ConfigComponentEntry Clone()
        {
            return new ConfigComponentEntry(Slot, Value == null ? null : Value.Clone());
        }
    }

    /// <summary>Common component-bearing shape shared by top-level and deferred nested configs.</summary>
    public interface IComponentContainer
    {
        List<ConfigComponentEntry> Components { get; set; }
    }

    /// <summary>A top-level persisted app configuration with a concrete generated app identity.</summary>
    public interface IAppConfig : IComponentContainer
    {
        int AppTypeId { get; set; }
    }

    public static class ComponentConfigExtensions
    {
        public static T TryGet<T>(this IComponentContainer config, string slot) where T : ConfigComponent
        {
            if (config == null || config.Components == null)
                return null;

            var entry = config.Components.FirstOrDefault(component =>
                component != null && component.Slot == slot && component.Value is T);
            return entry == null ? null : entry.Value as T;
        }

        public static T Get<T>(this IComponentContainer config, string slot) where T : ConfigComponent
        {
            var component = config.TryGet<T>(slot);
            if (component == null)
                throw new InvalidOperationException(
                    $"Missing config component '{slot}' ({typeof(T).Name}) for app {GetAppIdentity(config)}.");
            return component;
        }

        public static T TryGetComponent<T>(this IComponentContainer config) where T : ConfigComponent
        {
            if (config == null || config.Components == null)
                return null;

            T result = null;
            foreach (var entry in config.Components)
            {
                var component = entry == null ? null : entry.Value as T;
                if (component == null)
                    continue;
                if (result != null)
                    throw new InvalidOperationException(
                        $"Multiple {typeof(T).Name} components exist for app {GetAppIdentity(config)}; use the slot overload.");
                result = component;
            }
            return result;
        }

        public static T GetComponent<T>(this IComponentContainer config) where T : ConfigComponent
        {
            var component = config.TryGetComponent<T>();
            if (component == null)
                throw new InvalidOperationException(
                    $"Missing {typeof(T).Name} component for app {GetAppIdentity(config)}.");
            return component;
        }

        public static T TryGetComponent<T>(this IComponentContainer config, string slot) where T : ConfigComponent
        {
            return config.TryGet<T>(slot);
        }

        public static T GetComponent<T>(this IComponentContainer config, string slot) where T : ConfigComponent
        {
            return config.Get<T>(slot);
        }

        public static void Set(this IComponentContainer config, string slot, ConfigComponent component)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (config.Components == null)
                config.Components = new List<ConfigComponentEntry>();

            var existing = config.Components.FirstOrDefault(entry => entry != null && entry.Slot == slot);
            if (existing == null)
                config.Components.Add(new ConfigComponentEntry(slot, component));
            else
                existing.Value = component;
        }

        /// <summary>
        /// Copies only slots that exist in both schemas and have the same component data shape.
        /// Reference components therefore copy only when their semantic slot also matches.
        /// </summary>
        public static void CopyCompatibleFrom(this IComponentContainer targetConfig, IComponentContainer sourceConfig)
        {
            if (sourceConfig?.Components == null || targetConfig?.Components == null)
                return;

            foreach (var target in targetConfig.Components)
            {
                if (target?.Value == null)
                    continue;

                var sourceEntry = sourceConfig.Components.FirstOrDefault(candidate =>
                    candidate?.Value != null
                    && candidate.Slot == target.Slot
                    && candidate.Value.GetType() == target.Value.GetType());

                if (sourceEntry != null)
                    target.Value = sourceEntry.Value.Clone();
            }
        }

        public static List<ConfigComponentEntry> CloneComponents(this IComponentContainer config)
        {
            return config?.Components == null
                ? new List<ConfigComponentEntry>()
                : config.Components.Where(entry => entry != null).Select(entry => entry.Clone()).ToList();
        }

        static int GetAppIdentity(IComponentContainer config)
        {
            var appConfig = config as IAppConfig;
            if (appConfig != null)
                return appConfig.AppTypeId;

            var nested = config as AppInstanceConfig;
            return nested == null ? 0 : nested.AppKind;
        }
    }

    /// <summary>
    /// Deferred nested-app data. Its legacy AppKind is intentionally preserved until nested app
    /// identity and factories are designed in a later migration.
    /// </summary>
    [ProtoContract]
    public sealed class AppInstanceConfig : IComponentContainer
    {
        [ProtoMember(1)] public ulong InstanceId { get; set; }
        [ProtoMember(2)] public int AppKind { get; set; }
        [ProtoMember(3)] public string Title { get; set; }
        [ProtoMember(4)] public List<ConfigComponentEntry> Components { get; set; } = new List<ConfigComponentEntry>();

        public AppInstanceConfig Clone()
        {
            return new AppInstanceConfig
            {
                InstanceId = InstanceId,
                AppKind = AppKind,
                Title = Title,
                Components = this.CloneComponents()
            };
        }
    }

    [ProtoContract]
    public sealed class SurfaceConfig : IAppConfig
    {
        [ProtoMember(1)] public int SurfaceIndex { get; set; }

        // Public V0 migration hint. Migration-only; never write new AppType values here.
        [ProtoMember(2)]
        [XmlElement("AppKind")]
        public int LegacyAppKind { get; set; }

        [ProtoMember(3)]
        [XmlArrayItem("Component")]
        public List<ConfigComponentEntry> Components { get; set; } = new List<ConfigComponentEntry>();

        // Component-schema V1 concrete app identity generated from [LcdApp].
        [ProtoMember(4)] public int AppTypeId { get; set; }

        public SurfaceConfig Clone()
        {
            return new SurfaceConfig
            {
                SurfaceIndex = SurfaceIndex,
                LegacyAppKind = LegacyAppKind,
                AppTypeId = AppTypeId,
                Components = this.CloneComponents()
            };
        }
    }

    /// <summary>
    /// The only component that owns multiple independently configured app instances. Ordinary
    /// surface scripts store AppTypeId and Components directly on SurfaceConfig.
    /// </summary>
    [ProtoContract]
    public sealed class TabContainerConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public ulong ActiveAppInstanceId { get; set; }
        [ProtoMember(2)] public ulong NextAppInstanceId { get; set; } = 1;
        [ProtoMember(3)] public List<AppInstanceConfig> Apps { get; set; } = new List<AppInstanceConfig>();

        public ulong AllocateAppInstanceId()
        {
            NormalizeNextAppInstanceId();
            return NextAppInstanceId++;
        }

        public AppInstanceConfig GetActiveApp()
        {
            if (Apps == null || Apps.Count == 0)
                return null;

            var selected = Apps.FirstOrDefault(app =>
                app != null && app.InstanceId == ActiveAppInstanceId);
            return selected ?? Apps.FirstOrDefault(app => app != null);
        }

        public void ReplaceActiveApp(AppInstanceConfig app)
        {
            if (Apps == null)
                Apps = new List<AppInstanceConfig>();

            var active = GetActiveApp();
            if (active == null)
                Apps.Add(app);
            else
                Apps[Apps.IndexOf(active)] = app;

            ActiveAppInstanceId = app == null ? 0 : app.InstanceId;
            NormalizeNextAppInstanceId();
        }

        public void NormalizeAppInstanceIds()
        {
            if (Apps == null)
                Apps = new List<AppInstanceConfig>();

            for (var i = Apps.Count - 1; i >= 0; i--)
                if (Apps[i] == null)
                    Apps.RemoveAt(i);

            var used = new HashSet<ulong>();
            ulong next = NextAppInstanceId == 0 ? 1 : NextAppInstanceId;
            foreach (var app in Apps)
            {
                if (app.InstanceId != 0 && used.Add(app.InstanceId))
                {
                    if (app.InstanceId >= next)
                        next = app.InstanceId + 1;
                    continue;
                }

                while (next == 0 || used.Contains(next))
                    next++;
                app.InstanceId = next;
                used.Add(next);
                next++;
            }

            if (Apps.Count == 0)
                ActiveAppInstanceId = 0;
            else if (ActiveAppInstanceId == 0 || !used.Contains(ActiveAppInstanceId))
                ActiveAppInstanceId = Apps[0].InstanceId;

            NextAppInstanceId = next == 0 ? 1 : next;
        }

        public void NormalizeNextAppInstanceId()
        {
            NormalizeAppInstanceIds();
        }

        public override ConfigComponent Clone()
        {
            return new TabContainerConfigComponent
            {
                ActiveAppInstanceId = ActiveAppInstanceId,
                NextAppInstanceId = NextAppInstanceId,
                Apps = Apps == null
                    ? new List<AppInstanceConfig>()
                    : Apps.Where(app => app != null).Select(app => app.Clone()).ToList()
            };
        }
    }

    [ProtoContract]
    public sealed class GeneralConfigComponent : ConfigComponent
    {
        [ProtoMember(1)]
        [TerminalControlSwitch(
            2600,
            "TitleSwitch",
            "BlockPropertyTitle_TextPanelPublicTitle",
            Slot = GENERAL,
            MinimumHeightToWidthRatio = MIN_SCREEN_HEIGHT_TO_WIDTH_RATIO,
            TitleSuffix = "RadialMenuAction_Hud_Visible")]
        public bool TitleVisible { get; set; } = true;
        [ProtoMember(2)] public float InternalScale { get; set; } = 1f;
        [ProtoMember(3)] public bool DrawLines { get; set; }
        [ProtoMember(4)] public int DisplayMode { get; set; }
        [ProtoMember(5)] public OptionalValue<byte> BackgroundAlpha { get; set; } = new OptionalValue<byte>();
        [ProtoMember(6)]
        [XmlIgnore]
        public Dictionary<string, byte[]> CustomData { get; set; } = new Dictionary<string, byte[]>();

        [ProtoIgnore]
        [XmlArray("CustomData")]
        [XmlArrayItem("Entry")]
        public ConfigCustomDataXmlEntry[] CustomDataXml
        {
            get
            {
                if (CustomData == null || CustomData.Count == 0)
                    return null;

                return CustomData
                    .Where(entry => !string.IsNullOrEmpty(entry.Key) && entry.Value != null)
                    .Select(entry => new ConfigCustomDataXmlEntry
                    {
                        Key = entry.Key,
                        Value = Convert.ToBase64String(entry.Value)
                    })
                    .ToArray();
            }
            set
            {
                CustomData = new Dictionary<string, byte[]>();
                if (value == null)
                    return;

                foreach (var entry in value)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Key) || string.IsNullOrEmpty(entry.Value))
                        continue;

                    try
                    {
                        CustomData[entry.Key] = Convert.FromBase64String(entry.Value);
                    }
                    catch (FormatException)
                    {
                        // Keep debug XML editing resilient to malformed custom-data entries.
                    }
                }
            }
        }

        public override ConfigComponent Clone()
        {
            var customData = new Dictionary<string, byte[]>();
            if (CustomData != null)
            {
                foreach (var pair in CustomData)
                {
                    if (pair.Key != null)
                        customData[pair.Key] = pair.Value == null ? null : (byte[])pair.Value.Clone();
                }
            }

            return new GeneralConfigComponent
            {
                TitleVisible = TitleVisible,
                InternalScale = InternalScale,
                DrawLines = DrawLines,
                DisplayMode = DisplayMode,
                BackgroundAlpha = ConfigComponentClone.Copy(BackgroundAlpha),
                CustomData = customData
            };
        }
    }

    [ProtoContract]
    public sealed class ColorConfigComponent : ConfigComponent
    {
        [ProtoMember(1)]
        [TerminalControlColor(
            2300,
            "HeaderColor",
            "BlockPropertyTitle_TextPanelPublicTitle",
            Slot = COLORS,
            RequiresCustomColor = true)]
        public OptionalValue<Color> HeaderColor { get; set; } = new OptionalValue<Color>();
        [ProtoMember(2)]
        [TerminalControlColor(
            2500,
            "ErrorColor",
            "ContractScreen_Aministration_CreatinResultCaption_Error",
            Slot = COLORS,
            RequiresCustomColor = true)]
        public OptionalValue<Color> ErrorColor { get; set; } = new OptionalValue<Color>();
        [ProtoMember(3)]
        [TerminalControlColor(
            2400,
            "WarningColor",
            "SalvageService_InventoryWarning_Title",
            Slot = COLORS,
            RequiresCustomColor = true)]
        public OptionalValue<Color> WarningColor { get; set; } = new OptionalValue<Color>();
        [ProtoMember(4)]
        [TerminalControlSwitch(
            2200,
            "SwitchToggleCustomColors",
            "WorldSettings_ViewDistance_Custom",
            Slot = COLORS,
            TitleSuffix = "ScreenAdmin_Safezone_ColorLabel",
            RefreshTerminalOnSet = true)]
        public bool CustomizedColors { get; set; }

        public override ConfigComponent Clone()
        {
            return new ColorConfigComponent
            {
                HeaderColor = ConfigComponentClone.Copy(HeaderColor),
                ErrorColor = ConfigComponentClone.Copy(ErrorColor),
                WarningColor = ConfigComponentClone.Copy(WarningColor),
                CustomizedColors = CustomizedColors
            };
        }
    }

    [ProtoContract]
    public sealed class InteractiveConfigComponent : ConfigComponent
    {
        [ProtoMember(1)]
        [TerminalControlSlider(
            2800,
            "CursorScaleSlider",
            MOD_PREFIX + "CursorScale",
            0f,
            GeneralConfigComponentExtensions.MAX_SCALE,
            "0.000",
            Slot = INTERACTION)]
        public float CursorScale { get; set; } = 1f;
        [ProtoMember(2)] public bool RequiresAlt { get; set; } = true;
        [ProtoMember(3)] public int ReferenceMode { get; set; }
        [ProtoMember(4)] public float AutoScrollStep { get; set; } = 2f;

        public override ConfigComponent Clone()
        {
            return new InteractiveConfigComponent
            {
                CursorScale = CursorScale,
                RequiresAlt = RequiresAlt,
                ReferenceMode = ReferenceMode,
                AutoScrollStep = AutoScrollStep
            };
        }
    }

    [ProtoContract]
    public sealed class FilterConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public int SortMethod { get; set; }
        [ProtoMember(2)] public bool HideEmpty { get; set; } = true;

        public override ConfigComponent Clone()
        {
            return new FilterConfigComponent { SortMethod = SortMethod, HideEmpty = HideEmpty };
        }
    }

    [ProtoContract]
    public sealed class BlockSelectionConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public long[] SelectedBlocks { get; set; } = Array.Empty<long>();
        [ProtoMember(2)] public string[] SelectedGroups { get; set; } = Array.Empty<string>();
        [ProtoMember(3)] public int GridLinkTypeInternal { get; set; } = 1;
        [ProtoMember(4)] public string[] SortFilterKeys { get; set; } = Array.Empty<string>();
        [ProtoMember(5)] public string[] SortFilterCategories { get; set; } = Array.Empty<string>();

        public override ConfigComponent Clone()
        {
            return new BlockSelectionConfigComponent
            {
                SelectedBlocks = ConfigComponentClone.Copy(SelectedBlocks),
                SelectedGroups = ConfigComponentClone.Copy(SelectedGroups),
                GridLinkTypeInternal = GridLinkTypeInternal,
                SortFilterKeys = ConfigComponentClone.Copy(SortFilterKeys),
                SortFilterCategories = ConfigComponentClone.Copy(SortFilterCategories)
            };
        }
    }

    [ProtoContract]
    public sealed class ItemSelectionConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public string[] SelectedDefinition { get; set; } = Array.Empty<string>();
        [ProtoMember(2)] public string[] SelectedCategories { get; set; } = Array.Empty<string>();

        public override ConfigComponent Clone()
        {
            return new ItemSelectionConfigComponent
            {
                SelectedDefinition = ConfigComponentClone.Copy(SelectedDefinition),
                SelectedCategories = ConfigComponentClone.Copy(SelectedCategories)
            };
        }
    }

    [ProtoContract]
    public sealed class BlockReferenceConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public long EntityId { get; set; }

        public override ConfigComponent Clone()
        {
            return new BlockReferenceConfigComponent { EntityId = EntityId };
        }
    }

    [ProtoContract]
    public sealed class PowerConfigComponent : ConfigComponent
    {
        [ProtoMember(1), DefaultValue(true)] public bool HideEmpty { get; set; } = true;
        [ProtoMember(2), DefaultValue(2)] public int GraphWindowIndex { get; set; } = 2;
        [ProtoMember(3), DefaultValue(-1)] public int PowerHistoryTier { get; set; } = -1;
        [ProtoMember(4), DefaultValue((int)GridLinkTypeEnum.Mechanical)] public int GridLinkTypeInternal { get; set; } = (int)GridLinkTypeEnum.Mechanical;

        public override ConfigComponent Clone()
        {
            return new PowerConfigComponent
            {
                HideEmpty = HideEmpty,
                GraphWindowIndex = GraphWindowIndex,
                PowerHistoryTier = PowerHistoryTier,
                GridLinkTypeInternal = GridLinkTypeInternal
            };
        }
    }

    [ProtoContract]
    public sealed class RadarConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public float RangeScale { get; set; } = 1f;
        [ProtoMember(2)] public double CameraPanX { get; set; }
        [ProtoMember(3)] public double CameraPanY { get; set; }
        [ProtoMember(4)] public float CameraZoomScale { get; set; } = 1f;

        public override ConfigComponent Clone()
        {
            return new RadarConfigComponent
            {
                RangeScale = RangeScale,
                CameraPanX = CameraPanX,
                CameraPanY = CameraPanY,
                CameraZoomScale = CameraZoomScale
            };
        }
    }

    public interface IGpsDisplayConfig
    {
        bool DisplayMyGps { get; set; }
        bool IncludeRadioSignals { get; set; }
        GpsDisplayWaypoint[] AlwaysDisplayedGpsWaypoints { get; set; }
        int[] AlwaysDisplayedGpsHashes { get; set; }
    }

    [ProtoContract]
    public sealed class GpsDisplayWaypoint
    {
        [ProtoMember(1)] public int SourceHash { get; set; }
        [ProtoMember(2)] public string Name { get; set; } = string.Empty;
        [ProtoMember(3)] public double X { get; set; }
        [ProtoMember(4)] public double Y { get; set; }
        [ProtoMember(5)] public double Z { get; set; }
        [ProtoMember(6)] public Color Color { get; set; } = new Color(117, 201, 241);

        public GpsDisplayWaypoint Clone()
        {
            return new GpsDisplayWaypoint
            {
                SourceHash = SourceHash,
                Name = Name,
                X = X,
                Y = Y,
                Z = Z,
                Color = Color
            };
        }
    }

    [ProtoContract]
    public sealed class StarMapConfigComponent : ConfigComponent, IGpsDisplayConfig
    {
        [ProtoMember(1)] public float FoV { get; set; } = 70f;
        [ProtoMember(2)] public bool DisplayMyGps { get; set; }
        [ProtoMember(3)] public int[] AlwaysDisplayedGpsHashes { get; set; } = Array.Empty<int>();
        [ProtoMember(4)] public bool IncludeRadioSignals { get; set; }
        [ProtoMember(5)] public GpsDisplayWaypoint[] AlwaysDisplayedGpsWaypoints { get; set; } =
            Array.Empty<GpsDisplayWaypoint>();
        [ProtoMember(6)] public float StaticOrbitYawRadians { get; set; }
        [ProtoMember(7)] public float StaticOrbitPitchRadians { get; set; }
        [ProtoMember(8)] public long StaticFocusPlanetId { get; set; }
        [ProtoMember(9)] public double StaticCameraTargetOffsetX { get; set; }
        [ProtoMember(10)] public double StaticCameraTargetOffsetY { get; set; }
        [ProtoMember(11)] public double StaticCameraTargetOffsetZ { get; set; }

        public override ConfigComponent Clone()
        {
            return new StarMapConfigComponent
            {
                FoV = FoV,
                DisplayMyGps = DisplayMyGps,
                IncludeRadioSignals = IncludeRadioSignals,
                StaticOrbitYawRadians = StaticOrbitYawRadians,
                StaticOrbitPitchRadians = StaticOrbitPitchRadians,
                StaticFocusPlanetId = StaticFocusPlanetId,
                StaticCameraTargetOffsetX = StaticCameraTargetOffsetX,
                StaticCameraTargetOffsetY = StaticCameraTargetOffsetY,
                StaticCameraTargetOffsetZ = StaticCameraTargetOffsetZ,
                AlwaysDisplayedGpsHashes = ConfigComponentClone.Copy(AlwaysDisplayedGpsHashes),
                AlwaysDisplayedGpsWaypoints = ConfigComponentClone.Copy(AlwaysDisplayedGpsWaypoints)
            };
        }
    }

    [ProtoContract]
    public sealed class PlanetaryMapConfigComponent : ConfigComponent, IGpsDisplayConfig
    {
        [ProtoMember(1), DefaultValue(true)] public bool NorthUp { get; set; } = true;
        [ProtoMember(2), DefaultValue(true)] public bool FollowCamera { get; set; } = true;
        [ProtoMember(3)] public float OrbitYawRadians { get; set; }
        [ProtoMember(4)] public float OrbitPitchRadians { get; set; }
        [ProtoMember(5)] public bool HasStaticCameraPosition { get; set; }
        [ProtoMember(6)] public double StaticCameraPositionX { get; set; }
        [ProtoMember(7)] public double StaticCameraPositionY { get; set; }
        [ProtoMember(8)] public double StaticCameraPositionZ { get; set; }
        [ProtoMember(9), DefaultValue(16f)] public float Zoom { get; set; } = 16f;
        [ProtoMember(10)] public bool DisplayMyGps { get; set; }
        [ProtoMember(11)] public int[] AlwaysDisplayedGpsHashes { get; set; } = Array.Empty<int>();
        [ProtoMember(12)] public bool IncludeRadioSignals { get; set; }
        [ProtoMember(13)] public GpsDisplayWaypoint[] AlwaysDisplayedGpsWaypoints { get; set; } =
            Array.Empty<GpsDisplayWaypoint>();
        [ProtoMember(14)] public int MapLayer { get; set; }

        public override ConfigComponent Clone()
        {
            return new PlanetaryMapConfigComponent
            {
                NorthUp = NorthUp,
                FollowCamera = FollowCamera,
                OrbitYawRadians = OrbitYawRadians,
                OrbitPitchRadians = OrbitPitchRadians,
                HasStaticCameraPosition = HasStaticCameraPosition,
                StaticCameraPositionX = StaticCameraPositionX,
                StaticCameraPositionY = StaticCameraPositionY,
                StaticCameraPositionZ = StaticCameraPositionZ,
                Zoom = Zoom,
                DisplayMyGps = DisplayMyGps,
                IncludeRadioSignals = IncludeRadioSignals,
                AlwaysDisplayedGpsHashes = ConfigComponentClone.Copy(AlwaysDisplayedGpsHashes),
                AlwaysDisplayedGpsWaypoints = ConfigComponentClone.Copy(AlwaysDisplayedGpsWaypoints),
                MapLayer = MapLayer
            };
        }
    }

    [ProtoContract]
    public sealed class DiagnosticConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public float Rotation { get; set; }
        public override ConfigComponent Clone() => new DiagnosticConfigComponent { Rotation = Rotation };
    }

    [ProtoContract]
    public sealed class RaycastConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public int RelationOverlay { get; set; } = 1;
        [ProtoMember(2)] public float RenderScale { get; set; } = .2f;
        [ProtoMember(3)]
        [TerminalControlSlider(
            3100,
            "RaysPerTickSlider",
            MOD_PREFIX + "RaysPerTick",
            2f,
            256f,
            "0",
            Slot = APP,
            RequiresAdvancedTweakables = true)]
        public int RaysPerTick { get; set; } = 32;

        public override ConfigComponent Clone()
        {
            return new RaycastConfigComponent
            {
                RelationOverlay = RelationOverlay,
                RenderScale = RenderScale,
                RaysPerTick = RaysPerTick
            };
        }
    }

    [ProtoContract]
    public sealed class RenderProxyConfigComponent : ConfigComponent
    {
        [ProtoMember(1)]
        [TerminalControlSlider(
            5700,
            "SliderProxyX",
            MOD_PREFIX + "ProxyOffsetX",
            -16f,
            16f,
            "0",
            Slot = APP)]
        public sbyte XAxisOffset { get; set; }
        [ProtoMember(2)]
        [TerminalControlSlider(
            5800,
            "SliderProxyY",
            MOD_PREFIX + "ProxyOffsetY",
            -16f,
            16f,
            "0",
            Slot = APP)]
        public sbyte YAxisOffset { get; set; }
        [ProtoMember(3)]
        [TerminalControlSwitch(
            5900,
            "ProxyAutoAdjustSwitch",
            MOD_PREFIX + "EnableAutoAdjust",
            Slot = APP,
            Tooltip = MOD_PREFIX + "EnableAutoAdjust_Tooltip")]
        public bool EnableAutoAdjust { get; set; } = true;

        public override ConfigComponent Clone()
        {
            return new RenderProxyConfigComponent
            {
                XAxisOffset = XAxisOffset,
                YAxisOffset = YAxisOffset,
                EnableAutoAdjust = EnableAutoAdjust
            };
        }
    }

    [ProtoContract]
    public sealed class MarkdownConfigComponent : ConfigComponent
    {
        public const string DEFAULT_TEXT = @"# This is a Title

This is a paragraph with **bold**, *italic*, and [color:#00FF00]colored text[/color].

---

## This is a List

1. This is the first item
2. This item uses [font:""monospace""]monospace text[/font]
3. This item uses [color:#FF0000][font:""monospace""]red monospace text[/font][/color]

---

![Connector](sprite:MyObjectBuilder_ShipConnector/LargeBlockInsetConnector) ![Arrow](sprite:Arrow) ![Danger](sprite:Danger) Images from Sprites.

---

###### This is a Small Heading

Click [color:#0000FF]""[loc]BlockPropertyTitle_TextPanelShowTextPanel[/loc]""[/color] to edit this text";

        [ProtoMember(1)] public string RawText { get; set; } = DEFAULT_TEXT;
        public override ConfigComponent Clone() => new MarkdownConfigComponent { RawText = RawText };
    }

    [ProtoContract]
    public sealed class ButtonPanelConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public bool HideEmpty { get; set; }
        [ProtoMember(2)] public int ButtonCount { get; set; } = ButtonPanelLayout.DefaultButtonCount;
        [ProtoMember(3)] public int ButtonStyle { get; set; } = (int)ButtonPanelStyle.Default;

        public override ConfigComponent Clone()
        {
            return new ButtonPanelConfigComponent
            {
                HideEmpty = HideEmpty,
                ButtonCount = ButtonCount,
                ButtonStyle = ButtonStyle
            };
        }
    }

    [ProtoContract]
    public sealed class DigitalPictureFramesConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public string BackgroundSprite { get; set; } = string.Empty;
        [ProtoMember(2)] public string[] SelectedSprites { get; set; } = Array.Empty<string>();
        [ProtoMember(3)]
        [TerminalControlSlider(
            5500,
            "ImageChangeIntervalSlider",
            "BlockPropertyTitle_LCDScreenRefreshInterval",
            0f,
            30f,
            "0.000",
            Slot = APP,
            WriterSuffix = " s")]
        public float ImageChangeInterval { get; set; }

        public override ConfigComponent Clone()
        {
            return new DigitalPictureFramesConfigComponent
            {
                BackgroundSprite = BackgroundSprite,
                SelectedSprites = ConfigComponentClone.Copy(SelectedSprites),
                ImageChangeInterval = ImageChangeInterval
            };
        }
    }

    [ProtoContract]
    public sealed class CargoActionsConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public int SortMode { get; set; }
        [ProtoMember(2)] public int UraniumLargeGridSmallReactor { get; set; } = 4;
        [ProtoMember(3)] public int UraniumLargeGridLargeReactor { get; set; } = 10;
        [ProtoMember(4)] public int UraniumSmallGridSmallReactor { get; set; } = 1;
        [ProtoMember(5)] public int UraniumSmallGridLargeReactor { get; set; } = 5;
        [ProtoMember(6)] public int AmmoDefaultPerWeapon { get; set; } = 10;
        [ProtoMember(7)] public string[] WeaponOverrideKeys { get; set; } = Array.Empty<string>();
        [ProtoMember(8)] public int[] WeaponOverrideCounts { get; set; } = Array.Empty<int>();
        [ProtoMember(9)] public int SettingsRevision { get; set; }
        [ProtoMember(10)]
        [TerminalControlSwitch(
            6100,
            "CargoActionsShowConfigButton",
            MOD_PREFIX + "CargoActions_ShowConfigButton",
            Slot = APP,
            Tooltip = MOD_PREFIX + "CargoActions_ShowConfigButton_Tooltip")]
        public bool ShowConfigButton { get; set; } = true;
        [ProtoMember(11)] public int GridLinkTypeInternal { get; set; } = 1;

        public override ConfigComponent Clone()
        {
            return new CargoActionsConfigComponent
            {
                SortMode = SortMode,
                UraniumLargeGridSmallReactor = UraniumLargeGridSmallReactor,
                UraniumLargeGridLargeReactor = UraniumLargeGridLargeReactor,
                UraniumSmallGridSmallReactor = UraniumSmallGridSmallReactor,
                UraniumSmallGridLargeReactor = UraniumSmallGridLargeReactor,
                AmmoDefaultPerWeapon = AmmoDefaultPerWeapon,
                WeaponOverrideKeys = ConfigComponentClone.Copy(WeaponOverrideKeys),
                WeaponOverrideCounts = ConfigComponentClone.Copy(WeaponOverrideCounts),
                SettingsRevision = SettingsRevision,
                ShowConfigButton = ShowConfigButton,
                GridLinkTypeInternal = GridLinkTypeInternal
            };
        }
    }

    [ProtoContract]
    public sealed class NpcMarketConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public int SelectedMode { get; set; }
        [ProtoMember(2)] public float ScrollOffsetPixels { get; set; }
        [ProtoMember(3)] public int BuySortColumn { get; set; } = 1;
        [ProtoMember(4)] public bool BuySortDescending { get; set; }
        [ProtoMember(5)] public int SellSortColumn { get; set; } = 1;
        [ProtoMember(6)] public bool SellSortDescending { get; set; } = true;
        [ProtoMember(7)] public int BothSortColumn { get; set; }
        [ProtoMember(8)] public bool BothSortDescending { get; set; }
        [ProtoMember(9)] public float HorizontalScrollOffsetPixels { get; set; }
        [ProtoMember(10)] public float VerticalScrollOffsetPixels { get; set; }
        [ProtoMember(11)] public float MaxDistanceMeters { get; set; } = 10000001f;
        [ProtoMember(12)] public float PageSwitchSeconds { get; set; } = 5f;
        [ProtoMember(13)] public string SearchQuery { get; set; } = string.Empty;

        public override ConfigComponent Clone()
        {
            return new NpcMarketConfigComponent
            {
                SelectedMode = SelectedMode,
                ScrollOffsetPixels = ScrollOffsetPixels,
                BuySortColumn = BuySortColumn,
                BuySortDescending = BuySortDescending,
                SellSortColumn = SellSortColumn,
                SellSortDescending = SellSortDescending,
                BothSortColumn = BothSortColumn,
                BothSortDescending = BothSortDescending,
                HorizontalScrollOffsetPixels = HorizontalScrollOffsetPixels,
                VerticalScrollOffsetPixels = VerticalScrollOffsetPixels,
                MaxDistanceMeters = MaxDistanceMeters,
                PageSwitchSeconds = PageSwitchSeconds,
                SearchQuery = SearchQuery
            };
        }
    }

    [ProtoContract]
    public sealed class ClockDashboardConfigComponent : ConfigComponent
    {
        [ProtoMember(1)]
        [TerminalControlSwitch(
            1900,
            "ClockDashboard24Hour",
            MOD_PREFIX + "ClockDashboard_Control_24Hour",
            Slot = APP)]
        public bool Use24HourClock { get; set; } = true;
        [ProtoMember(2)] public int TemperatureModeInternal { get; set; }

        public override ConfigComponent Clone()
        {
            return new ClockDashboardConfigComponent
            {
                Use24HourClock = Use24HourClock,
                TemperatureModeInternal = TemperatureModeInternal
            };
        }
    }

    [ProtoContract]
    public sealed class VisibleTreeDebugConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public int ReferenceScreenIndex { get; set; }
        public override ConfigComponent Clone() => new VisibleTreeDebugConfigComponent { ReferenceScreenIndex = ReferenceScreenIndex };
    }


    [ProtoContract]
    public sealed class MediaPlayerConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public string SelectedSoundSubtype { get; set; } = string.Empty;
        [ProtoMember(2)] public int SelectedIndex { get; set; } = -1;
        [ProtoMember(3)] public bool AutoPlay { get; set; }
        [ProtoMember(4)]
        [TerminalControlSwitch(
            8700,
            "MediaPlayerVisualizer",
            MOD_PREFIX + "MediaPlayer_Visualizer",
            Slot = APP)]
        public bool VisualizerEnabled { get; set; } = true;
        [ProtoMember(5)] public bool ShuffleEnabled { get; set; }
        [ProtoMember(6)] public int RepeatModeInternal { get; set; }
        [ProtoMember(7)] public string SelectedAudioSource { get; set; } = string.Empty;
        [ProtoMember(8)] public string SelectedPickerFullPath { get; set; } = string.Empty;
        [ProtoMember(9)] public string[] PlaylistPaths { get; set; } = Array.Empty<string>();
        [ProtoMember(10)] public string[] PlaylistTitles { get; set; } = Array.Empty<string>();
        [ProtoMember(11)] public int PlaylistIndex { get; set; } = -1;
        [ProtoMember(12)] public int ShuffleSeed { get; set; }

        public override ConfigComponent Clone()
        {
            return new MediaPlayerConfigComponent
            {
                SelectedSoundSubtype = SelectedSoundSubtype,
                SelectedIndex = SelectedIndex,
                AutoPlay = AutoPlay,
                VisualizerEnabled = VisualizerEnabled,
                ShuffleEnabled = ShuffleEnabled,
                RepeatModeInternal = RepeatModeInternal,
                SelectedAudioSource = SelectedAudioSource,
                SelectedPickerFullPath = SelectedPickerFullPath,
                PlaylistPaths = ConfigComponentClone.Copy(PlaylistPaths),
                PlaylistTitles = ConfigComponentClone.Copy(PlaylistTitles),
                PlaylistIndex = PlaylistIndex,
                ShuffleSeed = ShuffleSeed
            };
        }
    }

    public sealed class ConfigCustomDataXmlEntry
    {
        [XmlAttribute]
        public string Key { get; set; }

        [XmlText]
        public string Value { get; set; }
    }

    static class ConfigComponentClone
    {
        public static OptionalValue<T> Copy<T>(OptionalValue<T> value)
        {
            return value == null
                ? new OptionalValue<T>()
                : new OptionalValue<T> { HasValue = value.HasValue, Value = value.Value };
        }

        public static T[] Copy<T>(T[] values)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<T>();
            return (T[])values.Clone();
        }

        public static GpsDisplayWaypoint[] Copy(GpsDisplayWaypoint[] values)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<GpsDisplayWaypoint>();

            var copy = new GpsDisplayWaypoint[values.Length];
            for (var i = 0; i < values.Length; i++)
                copy[i] = values[i] == null ? null : values[i].Clone();
            return copy;
        }
    }
}
