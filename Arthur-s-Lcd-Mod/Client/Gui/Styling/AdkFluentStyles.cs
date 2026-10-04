using System;
using System.Collections.Generic;
using System.IO;
using Adk.Gui.Styling;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Client.Gui.Styling
{
    internal static class AdkFluentStyles
    {
        static readonly TimeSpan UnknownSizeRetry = TimeSpan.FromSeconds(5);
        static readonly Dictionary<string, Vector2> TextureSizes = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, DateTime> UnknownTextureSizes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        internal static void ConfigureSourceLoader()
        {
            StyleSheet.SourceLoader = LoadSource;
            StyleSheet.SourceLoadFailed = ReportStyleLoadFailure;
            Adk.Gui.Lcd.LcdTextureResolver.IsTextureDefined = IsTextureDefined;
            Adk.Gui.Lcd.LcdTextureResolver.TextureSize = GetTextureSize;
#if DEBUG
            Adk.Gui.Markup.HotReload.Log = ReportHotReload;
#endif
        }

        internal static void Reset()
        {
            Adk.Gui.Markup.HotReload.Reset();
            Adk.Gui.Markup.HotReload.Log = null;
            Adk.Gui.Lcd.LcdTextureResolver.IsTextureDefined = null;
            Adk.Gui.Lcd.LcdTextureResolver.TextureSize = null;
            TextureSizes.Clear();
            UnknownTextureSizes.Clear();
            LcdMod.Client.Gui.UserControls.BSoDQrCode.Release();
            Adk.Gui.Controls.Chrome.CaptionGlyphs.Release();
            Adk.Gui.Controls.WindowPlatforms.CaptionButtons = Adk.Gui.Controls.CaptionButtonsPlacement.Right;
            StyleSheet.SourceLoader = null;
            StyleSheet.SourceLoadFailed = null;
        }

        static void ReportStyleLoadFailure(string source, Exception error)
        {
            LcdMod.Common.Helpers.LogHelper.Log(VRage.Utils.MyLogSeverity.Error,
                "Skipped style source '" + source + "': " + error.GetType().Name + ": " + error.Message);
        }

#if DEBUG
        static void ReportHotReload(string message)
        {
            LcdMod.Common.Helpers.LogHelper.Log(VRage.Utils.MyLogSeverity.Info, message);
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.ShowMessage("lcdMod", message);
        }
#endif

        static bool IsTextureDefined(string name)
        {
            return Sandbox.Definitions.MyDefinitionManager.Static != null &&
                   Sandbox.Definitions.MyDefinitionManager.Static.GetDefinition<Sandbox.Definitions.MyLCDTextureDefinition>(
                       VRage.Utils.MyStringHash.GetOrCompute(name)) != null;
        }

        static Vector2 GetTextureSize(string name)
        {
            Vector2 size;
            if (TextureSizes.TryGetValue(name, out size))
                return size;
            DateTime now = DateTime.UtcNow;
            DateTime retry;
            if (UnknownTextureSizes.TryGetValue(name, out retry) && now < retry)
                return Vector2.Zero;
            Vector2I pixels;
            if (!LcdTextureSizeHelper.TryGetTextureSize(name, out pixels) || pixels.X <= 0 || pixels.Y <= 0)
            {
                UnknownTextureSizes[name] = now + UnknownSizeRetry;
                return Vector2.Zero;
            }
            UnknownTextureSizes.Remove(name);
            return TextureSizes[name] = new Vector2(pixels.X, pixels.Y);
        }

        static string LoadSource(string source)
        {
            if (MyAPIGateway.Utilities == null)
                throw new InvalidOperationException("The ADK stylesheet loader requires MyAPIGateway.Utilities.");
            using (TextReader reader = MyAPIGateway.Utilities.ReadFileInModLocation(
                       source,
                       LcdModSessionComponent.CurrentModItem))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
