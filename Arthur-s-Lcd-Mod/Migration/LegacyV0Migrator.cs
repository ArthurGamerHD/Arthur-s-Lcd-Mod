using System;
using System.Collections.Generic;
using LcdMod.Common.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using LcdMod.Migration.Legacy.V0;
using VRage.Game.ModAPI;

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

            return result;
        }

        static SurfaceConfig MigrateSurface(LegacyScreenConfigGeneral source)
        {
            var app = new SurfaceConfig
            {
                SurfaceIndex = source.ScreenIndex,
                LegacyAppKind = GetAppKind(source),
                AppTypeId = 0,
                Components = new List<ConfigComponentEntry>()
            };

            app.Set(Constants.GENERAL, new GeneralConfigComponent
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
                app.Set(Constants.COLORS, new ColorConfigComponent
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
                app.Set(Constants.INTERACTION, new InteractiveConfigComponent
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
                app.Set(Constants.FILTERS, new FilterConfigComponent
                {
                    SortMethod = filters.SortMethod,
                    HideEmpty = filters.HideEmpty
                });
            }

            var blocks = source as LegacyScreenConfigWithBlocks;
            if (blocks != null)
            {
                app.Set(Constants.BLOCKS, new BlockSelectionConfigComponent
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
                app.Set(Constants.ITEMS, new ItemSelectionConfigComponent
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
            if (source is LegacyScreenConfigProjector) return (int)LegacyConfigKind.Projector;
            if (source is LegacyScreenConfigDiagnostic) return (int)LegacyConfigKind.Diagnostic;
            if (source is LegacyScreenConfigOreScanner) return (int)LegacyConfigKind.OreScanner;
            if (source is LegacyScreenConfigRadar) return (int)LegacyConfigKind.Radar;
            if (source is LegacyScreenConfigPower) return (int)LegacyConfigKind.Power;
            if (source is LegacyScreenConfigStarMap) return (int)LegacyConfigKind.StarMap;
            if (source is LegacyScreenConfigDocking) return (int)LegacyConfigKind.Docking;
            if (source is LegacyScreenConfigRaycast) return (int)LegacyConfigKind.Raycast;
            if (source is LegacyScreenConfigRenderProxy) return (int)LegacyConfigKind.RenderProxy;
            if (source is LegacyScreenConfigMarkdown) return (int)LegacyConfigKind.Markdown;
            if (source is LegacyScreenConfigButtonPanel) return (int)LegacyConfigKind.ButtonPanel;
            if (source is LegacyScreenConfigDigitalPictureFrames) return (int)LegacyConfigKind.DigitalPictureFrames;
            if (source is LegacyScreenConfigCargoActions) return (int)LegacyConfigKind.CargoActions;
            if (source is LegacyScreenConfigNpcMarket) return (int)LegacyConfigKind.NpcMarket;
            if (source is LegacyScreenConfigVisibleTreeDebug) return (int)LegacyConfigKind.VisibleTreeDebug;
            if (source is LegacyScreenConfigClockDashboard) return (int)LegacyConfigKind.ClockDashboard;
            if (source.GetType() == typeof(LegacyScreenConfigWithItems)) return (int)LegacyConfigKind.WithItems;
            if (source.GetType() == typeof(LegacyScreenConfigWithBlocks)) return (int)LegacyConfigKind.WithBlocks;
            if (source.GetType() == typeof(LegacyScreenConfigWithFilters)) return (int)LegacyConfigKind.WithFilters;
            if (source.GetType() == typeof(LegacyScreenConfigWithReferenceBlock)) return (int)LegacyConfigKind.WithReferenceBlock;
            if (source.GetType() == typeof(LegacyScreenConfigInteractive)) return (int)LegacyConfigKind.Interactive;
            if (source.GetType() == typeof(LegacyScreenConfigColorable)) return (int)LegacyConfigKind.Colorable;
            return (int)LegacyConfigKind.General;
        }

        static void AddReference(IComponentContainer app, LegacyScreenConfigGeneral source)
        {
            var projector = source as LegacyScreenConfigProjector;
            if (projector != null)
            {
                app.Set(Constants.PROJECTOR_REFERENCE, new BlockReferenceConfigComponent { EntityId = projector.ReferenceBlock });
                return;
            }

            var diagnostic = source as LegacyScreenConfigDiagnostic;
            if (diagnostic != null)
            {
                app.Set(Constants.PROJECTOR_REFERENCE, new BlockReferenceConfigComponent { EntityId = diagnostic.ReferenceBlock });
                return;
            }

            var docking = source as LegacyScreenConfigDocking;
            if (docking != null)
            {
                app.Set(Constants.DOCKABLE_REFERENCE, new BlockReferenceConfigComponent { EntityId = docking.ReferenceBlock });
                return;
            }

            var proxy = source as LegacyScreenConfigRenderProxy;
            if (proxy != null)
            {
                app.Set(Constants.RENDER_PROXY_REFERENCE, new BlockReferenceConfigComponent { EntityId = proxy.ReferenceBlock });
                return;
            }

            var visibleTree = source as LegacyScreenConfigVisibleTreeDebug;
            if (visibleTree != null)
            {
                app.Set(Constants.VISIBLE_TREE_REFERENCE, new BlockReferenceConfigComponent { EntityId = visibleTree.ReferenceBlock });
                return;
            }
        }

        static void AddAppSettings(IComponentContainer app, LegacyScreenConfigGeneral source)
        {
            var power = source as LegacyScreenConfigPower;
            if (power != null)
            {
                app.Set(Constants.APP, new PowerConfigComponent
                {
                    HideEmpty = power.HideEmpty,
                    GraphWindowIndex = power.GraphWindowIndex,
                    PowerHistoryTier = -1,
                    GridLinkTypeInternal = (int)GridLinkTypeEnum.Mechanical
                });
                return;
            }

            var radar = source as LegacyScreenConfigRadar;
            if (radar != null)
            {
                app.Set(Constants.APP, new RadarConfigComponent { RangeScale = radar.RangeScale });
                return;
            }

            var starMap = source as LegacyScreenConfigStarMap;
            if (starMap != null)
            {
                app.Set(Constants.APP, new StarMapConfigComponent { FoV = starMap.FoV });
                return;
            }

            var diagnostic = source as LegacyScreenConfigDiagnostic;
            if (diagnostic != null)
            {
                app.Set(Constants.APP, new DiagnosticConfigComponent { Rotation = diagnostic.Rotation });
                return;
            }

            var raycast = source as LegacyScreenConfigRaycast;
            if (raycast != null)
            {
                app.Set(Constants.APP, new RaycastConfigComponent
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
                app.Set(Constants.APP, new RenderProxyConfigComponent
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
                app.Set(Constants.APP, new MarkdownConfigComponent { RawText = markdown.RawText });
                return;
            }

            var buttonPanel = source as LegacyScreenConfigButtonPanel;
            if (buttonPanel != null)
            {
                app.Set(Constants.APP, new ButtonPanelConfigComponent { HideEmpty = buttonPanel.HideEmpty });
                return;
            }

            var pictureFrames = source as LegacyScreenConfigDigitalPictureFrames;
            if (pictureFrames != null)
            {
                app.Set(Constants.APP, new DigitalPictureFramesConfigComponent
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
                app.Set(Constants.APP, new CargoActionsConfigComponent
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
                app.Set(Constants.APP, new NpcMarketConfigComponent
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
                app.Set(Constants.APP, new ClockDashboardConfigComponent
                {
                    Use24HourClock = clock.Use24HourClock,
                    TemperatureModeInternal = clock.TemperatureModeInternal
                });
                return;
            }

            var visibleTree = source as LegacyScreenConfigVisibleTreeDebug;
            if (visibleTree != null)
                app.Set(Constants.APP, new VisibleTreeDebugConfigComponent { ReferenceScreenIndex = visibleTree.ReferenceScreenIndex });
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
