using System;
using Acrux.Diagnostics;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using VRage.Collections;
using VRage.Game.GUI.TextPanel;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Gui.UserControls
{
    /// <summary>
    /// The LCD mod's crash screen: Acrux UI draws it; this class supplies the mod's localized text,
    /// support link and QR code.
    /// </summary>
    class BSoD
    {
        const int QrModules = 52;

        // The support QR code (GitHub link) as rectangles in modules: center x, center y, width, height,
        // light (1) or dark (0), relative to the code center.
        static readonly int[] QrRectangles =
        {
            0, 0, 52, 52, 1, 0, 0, 50, 50, 0, 18, -18, 10, 10, 1, -18, -18, 10, 10, 1, -18, 18, 10, 10, 1, 18, -18, 6, 6, 0,
            -18, -18, 6, 6, 0, -18, 18, 6, 6, 0, 17, -10, 16, 2, 1, -17, -10, 16, 2, 1, -17, 10, 16, 2, 1, 10, -17, 2, 16, 1,
            -10, -17, 2, 16, 1, -4, 14, 2, 2, 1, -10, 17, 2, 16, 1, -6, 17, 2, 4, 1, -3, 21, 4, 8, 1, 7, 20, 4, 2, 1,
            1, -9, 4, 4, 1, 1, -3, 4, 4, 1, 22, -8, 2, 2, 1, -8, 22, 2, 2, 1, 24, -7, 2, 4, 1, 24, 10, 2, 2, 1,
            -2, 5, 2, 8, 1, 9, -2, 4, 2, 1, 14, 1, 2, 4, 1, 24, -1, 2, 4, 1, 18, 0, 2, 2, 1, -8, 12, 2, 2, 1,
            19, -4, 4, 6, 1, 16, -6, 2, 2, 1, 12, -6, 2, 2, 1, 10, -7, 2, 4, 1, 6, -11, 2, 4, 1, 6, -20, 2, 2, 1,
            8, -18, 2, 2, 1, 0, 20, 2, 2, 1, 4, -16, 6, 2, 1, 4, -14, 2, 2, 1, 2, -12, 2, 2, 1, -2, -12, 2, 2, 1,
            0, -14, 2, 2, 1, 4, -4, 2, 2, 1, 2, -6, 2, 2, 1, 2, 24, 2, 2, 1, 10, 4, 2, 2, 1, 6, 2, 6, 2, 1,
            3, 0, 8, 2, 1, 5, 6, 4, 2, 1, 23, 2, 4, 2, 1, 5, 22, 4, 2, 1, 4, 12, 6, 2, 1, 18, 4, 6, 6, 1,
            12, 12, 6, 6, 1, 18, 4, 2, 2, 0, 12, 12, 2, 2, 0, 2, 16, 6, 2, 1, 20, 22, 6, 2, 1, 14, 24, 6, 2, 1,
            -20, 1, 2, 8, 1, -24, -3, 2, 8, 1, -16, 4, 2, 6, 1, -8, 5, 2, 4, 1, -12, 6, 2, 2, 1, -10, 8, 2, 2, 1,
            -14, 8, 2, 2, 1, -18, 7, 2, 4, 1, -22, 6, 2, 6, 1, 18, 18, 2, 2, 1, 14, 18, 2, 2, 1, 14, 22, 2, 2, 1,
            16, 20, 2, 2, 1, 12, 20, 2, 2, 1, 8, 18, 2, 2, 1, 8, 24, 2, 2, 1, -12, -6, 2, 2, 1, 19, 11, 4, 4, 1,
            23, 8, 4, 2, 1, 21, 14, 4, 2, 1, -17, -6, 4, 2, 1, -9, -8, 4, 2, 1, 1, 14, 4, 2, 1, -15, 2, 8, 2, 1,
            -14, -2, 6, 2, 1, -4, -23, 2, 4, 1, -8, -23, 2, 4, 1, -6, -15, 2, 8, 1, -6, -4, 2, 6, 1, -8, -4, 2, 2, 1,
            -2, -5, 2, 4, 1, -4, -9, 2, 4, 1, 0, 4, 2, 2, 1, 2, 8, 2, 2, 1, -6, 8, 2, 2, 1, -8, 0, 2, 2, 1,
            0, -19, 2, 4, 1, 0, 10, 14, 2, 1, 3, -21, 4, 4, 1, -3, -17, 4, 4, 1, -2, -22, 2, 2, 1, -10, 4, 2, 2, 1,
            -14, 4, 2, 2, 1, -14, -4, 2, 2, 1, -18, -4, 2, 2, 1, -22, -2, 2, 2, 1, 6, 14, 2, 2, 1, 22, 20, 2, 2, 1,
            -22, -8, 2, 2, 1
        };

        readonly CrashScreen _screen;

        BSoD(SurfaceScriptBase app, Exception exception)
        {
            var report = new CrashReport("Arthur's LCD Mod")
            {
                Header = GetLocalizedText(MOD_PREFIX + "BSoD_Header", BSOD_TITLE_FALLBACK),
                Info = GetLocalizedText(MOD_PREFIX + "BSoD_InfoIntro", BSOD_INFO1_FALLBACK) + " " +
                       GetLocalizedText(MOD_PREFIX + "BSoD_InfoUrl", BSOD_INFO2_FALLBACK)+ " " + GITHUB + " " +
                       GetLocalizedText(MOD_PREFIX + "BSoD_InfoQr", BSOD_INFO3_FALLBACK),
                SupportUrl = GITHUB,
                SupportIntro = GetLocalizedText(MOD_PREFIX + "BSoD_SupportIntro", BSOD_INFO4_FALLBACK),
                ExceptionLabel = GetLocalizedText(MOD_PREFIX + "BSoD_ExceptionCode", BSOD_INFO5_FALLBACK),
                QrRectangles = QrRectangles,
                QrModules = QrModules,
            };
            _screen = CrashScreen.Show(app.Surface, app.ViewBox, app.Animations, report, exception);
            Frame = new ListReader<MySprite>(_screen.Frame);
        }

        public ListReader<MySprite> Frame { get; }

        // The frame shows the QR texture created while drawing it; re-upload it once that texture is live.
        public bool NeedsReupload => _screen.NeedsReupload;

        static string GetLocalizedText(string loc, string fallback)
        {
            try
            {
                string localized = LocHelper.GetLoc(loc);
                if (localized != null && !string.Equals(localized, loc, StringComparison.Ordinal))
                    return localized;
            }
            catch
            {
                // Intentionally ignored: BSoD must be able to render even if localization breaks.
            }

            return fallback;
        }

        public static BSoD ShowBSoD(SurfaceScriptBase surfaceScriptBase, Exception exception) =>
            new BSoD(surfaceScriptBase, exception);
    }
}
