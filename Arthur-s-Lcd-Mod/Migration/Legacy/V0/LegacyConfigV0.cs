using System;
using System.Collections.Generic;
using System.ComponentModel;
using ProtoBuf;
using VRageMath;

namespace LcdMod.Migration.Legacy.V0
{
    // Frozen data-only snapshot of the config schema stored under Constants.V0StorageGuid.
    // Do not add runtime methods, generator interfaces, XML projections, or conversions here.

    internal enum LegacyConfigKind
    {
        WithBlocks = 1,
        Projector = 2,
        Diagnostic = 3,
        General = 4,
        Colorable = 5,
        WithReferenceBlock = 6,
        WithFilters = 7,
        Radar = 8,
        OreScanner = 9,
        Power = 10,
        StarMap = 11,
        WithItems = 12,
        Docking = 13,
        Interactive = 14,
        Raycast = 15,
        RenderProxy = 16,
        Markdown = 17,
        ButtonPanel = 18,
        DigitalPictureFrames = 19,
        CargoActions = 20,
        NpcMarket = 21,
        VisibleTreeDebug = 22,
        ClockDashboard = 23,
    }
    
    [ProtoContract]
    public sealed class LegacyScreenProviderConfig
    {
        [ProtoMember(1)] public List<LegacyScreenConfigGeneral> Screens { get; set; }
        [ProtoMember(2)] public long Parent { get; set; }
    }

    [ProtoContract]
    public sealed class LegacyOptionalValue<T>
    {
        [ProtoMember(1)] public T Value { get; set; }
        [ProtoMember(2)] public bool HasValue { get; set; }
    }

    [ProtoContract]
    [ProtoInclude(101, typeof(LegacyScreenConfigColorable))]
    public class LegacyScreenConfigGeneral
    {
        [ProtoMember(1)] public int ScreenIndex { get; set; }
        [ProtoMember(11)] public bool TitleVisible { get; set; } = true;
        [ProtoMember(7)] public float InternalScale { get; set; } = 1f;
        [ProtoMember(9)] public bool DrawLines { get; set; }
        [ProtoMember(12)] public int DisplayMode { get; set; }
        [ProtoMember(52)] public LegacyOptionalValue<byte> BackgroundAlpha { get; set; } = new LegacyOptionalValue<byte>();
        [ProtoMember(99)] public Dictionary<string, byte[]> CustomData { get; set; } = new Dictionary<string, byte[]>();
    }

    [ProtoContract]
    [ProtoInclude(112, typeof(LegacyScreenConfigInteractive))]
    public class LegacyScreenConfigColorable : LegacyScreenConfigGeneral
    {
        [ProtoMember(2)] public LegacyOptionalValue<Color> HeaderColorInternal { get; set; } = new LegacyOptionalValue<Color>();
        [ProtoMember(14)] public LegacyOptionalValue<Color> ErrorColorInternal { get; set; } = new LegacyOptionalValue<Color>();
        [ProtoMember(15)] public LegacyOptionalValue<Color> WarningColorInternal { get; set; } = new LegacyOptionalValue<Color>();
        [ProtoMember(17)] public bool CustomizedColors { get; set; }
    }

