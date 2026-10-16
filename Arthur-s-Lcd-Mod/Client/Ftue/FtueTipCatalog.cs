using System;
using System.Collections.Generic;
using System.IO;
using Adk.Gui.Core;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
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
                CreateRadarRangeTip()
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
