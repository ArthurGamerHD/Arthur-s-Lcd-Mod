using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using ProtoBuf;
using LcdMod.Common.Config;
using VRageMath;

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
    public abstract class ConfigComponent
    {
        public abstract ConfigComponent Clone();
    }

    public static class ConfigSlots
    {
        public const string General = "core.general";
        public const string Colors = "core.colors";
        public const string Interaction = "core.interaction";
        public const string Filters = "data.filters";
        public const string Blocks = "data.blocks";
        public const string Items = "data.items";
        public const string App = "app.settings";
        public const string Tabs = "app.tabs";

        // The slot, not the component CLR type, identifies the semantic use of a reference.
        public const string ProjectorReference = "reference.projector";
        public const string DockableReference = "reference.dockable";
        public const string RenderProxyReference = "reference.render-proxy-source";
        public const string OreScannerReference = "reference.ore-scanner";
        public const string VisibleTreeReference = "reference.visible-tree";
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

    /// <summary>
    /// Common component-bearing shape. A normal surface implements this directly; app instances
    /// are reserved for the nested entries owned by the tab-container component.
    /// </summary>
    public interface IComponentConfig
    {
        int AppKind { get; set; }
        List<ConfigComponentEntry> Components { get; set; }
    }

    public static class ComponentConfigExtensions
    {
        public static T TryGet<T>(this IComponentConfig config, string slot) where T : ConfigComponent
        {
            if (config == null || config.Components == null)
                return null;

            var entry = config.Components.FirstOrDefault(component =>
                component != null && component.Slot == slot && component.Value is T);
            return entry == null ? null : entry.Value as T;
        }

        public static T Get<T>(this IComponentConfig config, string slot) where T : ConfigComponent
        {
            var component = config.TryGet<T>(slot);
            if (component == null)
                throw new InvalidOperationException(
                    $"Missing config component '{slot}' ({typeof(T).Name}) for app {config?.AppKind}.");
            return component;
        }

        public static void Set(this IComponentConfig config, string slot, ConfigComponent component)
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
        public static void CopyCompatibleFrom(this IComponentConfig targetConfig, IComponentConfig sourceConfig)
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

        public static List<ConfigComponentEntry> CloneComponents(this IComponentConfig config)
        {
            return config?.Components == null
                ? new List<ConfigComponentEntry>()
                : config.Components.Where(entry => entry != null).Select(entry => entry.Clone()).ToList();
        }
    }

    /// <summary>
    /// An independently addressable child app. This is intentionally not used for ordinary
    /// surfaces; only the tab-container component owns a collection of these instances.
    /// </summary>
    [ProtoContract]
    public sealed class AppInstanceConfig : IComponentConfig
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
    public sealed class SurfaceConfig : IComponentConfig
    {
        [ProtoMember(1)] public int SurfaceIndex { get; set; }
        [ProtoMember(2)] public int AppKind { get; set; }

        [ProtoMember(3)]
        [XmlArrayItem("Component")]
        public List<ConfigComponentEntry> Components { get; set; } = new List<ConfigComponentEntry>();

        public SurfaceConfig Clone()
        {
            return new SurfaceConfig
            {
                SurfaceIndex = SurfaceIndex,
                AppKind = AppKind,
                Components = this.CloneComponents()
            };
        }
    }

    /// <summary>
    /// The only component that owns multiple independently configured app instances. Ordinary
    /// surface scripts store AppKind and Components directly on SurfaceConfig.
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

        public void NormalizeNextAppInstanceId()
        {
            ulong max = 0;
            if (Apps != null)
            {
                foreach (var app in Apps)
                {
                    if (app != null && app.InstanceId > max)
                        max = app.InstanceId;
                }
            }

            if (NextAppInstanceId <= max)
                NextAppInstanceId = max + 1;
            if (NextAppInstanceId == 0)
                NextAppInstanceId = 1;
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
        [ProtoMember(1)] public bool TitleVisible { get; set; } = true;
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
        [ProtoMember(1)] public OptionalValue<Color> HeaderColor { get; set; } = new OptionalValue<Color>();
        [ProtoMember(2)] public OptionalValue<Color> ErrorColor { get; set; } = new OptionalValue<Color>();
        [ProtoMember(3)] public OptionalValue<Color> WarningColor { get; set; } = new OptionalValue<Color>();
        [ProtoMember(4)] public bool CustomizedColors { get; set; }

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
        [ProtoMember(1)] public float CursorScale { get; set; } = 1f;
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
        [ProtoMember(1)] public bool HideEmpty { get; set; } = true;
        [ProtoMember(2)] public int GraphWindowIndex { get; set; } = 2;

        public override ConfigComponent Clone()
        {
            return new PowerConfigComponent { HideEmpty = HideEmpty, GraphWindowIndex = GraphWindowIndex };
        }
    }

    [ProtoContract]
    public sealed class RadarConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public float RangeScale { get; set; } = 1f;
        public override ConfigComponent Clone() => new RadarConfigComponent { RangeScale = RangeScale };
    }

    [ProtoContract]
    public sealed class StarMapConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public float FoV { get; set; } = 70f;
        public override ConfigComponent Clone() => new StarMapConfigComponent { FoV = FoV };
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
        [ProtoMember(3)] public int RaysPerTick { get; set; } = 32;

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
        [ProtoMember(1)] public sbyte XAxisOffset { get; set; }
        [ProtoMember(2)] public sbyte YAxisOffset { get; set; }
        [ProtoMember(3)] public bool EnableAutoAdjust { get; set; } = true;

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
        [ProtoMember(1)] public string RawText { get; set; }
        public override ConfigComponent Clone() => new MarkdownConfigComponent { RawText = RawText };
    }

    [ProtoContract]
    public sealed class ButtonPanelConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public bool HideEmpty { get; set; }
        public override ConfigComponent Clone() => new ButtonPanelConfigComponent { HideEmpty = HideEmpty };
    }

    [ProtoContract]
    public sealed class DigitalPictureFramesConfigComponent : ConfigComponent
    {
        [ProtoMember(1)] public string BackgroundSprite { get; set; } = string.Empty;
        [ProtoMember(2)] public string[] SelectedSprites { get; set; } = Array.Empty<string>();
        [ProtoMember(3)] public float ImageChangeInterval { get; set; }

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
        [ProtoMember(10)] public bool ShowConfigButton { get; set; } = true;
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
        [ProtoMember(1)] public bool Use24HourClock { get; set; } = true;
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
    }
}