    [ProtoContract]
    [ProtoInclude(102, typeof(LegacyScreenConfigWithReferenceBlock))]
    [ProtoInclude(103, typeof(LegacyScreenConfigWithFilters))]
    [ProtoInclude(105, typeof(LegacyScreenConfigRadar))]
    [ProtoInclude(107, typeof(LegacyScreenConfigPower))]
    [ProtoInclude(108, typeof(LegacyScreenConfigStarMap))]
    [ProtoInclude(110, typeof(LegacyScreenConfigDiagnostic))]
    [ProtoInclude(111, typeof(LegacyScreenConfigDocking))]
    [ProtoInclude(113, typeof(LegacyScreenConfigRaycast))]
    [ProtoInclude(114, typeof(LegacyScreenConfigRenderProxy))]
    [ProtoInclude(115, typeof(LegacyScreenConfigMarkdown))]
    [ProtoInclude(116, typeof(LegacyScreenConfigButtonPanel))]
    [ProtoInclude(117, typeof(LegacyScreenConfigDigitalPictureFrames))]
    [ProtoInclude(118, typeof(LegacyScreenConfigCargoActions))]
    [ProtoInclude(119, typeof(LegacyScreenConfigNpcMarket))]
    [ProtoInclude(120, typeof(LegacyScreenConfigVisibleTreeDebug))]
    [ProtoInclude(121, typeof(LegacyScreenConfigClockDashboard))]
    public class LegacyScreenConfigInteractive : LegacyScreenConfigColorable
    {
        [ProtoMember(22)] public float CursorScale { get; set; } = 1f;
        [ProtoMember(23)] public bool RequiresAlt { get; set; } = true;
        [ProtoMember(27)] public int ReferenceMode { get; set; }
        [ProtoMember(98)] public float AutoScrollStep { get; set; } = 2f;
    }

    [ProtoContract]
    [ProtoInclude(100, typeof(LegacyScreenConfigWithBlocks))]
    public class LegacyScreenConfigWithFilters : LegacyScreenConfigInteractive
    {
        [ProtoMember(10)] public int SortMethod { get; set; }
        [ProtoMember(13)] public bool HideEmpty { get; set; } = true;
    }

    [ProtoContract]
    [ProtoInclude(104, typeof(LegacyScreenConfigWithItems))]
    public class LegacyScreenConfigWithBlocks : LegacyScreenConfigWithFilters
    {
        [ProtoMember(3)] public long[] SelectedBlocks { get; set; } = Array.Empty<long>();
        [ProtoMember(4)] public string[] SelectedGroups { get; set; } = Array.Empty<string>();
        [ProtoMember(20)] public int GridLinkTypeInternal { get; set; } = 1;
        [ProtoMember(21)] public string[] SortFilterKeys { get; set; } = Array.Empty<string>();
        [ProtoMember(22)] public string[] SortFilterCategories { get; set; } = Array.Empty<string>();
    }

    [ProtoContract]
    [ProtoInclude(109, typeof(LegacyScreenConfigProjector))]
    public class LegacyScreenConfigWithItems : LegacyScreenConfigWithBlocks
    {
        [ProtoMember(5)] public string[] SelectedDefinition { get; set; } = Array.Empty<string>();
        [ProtoMember(6)] public string[] SelectedCategories { get; set; } = Array.Empty<string>();
    }

