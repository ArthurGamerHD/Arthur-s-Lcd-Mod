using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Common.Config;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Config.Models.Apps;

namespace LcdMod.Common.Config.Components
{
    /// <summary>
    /// Temporary bridge used while apps still consume the pre-component config classes.
    /// Component storage remains the source of truth; runtime objects are materialized views.
    /// </summary>
    public static class ComponentConfigAdapter
    {
        static readonly IConfigGenerator ConfigGenerator = new ConfigGenerator();

        public static SurfaceConfig FromRuntimeSurface(ScreenConfigGeneral source, int surfaceIndex)
        {
            var surface = new SurfaceConfig { SurfaceIndex = surfaceIndex };
            CaptureRuntime(source, surface);
            return surface;
        }

        public static AppInstanceConfig FromRuntimeApp(ScreenConfigGeneral source, ulong instanceId)
        {
            var app = new AppInstanceConfig { InstanceId = instanceId };
            CaptureRuntime(source, app);
            return app;
        }

        public static void CaptureRuntime(ScreenConfigGeneral source, IComponentConfig app)
        {
            if (source == null)
                source = new ScreenConfigGeneral();
            if (app == null)
                throw new ArgumentNullException(nameof(app));

            app.AppKind = source.Id;
            app.Components = new List<ConfigComponentEntry>();

            app.Set(ConfigSlots.General, new GeneralConfigComponent
            {
                TitleVisible = source.TitleVisible,
                InternalScale = source.InternalScale,
                DrawLines = source.DrawLines,
                DisplayMode = source.DisplayMode,
                BackgroundAlpha = Copy(source.BackgroundAlpha),
                CustomData = Copy(source.CustomData)
            });

            var colorable = source as ScreenConfigColorable;
            if (colorable != null)
            {
                app.Set(ConfigSlots.Colors, new ColorConfigComponent
                {
                    HeaderColor = Copy(colorable.HeaderColorInternal),
                    ErrorColor = Copy(colorable.ErrorColorInternal),
                    WarningColor = Copy(colorable.WarningColorInternal),
                    CustomizedColors = colorable.CustomizedColors
                });
            }

            var interactive = source as ScreenConfigInteractive;
            if (interactive != null)
            {
                app.Set(ConfigSlots.Interaction, new InteractiveConfigComponent
                {
                    CursorScale = interactive.CursorScale,
                    RequiresAlt = interactive.RequiresAlt,
                    ReferenceMode = interactive.ReferenceMode,
                    AutoScrollStep = interactive.AutoScrollStep
                });
            }

            var filters = source as ScreenConfigWithFilters;
            if (filters != null)
            {
                app.Set(ConfigSlots.Filters, new FilterConfigComponent
                {
                    SortMethod = filters.SortMethod,
                    HideEmpty = filters.HideEmpty
                });
            }

            var blocks = source as ScreenConfigWithBlocks;
            if (blocks != null)
            {
                app.Set(ConfigSlots.Blocks, new BlockSelectionConfigComponent
                {
                    SelectedBlocks = Copy(blocks.SelectedBlocks),
                    SelectedGroups = Copy(blocks.SelectedGroups),
                    GridLinkTypeInternal = blocks.GridLinkTypeInternal,
                    SortFilterKeys = Copy(blocks.SortFilterKeys),
                    SortFilterCategories = Copy(blocks.SortFilterCategories)
                });
            }

            var items = source as ScreenConfigWithItems;
            if (items != null)
            {
                app.Set(ConfigSlots.Items, new ItemSelectionConfigComponent
                {
                    SelectedDefinition = Copy(items.SelectedDefinition),
                    SelectedCategories = Copy(items.SelectedCategories)
                });
            }

            AddReferenceComponent(app, source);
            AddAppComponent(app, source);
        }

