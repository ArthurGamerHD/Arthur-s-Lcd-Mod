using System;
using System.Collections.Generic;
using LcdMod.Common.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Models;
using LcdMod.Migration.Legacy.V0;

namespace LcdMod.Migration
{
    public static class LegacyV0Migrator
    {
        public static ScreenProviderConfig Migrate(LegacyScreenProviderConfig legacy)
        {
            if (legacy == null)
                throw new ArgumentNullException(nameof(legacy));

            var result = new ScreenProviderConfig
            {
                SchemaVersion = ScreenProviderConfig.COMPONENT_SCHEMA_VERSION,
                Parent = legacy.Parent,
                Surfaces = new List<SurfaceConfig>()
            };

            if (legacy.Screens != null)
            {
                for (var index = 0; index < legacy.Screens.Count; index++)
                {
                    var screen = legacy.Screens[index] ?? new LegacyScreenConfigGeneral { ScreenIndex = index };
                    result.Surfaces.Add(MigrateSurface(screen));
                }
            }

            result.EnsureRuntimeScreens();
            return result;
        }

        static SurfaceConfig MigrateSurface(LegacyScreenConfigGeneral source)
        {
            var app = new SurfaceConfig
            {
                SurfaceIndex = source.ScreenIndex,
                AppKind = GetAppKind(source),
                Components = new List<ConfigComponentEntry>()
            };

            app.Set(ConfigSlots.General, new GeneralConfigComponent
            {
                TitleVisible = source.TitleVisible,
                InternalScale = source.InternalScale,
                DrawLines = source.DrawLines,
                DisplayMode = source.DisplayMode,
                BackgroundAlpha = Copy(source.BackgroundAlpha),
                CustomData = Copy(source.CustomData)
            });

            var colorable = source as LegacyScreenConfigColorable;
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

            var interactive = source as LegacyScreenConfigInteractive;
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

            var filters = source as LegacyScreenConfigWithFilters;
            if (filters != null)
            {
                app.Set(ConfigSlots.Filters, new FilterConfigComponent
                {
                    SortMethod = filters.SortMethod,
                    HideEmpty = filters.HideEmpty
                });
            }

            var blocks = source as LegacyScreenConfigWithBlocks;
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

            var items = source as LegacyScreenConfigWithItems;
            if (items != null)
            {
                app.Set(ConfigSlots.Items, new ItemSelectionConfigComponent
                {
                    SelectedDefinition = Copy(items.SelectedDefinition),
                    SelectedCategories = Copy(items.SelectedCategories)
                });
            }

            AddReference(app, source);
            AddAppSettings(app, source);
            return app;
        }

        static int GetAppKind(LegacyScreenConfigGeneral source)
        {
            if (source is LegacyScreenConfigProjector) return 2;
            if (source is LegacyScreenConfigDiagnostic) return 3;
            if (source is LegacyScreenConfigOreScanner) return 9;
            if (source is LegacyScreenConfigRadar) return 8;
            if (source is LegacyScreenConfigPower) return 10;
            if (source is LegacyScreenConfigStarMap) return 11;
            if (source is LegacyScreenConfigDocking) return 13;
            if (source is LegacyScreenConfigRaycast) return 15;
            if (source is LegacyScreenConfigRenderProxy) return 16;
            if (source is LegacyScreenConfigMarkdown) return 17;
            if (source is LegacyScreenConfigButtonPanel) return 18;
            if (source is LegacyScreenConfigDigitalPictureFrames) return 19;
            if (source is LegacyScreenConfigCargoActions) return 20;
            if (source is LegacyScreenConfigNpcMarket) return 21;
            if (source is LegacyScreenConfigVisibleTreeDebug) return 22;
            if (source is LegacyScreenConfigClockDashboard) return 23;
            if (source.GetType() == typeof(LegacyScreenConfigWithItems)) return 12;
            if (source.GetType() == typeof(LegacyScreenConfigWithBlocks)) return 1;
            if (source.GetType() == typeof(LegacyScreenConfigWithFilters)) return 7;
            if (source.GetType() == typeof(LegacyScreenConfigWithReferenceBlock)) return 6;
            if (source.GetType() == typeof(LegacyScreenConfigInteractive)) return 14;
            if (source.GetType() == typeof(LegacyScreenConfigColorable)) return 5;
            return 4;
        }

