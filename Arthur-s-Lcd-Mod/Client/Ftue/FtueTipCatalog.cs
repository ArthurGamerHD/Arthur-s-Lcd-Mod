using System;
using System.Collections.Generic;
using System.IO;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Custom.Planet;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.Ftue
{
    internal static class FtueTipCatalog
    {
        const string FARM_PLOT_DETAILS_CLASS = "FarmPlotDetails";
        const string POWER_FILLED_DETAILS_CLASS = "PowerFilledDetails";
        const string PICTURE_FRAME_PICK_TEXTURE_CLASS = "PictureFramePickTexture";

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
                CreateRadarRangeTip()
            };
        }

        static string Loc(string key) => Constants.MOD_PREFIX + "Ftue_" + key;

        static FtueTip CreateMarketSearchTip()
        {
            var tip = CreateControlHint<NpcMarketApp>(
                "market.search.macros",
                Loc("MarketSearch_Line1"),
                Loc("MarketSearch_Line2"),
                (surface, app, control) => control.HasStyleClass("Search"),
                (surface, app, control) => new object[]
                {
                    LocHelper.GetLoc("DisplayName_Item_ZoneChip")
                });

            tip.CompleteOnPrimaryClick = false;
            tip.CompletionBinder = (surface, app, complete) =>
            {
                Action<string> handler = query =>
                {
                    if (!UsesAdvancedMarketSearchSyntax(query))
                        return;

                    try
                    {
                        complete();
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, surface);
                    }
                };

                app.SearchChanged += handler;
                return () => app.SearchChanged -= handler;
            };

            return tip;
        }

        static FtueTip CreateItemsCraftTip()
        {
            return CreateControlHint<ItemsApp>(
                "items.open-craft",
                Loc("ItemsCraft_Line1"),
                Loc("ItemsCraft_Line2"),
                (surface, app, control) =>
                    (app is InventoryApp || app is ProjectorApp) &&
                    control.DataContext is ItemsApp.ItemViewModel);
        }

        static FtueTip CreateFarmPlotDetailsTip()
        {
            return CreateAppInputHint<FarmApp>(
                "farm.plot-details",
                Loc("FarmPlotDetails_Line1"),
                Loc("FarmPlotDetails_Line2"),
                (surface, app, control) => control.HasStyleClass(FARM_PLOT_DETAILS_CLASS));
        }

        static FtueTip CreatePowerFilledDetailsTip()
        {
            return CreateAppInputHint<PowerFilledApp>(
                "power-filled.entry-details",
                Loc("PowerFilledDetails_Line1"),
                Loc("PowerFilledDetails_Line2"),
                (surface, app, control) => control.HasStyleClass(POWER_FILLED_DETAILS_CLASS));
        }

        static FtueTip CreateStarMapPlanetDetailsTip()
        {
            var tip = CreateAppInputHint<StarMapApp>(
                "starmap.planet-details",
                Loc("StarMapPlanetDetails_Line1"),
                Loc("StarMapPlanetDetails_Line2"),
                (surface, app, control) =>
                    control is InteractiveCircleEntry || control is PlanetGlobeControl);
            return tip;
        }

        static FtueTip CreateStarMapStaticCameraTip()
        {
            var tip = CreateAppInputHint<StarMapApp>(
                "starmap.static-camera",
                Loc("StarMapStaticCamera_Line1"),
                Loc("StarMapStaticCamera_Line2"),
                null);
            tip.Placement = HintPlacement.Top;
            tip.ActivationCondition = (surface, app) => app.PlanetariumMode;
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
                Loc("PlanetaryMapCreateGps_Line2"),
                null);
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
                Loc("PlanetaryMapCamera_Line2"),
                null);
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
            var tip = CreateControlHint<DigitalPictureFramesApp>(
                "picture-frame.custom-textures",
                Loc("PictureFrameCustomTextures_Line1"),
                Loc("PictureFrameCustomTextures_Line2"),
                (surface, app, control) => control.HasStyleClass(PICTURE_FRAME_PICK_TEXTURE_CLASS),
                (surface, app, control) => new object[] { GetTextureStoragePath() });

            tip.Trigger = ControlHintTrigger.PrimaryClick;
            tip.CompleteOnPrimaryClick = false;
            return tip;
        }

        static FtueTip CreateRadarRangeTip()
        {
            var tip = CreateAppInputHint<RadarApp>(
                "radar.scroll-range",
                Loc("RadarRange_Line1"),
                Loc("RadarRange_Line2"),
                null,
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

        static AppInputHintFtueTip<TApp> CreateAppInputHint<TApp>(
            string id,
            string line1LocalizationKey,
            string line2LocalizationKey,
            Func<InteractiveSurfaceScript, TApp, ControlTemplate, bool> completionCondition,
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
                },
                completionCondition);
        }

        static AppControlHintFtueTip<TApp> CreateControlHint<TApp>(
            string id,
            string line1LocalizationKey,
            string line2LocalizationKey,
            Func<InteractiveSurfaceScript, TApp, ControlTemplate, bool> condition,
            Func<InteractiveSurfaceScript, TApp, ControlTemplate, object[]> parametersFactory = null)
            where TApp : class, IApp
        {
            return new AppControlHintFtueTip<TApp>(
                id,
                condition,
                (surface, app, control) =>
                {
                    var parameters = parametersFactory == null
                        ? Array.Empty<object>()
                        : parametersFactory(surface, app, control) ?? Array.Empty<object>();

                    return BuildLocalizedMarkdown(
                        line1LocalizationKey,
                        line2LocalizationKey,
                        parameters);
                });
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
            if (utilities?.GamePaths == null)
                return "%AppData%\\SpaceEngineers\\Storage";

            return Path.Combine(
                    utilities.GamePaths.UserDataPath,
                    "Storage",
                    utilities.GamePaths.ModScopeName)
                .Replace('\\', '/');
        }
    }
}