        public static ScreenConfigGeneral ToRuntime(IComponentConfig source, int screenIndex)
        {
            if (source == null)
                return new ScreenConfigGeneral { ScreenIndex = screenIndex };

            var target = ConfigGenerator.GenerateConfig((ConfigKind)source.AppKind) as ScreenConfigGeneral
                         ?? new ScreenConfigGeneral();
            target.ScreenIndex = screenIndex;

            var general = source.TryGet<GeneralConfigComponent>(ConfigSlots.General);
            if (general != null)
            {
                target.TitleVisible = general.TitleVisible;
                target.InternalScale = general.InternalScale;
                target.DrawLines = general.DrawLines;
                target.DisplayMode = general.DisplayMode;
                target.BackgroundAlpha = Copy(general.BackgroundAlpha);
                target.CustomData = Copy(general.CustomData);
            }

            var colorable = target as ScreenConfigColorable;
            var colors = source.TryGet<ColorConfigComponent>(ConfigSlots.Colors);
            if (colorable != null && colors != null)
            {
                colorable.HeaderColorInternal = Copy(colors.HeaderColor);
                colorable.ErrorColorInternal = Copy(colors.ErrorColor);
                colorable.WarningColorInternal = Copy(colors.WarningColor);
                colorable.CustomizedColors = colors.CustomizedColors;
            }

            var interactive = target as ScreenConfigInteractive;
            var interaction = source.TryGet<InteractiveConfigComponent>(ConfigSlots.Interaction);
            if (interactive != null && interaction != null)
            {
                interactive.CursorScale = interaction.CursorScale;
                interactive.RequiresAlt = interaction.RequiresAlt;
                interactive.ReferenceMode = interaction.ReferenceMode;
                interactive.AutoScrollStep = interaction.AutoScrollStep;
            }

            var filters = target as ScreenConfigWithFilters;
            var filterComponent = source.TryGet<FilterConfigComponent>(ConfigSlots.Filters);
            if (filters != null && filterComponent != null)
            {
                filters.SortMethod = filterComponent.SortMethod;
                filters.HideEmpty = filterComponent.HideEmpty;
            }

            var blocks = target as ScreenConfigWithBlocks;
            var blockComponent = source.TryGet<BlockSelectionConfigComponent>(ConfigSlots.Blocks);
            if (blocks != null && blockComponent != null)
            {
                blocks.SelectedBlocks = Copy(blockComponent.SelectedBlocks);
                blocks.SelectedGroups = Copy(blockComponent.SelectedGroups);
                blocks.GridLinkTypeInternal = blockComponent.GridLinkTypeInternal;
                blocks.SortFilterKeys = Copy(blockComponent.SortFilterKeys);
                blocks.SortFilterCategories = Copy(blockComponent.SortFilterCategories);
            }

            var items = target as ScreenConfigWithItems;
            var itemComponent = source.TryGet<ItemSelectionConfigComponent>(ConfigSlots.Items);
            if (items != null && itemComponent != null)
            {
                items.SelectedDefinition = Copy(itemComponent.SelectedDefinition);
                items.SelectedCategories = Copy(itemComponent.SelectedCategories);
            }

            ApplyReferenceComponent(source, target);
            ApplyAppComponent(source, target);
            return target;
        }