        static void AddReference(IComponentConfig app, LegacyScreenConfigGeneral source)
        {
            var projector = source as LegacyScreenConfigProjector;
            if (projector != null)
            {
                app.Set(ConfigSlots.ProjectorReference, new BlockReferenceConfigComponent { EntityId = projector.ReferenceBlock });
                return;
            }

            var diagnostic = source as LegacyScreenConfigDiagnostic;
            if (diagnostic != null)
            {
                app.Set(ConfigSlots.ProjectorReference, new BlockReferenceConfigComponent { EntityId = diagnostic.ReferenceBlock });
                return;
            }

            var docking = source as LegacyScreenConfigDocking;
            if (docking != null)
            {
                app.Set(ConfigSlots.DockableReference, new BlockReferenceConfigComponent { EntityId = docking.ReferenceBlock });
                return;
            }

            var proxy = source as LegacyScreenConfigRenderProxy;
            if (proxy != null)
            {
                app.Set(ConfigSlots.RenderProxyReference, new BlockReferenceConfigComponent { EntityId = proxy.ReferenceBlock });
                return;
            }

            var visibleTree = source as LegacyScreenConfigVisibleTreeDebug;
            if (visibleTree != null)
            {
                app.Set(ConfigSlots.VisibleTreeReference, new BlockReferenceConfigComponent { EntityId = visibleTree.ReferenceBlock });
                return;
            }

            var reference = source as LegacyScreenConfigWithReferenceBlock;
            if (reference != null)
                app.Set(ConfigSlots.OreScannerReference, new BlockReferenceConfigComponent { EntityId = reference.ReferenceBlock });
        }

        static void AddAppSettings(IComponentConfig app, LegacyScreenConfigGeneral source)
        {
            var power = source as LegacyScreenConfigPower;
            if (power != null)
            {
                app.Set(ConfigSlots.App, new PowerConfigComponent
                {
                    HideEmpty = power.HideEmpty,
                    GraphWindowIndex = power.GraphWindowIndex
                });
                return;
            }

            var radar = source as LegacyScreenConfigRadar;
            if (radar != null)
            {
                app.Set(ConfigSlots.App, new RadarConfigComponent { RangeScale = radar.RangeScale });
                return;
            }

            var starMap = source as LegacyScreenConfigStarMap;
            if (starMap != null)
            {
                app.Set(ConfigSlots.App, new StarMapConfigComponent { FoV = starMap.FoV });
                return;
            }

            var diagnostic = source as LegacyScreenConfigDiagnostic;
            if (diagnostic != null)
            {
                app.Set(ConfigSlots.App, new DiagnosticConfigComponent { Rotation = diagnostic.Rotation });
                return;
            }

            var raycast = source as LegacyScreenConfigRaycast;
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

            var proxy = source as LegacyScreenConfigRenderProxy;
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

            var markdown = source as LegacyScreenConfigMarkdown;
            if (markdown != null)
            {
                app.Set(ConfigSlots.App, new MarkdownConfigComponent { RawText = markdown.RawText });
                return;
            }

            var buttonPanel = source as LegacyScreenConfigButtonPanel;
            if (buttonPanel != null)
            {
                app.Set(ConfigSlots.App, new ButtonPanelConfigComponent { HideEmpty = buttonPanel.HideEmpty });
                return;
            }

            var pictureFrames = source as LegacyScreenConfigDigitalPictureFrames;
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

            var cargoActions = source as LegacyScreenConfigCargoActions;
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

            var npcMarket = source as LegacyScreenConfigNpcMarket;
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

            var clock = source as LegacyScreenConfigClockDashboard;
            if (clock != null)
            {
                app.Set(ConfigSlots.App, new ClockDashboardConfigComponent
                {
                    Use24HourClock = clock.Use24HourClock,
                    TemperatureModeInternal = clock.TemperatureModeInternal
                });
                return;
            }

            var visibleTree = source as LegacyScreenConfigVisibleTreeDebug;
            if (visibleTree != null)
                app.Set(ConfigSlots.App, new VisibleTreeDebugConfigComponent { ReferenceScreenIndex = visibleTree.ReferenceScreenIndex });
        }

        static OptionalValue<T> Copy<T>(LegacyOptionalValue<T> value)
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
