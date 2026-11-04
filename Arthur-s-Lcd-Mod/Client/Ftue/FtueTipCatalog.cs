using System;
using System.Collections.Generic;
using System.IO;
using Acrux.Core;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.IntegrityMonitor;
using LcdMod.Client.Apps.NpcMarket;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.Ftue
{
    internal static class FtueTipCatalog
    {
        public static IEnumerable<FtueTip> CreateTips()
        {
            return new[]
            {
                new LookAroundFtueTip(),
                CreateMarketSearchTip(),
                CreateItemsCraftTip(),
                CreateFarmPlotDetailsTip(),
                CreatePowerFilledDetailsTip(),
                CreateStarMapPlanetDetailsTip(),
                CreateStarMapStaticCameraTip(),
                CreatePlanetaryMapCreateGpsTip(),
                CreatePlanetaryMapCameraTip(),
                CreatePictureFrameCustomTexturesTip(),
                CreateRadarCameraTip(),
                CreateRadarRangeTip(),
                CreatePlanetaryMap3DTip(),
                CreatePlanetaryMapStreetViewTip(),
                CreatePlanetaryMapRoadsTip(),
                CreateGridView3DTip(),
                CreateGridView2DTip(),
                CreateGridXRayTip(),
                CreateGridCameraLockTip(),
                CreateGridLegendTip(),
                CreatePreviewGridSnapshotTip(),
                CreateSelfDiagnosticSnapshotTip(),
                CreateSelfDiagnosticCombatTip(),
                CreateTargetInfoLockTip(),
                CreateTargetInfoMemoryTip(),
                CreateMediaPlayerVideosTip(),
                CreateSensorsOrbitTip(),
                CreateSensorsCamerasTip(),
                CreateSensorsFirstPersonTip()
            };
        }

        static string Loc(string key) => Constants.MOD_PREFIX + "Ftue_" + key;

        static FtueTip CreateMarketSearchTip()
        {
            var tip = CreateAppInputHint<NpcMarketApp>(
                "market.search.macros",
                Loc("MarketSearch_Line1"),
                Loc("MarketSearch_Line2"),
                (surface, app) => new object[]
                {
                    LocHelper.GetLoc("DisplayName_Item_ZoneChip")
                });

            tip.ActivationCondition = (surface, app) => IsPointerOver<MarketSearchHeader>(surface);
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action<string> handler = query =>
                {
                    if (UsesAdvancedMarketSearchSyntax(query))
                        CompleteSafely(surface, complete);
                };

                app.SearchChanged += handler;
                return () => app.SearchChanged -= handler;
            };

            return tip;
        }

        static FtueTip CreateItemsCraftTip()
        {
            var tip = CreateAppInputHint<ItemsApp>(
                "items.open-craft",
                Loc("ItemsCraft_Line1"),
                Loc("ItemsCraft_Line2"));
            tip.ActivationCondition = (surface, app) => app is InventoryApp || app is ProjectorApp;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.CraftOpened += handler;
                return () => app.CraftOpened -= handler;
            };
            return tip;
        }

        static FtueTip CreateFarmPlotDetailsTip()
        {
            var tip = CreateAppInputHint<FarmApp>(
                "farm.plot-details",
                Loc("FarmPlotDetails_Line1"),
                Loc("FarmPlotDetails_Line2"));
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.PlotDetailsOpened += handler;
                return () => app.PlotDetailsOpened -= handler;
            };
            return tip;
        }

        static FtueTip CreatePowerFilledDetailsTip()
        {
            var tip = CreateAppInputHint<PowerFilledApp>(
                "power-filled.entry-details",
                Loc("PowerFilledDetails_Line1"),
                Loc("PowerFilledDetails_Line2"));
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.EntryDetailsOpened += handler;
                return () => app.EntryDetailsOpened -= handler;
            };
            return tip;
        }

        static FtueTip CreateStarMapPlanetDetailsTip()
        {
            var tip = CreateAppInputHint<StarMapApp>(
                "starmap.planet-details",
                Loc("StarMapPlanetDetails_Line1"),
                Loc("StarMapPlanetDetails_Line2"));
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.PlanetDetailsOpened += handler;
                return () => app.PlanetDetailsOpened -= handler;
            };
            return tip;
        }

        static FtueTip CreateStarMapStaticCameraTip()
        {
            var tip = CreateAppInputHint<StarMapApp>(
                "starmap.static-camera",
                Loc("StarMapStaticCamera_Line1"),
                Loc("StarMapStaticCamera_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.StaticCameraOrbitChanged += handler;
                return () => app.StaticCameraOrbitChanged -= handler;
            };
            return tip;
        }

        static FtueTip CreatePlanetaryMapCreateGpsTip()
        {
            var tip = CreateAppInputHint<PlanetaryMapApp>(
                "planetary-map.create-gps",
                Loc("PlanetaryMapCreateGps_Line1"),
                Loc("PlanetaryMapCreateGps_Line2"));
            tip.Placement = HintPlacement.Bottom;
            tip.ActivationCondition = (surface, app) => app.CanCreateSurfaceGps;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.SurfaceGpsCreated += handler;
                return () => app.SurfaceGpsCreated -= handler;
            };
            return tip;
        }

        static FtueTip CreatePlanetaryMapCameraTip()
        {
            var tip = CreateAppInputHint<PlanetaryMapApp>(
                "planetary-map.camera",
                Loc("PlanetaryMapCamera_Line1"),
                Loc("PlanetaryMapCamera_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => app.CanOrbitCamera;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.CameraOrbitChanged += handler;
                return () => app.CameraOrbitChanged -= handler;
            };
            return tip;
        }

        static FtueTip CreatePlanetaryMap3DTip()
        {
            var tip = CreateAppInputHint<PlanetaryMapApp>(
                "planetary-map.view-3d",
                Loc("PlanetaryMap3D_Line1"),
                Loc("PlanetaryMap3D_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => !app.Is3D && app.CanCreateSurfaceGps;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.ProjectionToggled += handler;
                return () => app.ProjectionToggled -= handler;
            };
            return tip;
        }

        static FtueTip CreatePlanetaryMapStreetViewTip()
        {
            var tip = CreateAppInputHint<PlanetaryMapApp>(
                "planetary-map.street-view",
                Loc("PlanetaryMapStreetView_Line1"),
                Loc("PlanetaryMapStreetView_Line2"));
            tip.Placement = HintPlacement.Bottom;
            tip.ActivationCondition = (surface, app) => app.StreetViewAvailable;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.StreetViewEntered += handler;
                return () => app.StreetViewEntered -= handler;
            };
            return tip;
        }

        static FtueTip CreatePlanetaryMapRoadsTip()
        {
            var tip = CreateAppInputHint<PlanetaryMapApp>(
                "planetary-map.roads",
                Loc("PlanetaryMapRoads_Line1"),
                Loc("PlanetaryMapRoads_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => app.RoadsToggleVisible;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.RoadsToggled += handler;
                return () => app.RoadsToggled -= handler;
            };
            return tip;
        }

        // Preview Grid, Self Diagnostic and Target Info share these: one tip teaches all three.
        static FtueTip CreateGridView3DTip()
        {
            var tip = CreateAppInputHint<GridInspectorApp>(
                "grid.view-3d",
                Loc("GridView3D_Line1"),
                Loc("GridView3D_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => !app.StartsIn3D && app.HasMap && !app.Is3D;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.Opened3D += handler;
                return () => app.Opened3D -= handler;
            };
            return tip;
        }

        // Self Diagnostic and Target Info open in 3D, so theirs points the other way, to the flat map.
        static FtueTip CreateGridView2DTip()
        {
            var tip = CreateAppInputHint<GridInspectorApp>(
                "grid.view-2d",
                Loc("GridView2D_Line1"),
                Loc("GridView2D_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => app.StartsIn3D && app.HasMap && app.Is3D;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.Opened2D += handler;
                return () => app.Opened2D -= handler;
            };
            return tip;
        }

        static FtueTip CreateGridXRayTip()
        {
            var tip = CreateAppInputHint<GridInspectorApp>(
                "grid.xray",
                Loc("GridXRay_Line1"),
                Loc("GridXRay_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => app.HasMap && app.Is3D;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.XRayPicked += handler;
                return () => app.XRayPicked -= handler;
            };
            return tip;
        }

        static FtueTip CreateGridCameraLockTip()
        {
            var tip = CreateAppInputHint<GridInspectorApp>(
                "grid.camera-lock",
                Loc("GridCameraLock_Line1"),
                Loc("GridCameraLock_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => app.HasMap && app.Is3D && app.CanOrbit && !app.IsCameraLocked;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.CameraRelocked += handler;
                return () => app.CameraRelocked -= handler;
            };
            return tip;
        }

        static FtueTip CreateGridLegendTip()
        {
            var tip = CreateAppInputHint<GridInspectorApp>(
                "grid.legend",
                Loc("GridLegend_Line1"),
                Loc("GridLegend_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => app.HasLegend && !app.IsInfoOpen;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.InfoOpened += handler;
                return () => app.InfoOpened -= handler;
            };
            return tip;
        }

        static FtueTip CreatePreviewGridSnapshotTip()
        {
            return CreateSnapshotTip<PreviewGridApp>("preview-grid.snapshot", "PreviewGridSnapshot");
        }

        static FtueTip CreateSelfDiagnosticSnapshotTip()
        {
            return CreateSnapshotTip<SelfDiagnosticApp>("self-diagnostic.snapshot", "SelfDiagnosticSnapshot");
        }

        static FtueTip CreateSnapshotTip<TApp>(string id, string key) where TApp : GridInspectorApp
        {
            var tip = CreateAppInputHint<TApp>(id, Loc(key + "_Line1"), Loc(key + "_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => app.ShowsSnapshotButton;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.SnapshotRequested += handler;
                return () => app.SnapshotRequested -= handler;
            };
            return tip;
        }

        // Explains what is on the screen; completed by dismissing it.
        static FtueTip CreateSelfDiagnosticCombatTip()
        {
            var tip = CreateAppInputHint<SelfDiagnosticApp>(
                "self-diagnostic.combat",
                Loc("SelfDiagnosticCombat_Line1"),
                Loc("SelfDiagnosticCombat_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => app.ShowsCombat;
            return tip;
        }

        static FtueTip CreateSensorsOrbitTip()
        {
            var tip = CreateAppInputHint<ExternalSensorsApp>(
                "external-sensors.orbit",
                Loc("SensorsOrbit_Line1"),
                Loc("SensorsOrbit_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => !app.IsFirstPerson;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.OrbitChanged += handler;
                return () => app.OrbitChanged -= handler;
            };
            return tip;
        }

        // Explains what the third-person view shows; completes on dismiss.
        static FtueTip CreateSensorsCamerasTip()
        {
            var tip = CreateAppInputHint<ExternalSensorsApp>(
                "external-sensors.cameras",
                Loc("SensorsCameras_Line1"),
                Loc("SensorsCameras_Line2"));
            tip.Placement = HintPlacement.Bottom;
            tip.ActivationCondition = (surface, app) => !app.IsFirstPerson;
            return tip;
        }

        static FtueTip CreateSensorsFirstPersonTip()
        {
            var tip = CreateAppInputHint<ExternalSensorsApp>(
                "external-sensors.first-person",
                Loc("SensorsFirstPerson_Line1"),
                Loc("SensorsFirstPerson_Line2"));
            tip.Placement = HintPlacement.Bottom;
            tip.ActivationCondition = (surface, app) => !app.IsFirstPerson;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.ViewToggled += handler;
                return () => app.ViewToggled -= handler;
            };
            return tip;
        }

        static FtueTip CreateTargetInfoLockTip()
        {
            var tip = CreateAppInputHint<TargetInfoApp>(
                "target-info.lock",
                Loc("TargetInfoLock_Line1"),
                Loc("TargetInfoLock_Line2"));
            tip.Placement = HintPlacement.Bottom;
            tip.ActivationCondition = (surface, app) => !app.HasTarget && !app.IsShowingMemory;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.TargetAcquired += handler;
                return () => app.TargetAcquired -= handler;
            };
            return tip;
        }

        static FtueTip CreateTargetInfoMemoryTip()
        {
            var tip = CreateAppInputHint<TargetInfoApp>(
                "target-info.memory",
                Loc("TargetInfoMemory_Line1"),
                Loc("TargetInfoMemory_Line2"));
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => app.IsShowingMemory;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.TargetAcquired += handler;
                return () => app.TargetAcquired -= handler;
            };
            return tip;
        }

        static FtueTip CreateMediaPlayerVideosTip()
        {
            var tip = CreateAppInputHint<MediaPlayerApp>(
                "media-player.local-videos",
                Loc("MediaPlayerVideos_Line1"),
                Loc("MediaPlayerVideos_Line2"),
                (surface, app) => new object[] { GetTextureStoragePath() });
            tip.ActivationCondition = (surface, app) => app.PickerOpened;
            return tip;
        }

        static void CompleteSafely(InteractiveSurfaceScript surface, Action complete)
        {
            try
            {
                complete();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, surface);
            }
        }

        static FtueTip CreatePictureFrameCustomTexturesTip()
        {
            var tip = CreateAppInputHint<DigitalPictureFramesApp>(
                "picture-frame.custom-textures",
                Loc("PictureFrameCustomTextures_Line1"),
                Loc("PictureFrameCustomTextures_Line2"),
                (surface, app) => new object[] { GetTextureStoragePath() });
            tip.ActivationCondition = (surface, app) => app.PickerOpened;
            return tip;
        }

        static FtueTip CreateRadarRangeTip()
        {
            var tip = CreateAppInputHint<RadarApp>(
                "radar.ctrl-scroll-range",
                Loc("RadarRange_Line1"),
                Loc("RadarRange_Line2"),
                (surface, app) => new object[]
                {
                    FormatingHelper.DistanceToString(SliderRadarRange.BASE_RANGE_METERS)
                });

            tip.Placement = HintPlacement.Bottom;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action<int> handler = delta =>
                {
                    try
                    {
                        complete();
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, surface);
                    }
                };

                app.RangeScrolled += handler;
                return () => app.RangeScrolled -= handler;
            };

            return tip;
        }

        static FtueTip CreateRadarCameraTip()
        {
            var tip = CreateAppInputHint<RadarApp>(
                "radar.camera-pan-zoom",
                Loc("RadarCamera_Line1"),
                Loc("RadarCamera_Line2"));

            tip.Placement = HintPlacement.Bottom;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action handler = () => CompleteSafely(surface, complete);
                app.CameraMoved += handler;
                return () => app.CameraMoved -= handler;
            };

            return tip;
        }

        static AppInputHintFtueTip<TApp> CreateAppInputHint<TApp>(
            string id,
            string line1LocalizationKey,
            string line2LocalizationKey,
            Func<InteractiveSurfaceScript, TApp, object[]> parametersFactory = null)
            where TApp : class, IApp
        {
            return new AppInputHintFtueTip<TApp>(
                id,
                (surface, app) =>
                {
                    var parameters = parametersFactory == null
                        ? Array.Empty<object>()
                        : parametersFactory(surface, app) ?? Array.Empty<object>();

                    return BuildLocalizedMarkdown(
                        line1LocalizationKey,
                        line2LocalizationKey,
                        parameters);
                });
        }

        static bool IsPointerOver<TElement>(InteractiveSurfaceScript surface)
            where TElement : VisualElement
        {
            var root = surface.RetainedAppGuiHost == null ? null : surface.RetainedAppGuiHost.Root;
            return root != null && FindPointerOver<TElement>(root);
        }

        static bool FindPointerOver<TElement>(VisualElement element)
            where TElement : VisualElement
        {
            if (!element.IsPointerOver)
                return false;
            if (element is TElement)
                return true;
            foreach (var child in element.VisualChildren)
                if (FindPointerOver<TElement>(child))
                    return true;
            return false;
        }

        static string BuildLocalizedMarkdown(
            string line1LocalizationKey,
            string line2LocalizationKey,
            object[] parameters)
        {
            string line1 = FormatLocalized(line1LocalizationKey, parameters);
            if (string.IsNullOrWhiteSpace(line2LocalizationKey))
                return line1;

            string line2 = FormatLocalized(line2LocalizationKey, parameters);
            return string.IsNullOrWhiteSpace(line2)
                ? line1
                : line1 + "\n-# " + line2;
        }

        static string FormatLocalized(string localizationKey, object[] parameters)
        {
            string value = LocHelper.GetLoc(localizationKey);
            return parameters == null || parameters.Length == 0
                ? value
                : string.Format(FormatingHelper.Culture, value, EscapeMarkdownParameters(parameters));
        }

        static object[] EscapeMarkdownParameters(object[] parameters)
        {
            var escaped = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                string text = parameters[i] as string;
                escaped[i] = text == null ? parameters[i] : EscapeMarkdownText(text);
            }

            return escaped;
        }

        static string EscapeMarkdownText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("`", "\\`")
                .Replace("*", "\\*")
                .Replace("_", "\\_")
                .Replace("~", "\\~")
                .Replace("[", "\\[")
                .Replace("]", "\\]");
        }

        static bool UsesAdvancedMarketSearchSyntax(string query)
        {
            return !string.IsNullOrEmpty(query) &&
                   (query.IndexOf('#') >= 0 || query.IndexOf(',') >= 0);
        }

        static string GetTextureStoragePath()
        {
            var utilities = MyAPIGateway.Utilities;
            if (utilities.GamePaths == null)
                return "%AppData%\\SpaceEngineers\\Storage";

            return Path.Combine(
                    utilities.GamePaths.UserDataPath,
                    "Storage",
                    utilities.GamePaths.ModScopeName)
                .Replace('\\', '/');
        }
    }
}