        static void AddReferenceComponent(IComponentConfig app, ScreenConfigGeneral source)
        {
            var projector = source as ScreenConfigProjector;
            if (projector != null)
            {
                app.Set(ConfigSlots.ProjectorReference, new BlockReferenceConfigComponent { EntityId = projector.ReferenceBlock });
                return;
            }

            var diagnostic = source as ScreenConfigDiagnostic;
            if (diagnostic != null)
            {
                app.Set(ConfigSlots.ProjectorReference, new BlockReferenceConfigComponent { EntityId = diagnostic.ReferenceBlock });
                return;
            }

            var docking = source as ScreenConfigDocking;
            if (docking != null)
            {
                app.Set(ConfigSlots.DockableReference, new BlockReferenceConfigComponent { EntityId = docking.ReferenceBlock });
                return;
            }

            var proxy = source as ScreenConfigRenderProxy;
            if (proxy != null)
            {
                app.Set(ConfigSlots.RenderProxyReference, new BlockReferenceConfigComponent { EntityId = proxy.ReferenceBlock });
                return;
            }

            var oreScanner = source as ScreenConfigOreScanner;
            if (oreScanner != null)
            {
                app.Set(ConfigSlots.OreScannerReference, new BlockReferenceConfigComponent { EntityId = oreScanner.ReferenceBlock });
                return;
            }

#if DEBUG
            var visibleTree = source as ScreenConfigVisibleTreeDebug;
            if (visibleTree != null)
            {
                app.Set(ConfigSlots.VisibleTreeReference, new BlockReferenceConfigComponent { EntityId = visibleTree.ReferenceBlock });
                return;
            }
#endif

            var genericReference = source as ScreenConfigWithReferenceBlock;
            if (genericReference != null)
                app.Set(ConfigSlots.OreScannerReference, new BlockReferenceConfigComponent { EntityId = genericReference.ReferenceBlock });
        }

        static void ApplyReferenceComponent(IComponentConfig source, ScreenConfigGeneral target)
        {
            var projector = target as ScreenConfigProjector;
            if (projector != null)
            {
                projector.ReferenceBlock = GetReference(source, ConfigSlots.ProjectorReference);
                return;
            }

            var diagnostic = target as ScreenConfigDiagnostic;
            if (diagnostic != null)
            {
                diagnostic.ReferenceBlock = GetReference(source, ConfigSlots.ProjectorReference);
                return;
            }

            var docking = target as ScreenConfigDocking;
            if (docking != null)
            {
                docking.ReferenceBlock = GetReference(source, ConfigSlots.DockableReference);
                return;
            }

            var proxy = target as ScreenConfigRenderProxy;
            if (proxy != null)
            {
                proxy.ReferenceBlock = GetReference(source, ConfigSlots.RenderProxyReference);
                return;
            }

            var oreScanner = target as ScreenConfigOreScanner;
            if (oreScanner != null)
            {
                oreScanner.ReferenceBlock = GetReference(source, ConfigSlots.OreScannerReference);
                return;
            }

#if DEBUG
            var visibleTree = target as ScreenConfigVisibleTreeDebug;
            if (visibleTree != null)
            {
                visibleTree.ReferenceBlock = GetReference(source, ConfigSlots.VisibleTreeReference);
                return;
            }
#endif

            var genericReference = target as ScreenConfigWithReferenceBlock;
            if (genericReference != null)
                genericReference.ReferenceBlock = GetReference(source, ConfigSlots.OreScannerReference);
        }

        static long GetReference(IComponentConfig app, string slot)
        {
            var component = app.TryGet<BlockReferenceConfigComponent>(slot);
            return component == null ? 0 : component.EntityId;
        }