    [ProtoContract]
    [ProtoInclude(106, typeof(LegacyScreenConfigOreScanner))]
    public class LegacyScreenConfigWithReferenceBlock : LegacyScreenConfigInteractive
    {
        [ProtoMember(8)] public virtual long ReferenceBlock { get; set; }
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigProjector : LegacyScreenConfigWithItems
    {
        [ProtoMember(8)] public long ReferenceBlock { get; set; }
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigOreScanner : LegacyScreenConfigWithReferenceBlock
    {
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigRadar : LegacyScreenConfigInteractive
    {
        [ProtoMember(19)] public float RangeScale { get; set; } = 1f;
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigPower : LegacyScreenConfigInteractive
    {
        [ProtoMember(13)] public bool HideEmpty { get; set; } = true;
        [ProtoMember(18)] public int GraphWindowIndex { get; set; } = 2;
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigStarMap : LegacyScreenConfigInteractive
    {
        [ProtoMember(19)] public float FoV { get; set; } = 70f;
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigDiagnostic : LegacyScreenConfigInteractive
    {
        [ProtoMember(8)] public long ReferenceBlock { get; set; }
        [ProtoMember(16)] public float Rotation { get; set; }
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigDocking : LegacyScreenConfigInteractive
    {
        [ProtoMember(8)] public long ReferenceBlock { get; set; }
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigRaycast : LegacyScreenConfigInteractive
    {
        [ProtoMember(24)] public int RelationOverlay { get; set; } = 1;
        [ProtoMember(25)] public float RenderScale { get; set; } = .2f;
        [ProtoMember(26)] public int RaysPerTick { get; set; } = 32;
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigRenderProxy : LegacyScreenConfigInteractive
    {
        [ProtoMember(8)] public long ReferenceBlock { get; set; }
        [ProtoMember(16)] public sbyte XAxisOffset { get; set; }
        [ProtoMember(17)] public sbyte YAxisOffset { get; set; }
        [ProtoMember(18)] public bool EnableAutoAdjust { get; set; } = true;
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigMarkdown : LegacyScreenConfigInteractive
    {
        [ProtoMember(24)]
        public string RawText { get; set; } = string.Empty;
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigButtonPanel : LegacyScreenConfigInteractive
    {
        [ProtoMember(1001)] public bool HideEmpty { get; set; }
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigDigitalPictureFrames : LegacyScreenConfigInteractive
    {
        [ProtoMember(30)] public string BackgroundSprite { get; set; } = string.Empty;
        [ProtoMember(31)] public string[] SelectedSprites { get; set; } = Array.Empty<string>();
        [ProtoMember(32)] public float ImageChangeInterval { get; set; }
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigCargoActions : LegacyScreenConfigInteractive
    {
        [ProtoMember(28)] public int SortMode { get; set; }
        [ProtoMember(29), DefaultValue(4)] public int UraniumLargeGridSmallReactor { get; set; } = 4;
        [ProtoMember(30), DefaultValue(10)] public int UraniumLargeGridLargeReactor { get; set; } = 10;
        [ProtoMember(31), DefaultValue(1)] public int UraniumSmallGridSmallReactor { get; set; } = 1;
        [ProtoMember(32), DefaultValue(5)] public int UraniumSmallGridLargeReactor { get; set; } = 5;
        [ProtoMember(33), DefaultValue(10)] public int AmmoDefaultPerWeapon { get; set; } = 10;
        [ProtoMember(34)] public string[] WeaponOverrideKeys { get; set; } = Array.Empty<string>();
        [ProtoMember(35)] public int[] WeaponOverrideCounts { get; set; } = Array.Empty<int>();
        [ProtoMember(36)] public int SettingsRevision { get; set; }
        [ProtoMember(37), DefaultValue(true)] public bool ShowConfigButton { get; set; } = true;
        [ProtoMember(38), DefaultValue(1)] public int GridLinkTypeInternal { get; set; } = 1;
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigNpcMarket : LegacyScreenConfigInteractive
    {
        [ProtoMember(39)] public int SelectedMode { get; set; }
        [ProtoMember(40)] public float ScrollOffsetPixels { get; set; }
        [ProtoMember(41)] public int BuySortColumn { get; set; } = 1;
        [ProtoMember(42)] public bool BuySortDescending { get; set; }
        [ProtoMember(43)] public int SellSortColumn { get; set; } = 1;
        [ProtoMember(44)] public bool SellSortDescending { get; set; } = true;
        [ProtoMember(45)] public int BothSortColumn { get; set; }
        [ProtoMember(46)] public bool BothSortDescending { get; set; }
        [ProtoMember(47)] public float HorizontalScrollOffsetPixels { get; set; }
        [ProtoMember(48)] public float VerticalScrollOffsetPixels { get; set; }
        [ProtoMember(49)] public float MaxDistanceMeters { get; set; } = 10000001f;
        [ProtoMember(50)] public float PageSwitchSeconds { get; set; } = 5f;
        [ProtoMember(51)] public string SearchQuery { get; set; } = string.Empty;
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigVisibleTreeDebug : LegacyScreenConfigInteractive
    {
        [ProtoMember(8)] public long ReferenceBlock { get; set; }
        [ProtoMember(28)] public int ReferenceScreenIndex { get; set; }
    }

    [ProtoContract]
    public sealed class LegacyScreenConfigClockDashboard : LegacyScreenConfigInteractive
    {
        [ProtoMember(39)] public bool Use24HourClock { get; set; } = true;
        [ProtoMember(40)] public int TemperatureModeInternal { get; set; }
    }
}