        static void AddAppComponent(IComponentConfig app, ScreenConfigGeneral source)
        {
            var power = source as ScreenConfigPower;
            if (power != null)
            {
                app.Set(ConfigSlots.App, new PowerConfigComponent
                {
                    HideEmpty = power.HideEmpty,
                    GraphWindowIndex = power.GraphWindowIndex
                });
                return;
            }

            var radar = source as ScreenConfigRadar;
            if (radar != null)
            {
                app.Set(ConfigSlots.App, new RadarConfigComponent { RangeScale = radar.RangeScale });
                return;
            }

            var starMap = source as ScreenConfigStarMap;
            if (starMap != null)
            {
                app.Set(ConfigSlots.App, new StarMapConfigComponent { FoV = starMap.FoV });
                return;
            }

            var diagnostic = source as ScreenConfigDiagnostic;
            if (diagnostic != null)
            {
                app.Set(ConfigSlots.App, new DiagnosticConfigComponent { Rotation = diagnostic.Rotation });
                return;
            }

            var raycast = source as ScreenConfigRaycast;
            if (raycast != null)
            {
                app.Set(ConfigSlots.App, new RaycastConfigComponent
                {
                    RelationOverlay = raycast.RelationOverlay,
                    RenderScale = raycast.RenderScale,
                    RaysPerTick = raycast.RaysPerTick
                });
                return;
            }

            var proxy = source as ScreenConfigRenderProxy;
            if (proxy != null)
            {
                app.Set(ConfigSlots.App, new RenderProxyConfigComponent
                {
                    XAxisOffset = proxy.XAxisOffset,
                    YAxisOffset = proxy.YAxisOffset,
                    EnableAutoAdjust = proxy.EnableAutoAdjust
                });
                return;
            }

            var markdown = source as ScreenConfigMarkdown;
            if (markdown != null)
            {
                app.Set(ConfigSlots.App, new MarkdownConfigComponent { RawText = markdown.RawText });
                return;
            }

            var buttonPanel = source as ScreenConfigButtonPanel;
            if (buttonPanel != null)
            {
                app.Set(ConfigSlots.App, new ButtonPanelConfigComponent { HideEmpty = buttonPanel.HideEmpty });
                return;
            }

            var pictureFrames = source as ScreenConfigDigitalPictureFrames;
            if (pictureFrames != null)
            {
                app.Set(ConfigSlots.App, new DigitalPictureFramesConfigComponent
                {
                    BackgroundSprite = pictureFrames.BackgroundSprite,
                    SelectedSprites = Copy(pictureFrames.SelectedSprites),
                    ImageChangeInterval = pictureFrames.ImageChangeInterval
                });
                return;
            }

            var cargoActions = source as ScreenConfigCargoActions;
            if (cargoActions != null)
            {
                app.Set(ConfigSlots.App, new CargoActionsConfigComponent
                {
                    SortMode = cargoActions.SortMode,
                    UraniumLargeGridSmallReactor = cargoActions.UraniumLargeGridSmallReactor,
                    UraniumLargeGridLargeReactor = cargoActions.UraniumLargeGridLargeReactor,
                    UraniumSmallGridSmallReactor = cargoActions.UraniumSmallGridSmallReactor,
                    UraniumSmallGridLargeReactor = cargoActions.UraniumSmallGridLargeReactor,
                    AmmoDefaultPerWeapon = cargoActions.AmmoDefaultPerWeapon,
                    WeaponOverrideKeys = Copy(cargoActions.WeaponOverrideKeys),
                    WeaponOverrideCounts = Copy(cargoActions.WeaponOverrideCounts),
                    SettingsRevision = cargoActions.SettingsRevision,
                    ShowConfigButton = cargoActions.ShowConfigButton,
                    GridLinkTypeInternal = cargoActions.GridLinkTypeInternal
                });
                return;
            }

            var npcMarket = source as ScreenConfigNpcMarket;
            if (npcMarket != null)
            {
                app.Set(ConfigSlots.App, new NpcMarketConfigComponent
                {
                    SelectedMode = npcMarket.SelectedMode,
                    ScrollOffsetPixels = npcMarket.ScrollOffsetPixels,
                    BuySortColumn = npcMarket.BuySortColumn,
                    BuySortDescending = npcMarket.BuySortDescending,
                    SellSortColumn = npcMarket.SellSortColumn,
                    SellSortDescending = npcMarket.SellSortDescending,
                    BothSortColumn = npcMarket.BothSortColumn,
                    BothSortDescending = npcMarket.BothSortDescending,
                    HorizontalScrollOffsetPixels = npcMarket.HorizontalScrollOffsetPixels,
                    VerticalScrollOffsetPixels = npcMarket.VerticalScrollOffsetPixels,
                    MaxDistanceMeters = npcMarket.MaxDistanceMeters,
                    PageSwitchSeconds = npcMarket.PageSwitchSeconds,
                    SearchQuery = npcMarket.SearchQuery
                });
                return;
            }

            var clock = source as ScreenConfigClockDashboard;
            if (clock != null)
            {
                app.Set(ConfigSlots.App, new ClockDashboardConfigComponent
                {
                    Use24HourClock = clock.Use24HourClock,
                    TemperatureModeInternal = clock.TemperatureModeInternal
                });
                return;
            }

#if DEBUG
            var visibleTree = source as ScreenConfigVisibleTreeDebug;
            if (visibleTree != null)
                app.Set(ConfigSlots.App, new VisibleTreeDebugConfigComponent { ReferenceScreenIndex = visibleTree.ReferenceScreenIndex });
#endif
        }

        static void ApplyAppComponent(IComponentConfig source, ScreenConfigGeneral target)
        {
            var power = target as ScreenConfigPower;
            var powerComponent = source.TryGet<PowerConfigComponent>(ConfigSlots.App);
            if (power != null && powerComponent != null)
            {
                power.HideEmpty = powerComponent.HideEmpty;
                power.GraphWindowIndex = powerComponent.GraphWindowIndex;
                return;
            }

            var radar = target as ScreenConfigRadar;
            var radarComponent = source.TryGet<RadarConfigComponent>(ConfigSlots.App);
            if (radar != null && radarComponent != null)
            {
                radar.RangeScale = radarComponent.RangeScale;
                return;
            }

            var starMap = target as ScreenConfigStarMap;
            var starMapComponent = source.TryGet<StarMapConfigComponent>(ConfigSlots.App);
            if (starMap != null && starMapComponent != null)
            {
                starMap.FoV = starMapComponent.FoV;
                return;
            }

            var diagnostic = target as ScreenConfigDiagnostic;
            var diagnosticComponent = source.TryGet<DiagnosticConfigComponent>(ConfigSlots.App);
            if (diagnostic != null && diagnosticComponent != null)
            {
                diagnostic.Rotation = diagnosticComponent.Rotation;
                return;
            }

            var raycast = target as ScreenConfigRaycast;
            var raycastComponent = source.TryGet<RaycastConfigComponent>(ConfigSlots.App);
            if (raycast != null && raycastComponent != null)
            {
                raycast.RelationOverlay = raycastComponent.RelationOverlay;
                raycast.RenderScale = raycastComponent.RenderScale;
                raycast.RaysPerTick = raycastComponent.RaysPerTick;
                return;
            }

            var proxy = target as ScreenConfigRenderProxy;
            var proxyComponent = source.TryGet<RenderProxyConfigComponent>(ConfigSlots.App);
            if (proxy != null && proxyComponent != null)
            {
                proxy.XAxisOffset = proxyComponent.XAxisOffset;
                proxy.YAxisOffset = proxyComponent.YAxisOffset;
                proxy.EnableAutoAdjust = proxyComponent.EnableAutoAdjust;
                return;
            }

            var markdown = target as ScreenConfigMarkdown;
            var markdownComponent = source.TryGet<MarkdownConfigComponent>(ConfigSlots.App);
            if (markdown != null && markdownComponent != null)
            {
                markdown.RawText = markdownComponent.RawText;
                return;
            }

            var buttonPanel = target as ScreenConfigButtonPanel;
            var buttonComponent = source.TryGet<ButtonPanelConfigComponent>(ConfigSlots.App);
            if (buttonPanel != null && buttonComponent != null)
            {
                buttonPanel.HideEmpty = buttonComponent.HideEmpty;
                return;
            }

            var pictureFrames = target as ScreenConfigDigitalPictureFrames;
            var pictureComponent = source.TryGet<DigitalPictureFramesConfigComponent>(ConfigSlots.App);
            if (pictureFrames != null && pictureComponent != null)
            {
                pictureFrames.BackgroundSprite = pictureComponent.BackgroundSprite;
                pictureFrames.SelectedSprites = Copy(pictureComponent.SelectedSprites);
                pictureFrames.ImageChangeInterval = pictureComponent.ImageChangeInterval;
                return;
            }

            var cargoActions = target as ScreenConfigCargoActions;
            var cargoComponent = source.TryGet<CargoActionsConfigComponent>(ConfigSlots.App);
            if (cargoActions != null && cargoComponent != null)
            {
                cargoActions.SortMode = cargoComponent.SortMode;
                cargoActions.UraniumLargeGridSmallReactor = cargoComponent.UraniumLargeGridSmallReactor;
                cargoActions.UraniumLargeGridLargeReactor = cargoComponent.UraniumLargeGridLargeReactor;
                cargoActions.UraniumSmallGridSmallReactor = cargoComponent.UraniumSmallGridSmallReactor;
                cargoActions.UraniumSmallGridLargeReactor = cargoComponent.UraniumSmallGridLargeReactor;
                cargoActions.AmmoDefaultPerWeapon = cargoComponent.AmmoDefaultPerWeapon;
                cargoActions.WeaponOverrideKeys = Copy(cargoComponent.WeaponOverrideKeys);
                cargoActions.WeaponOverrideCounts = Copy(cargoComponent.WeaponOverrideCounts);
                cargoActions.SettingsRevision = cargoComponent.SettingsRevision;
                cargoActions.ShowConfigButton = cargoComponent.ShowConfigButton;
                cargoActions.GridLinkTypeInternal = cargoComponent.GridLinkTypeInternal;
                return;
            }

            var npcMarket = target as ScreenConfigNpcMarket;
            var marketComponent = source.TryGet<NpcMarketConfigComponent>(ConfigSlots.App);
            if (npcMarket != null && marketComponent != null)
            {
                npcMarket.SelectedMode = marketComponent.SelectedMode;
                npcMarket.ScrollOffsetPixels = marketComponent.ScrollOffsetPixels;
                npcMarket.BuySortColumn = marketComponent.BuySortColumn;
                npcMarket.BuySortDescending = marketComponent.BuySortDescending;
                npcMarket.SellSortColumn = marketComponent.SellSortColumn;
                npcMarket.SellSortDescending = marketComponent.SellSortDescending;
                npcMarket.BothSortColumn = marketComponent.BothSortColumn;
                npcMarket.BothSortDescending = marketComponent.BothSortDescending;
                npcMarket.HorizontalScrollOffsetPixels = marketComponent.HorizontalScrollOffsetPixels;
                npcMarket.VerticalScrollOffsetPixels = marketComponent.VerticalScrollOffsetPixels;
                npcMarket.MaxDistanceMeters = marketComponent.MaxDistanceMeters;
                npcMarket.PageSwitchSeconds = marketComponent.PageSwitchSeconds;
                npcMarket.SearchQuery = marketComponent.SearchQuery;
                return;
            }

            var clock = target as ScreenConfigClockDashboard;
            var clockComponent = source.TryGet<ClockDashboardConfigComponent>(ConfigSlots.App);
            if (clock != null && clockComponent != null)
            {
                clock.Use24HourClock = clockComponent.Use24HourClock;
                clock.TemperatureModeInternal = clockComponent.TemperatureModeInternal;
                return;
            }

#if DEBUG
            var visibleTree = target as ScreenConfigVisibleTreeDebug;
            var visibleTreeComponent = source.TryGet<VisibleTreeDebugConfigComponent>(ConfigSlots.App);
            if (visibleTree != null && visibleTreeComponent != null)
                visibleTree.ReferenceScreenIndex = visibleTreeComponent.ReferenceScreenIndex;
#endif
        }

        static OptionalValue<T> Copy<T>(OptionalValue<T> value)
        {
            return value == null
                ? new OptionalValue<T>()
                : new OptionalValue<T> { HasValue = value.HasValue, Value = value.Value };
        }

        static T[] Copy<T>(T[] values)
        {
            return values == null || values.Length == 0 ? Array.Empty<T>() : (T[])values.Clone();
        }

        static Dictionary<string, byte[]> Copy(Dictionary<string, byte[]> values)
        {
            var copy = new Dictionary<string, byte[]>();
            if (values == null)
                return copy;

            foreach (var pair in values)
            {
                if (pair.Key != null)
                    copy[pair.Key] = pair.Value == null ? null : (byte[])pair.Value.Clone();
            }
            return copy;
        }
    }
}
