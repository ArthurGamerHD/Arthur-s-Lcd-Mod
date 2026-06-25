using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Models;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Terminal
{
    /// <summary>
    /// Client-session clipboard for copying compatible settings between LCD Mod surfaces.
    /// The clipboard is intentionally not serialized or synchronized.
    /// </summary>
    public sealed class SettingsClipboard
    {
        SurfaceConfig _copiedSettings;
        TextSurfaceSettings _copiedSurfaceSettings;

        public bool HasSettings => _copiedSettings != null;

        public bool Copy(IMyTerminalBlock block)
        {
            var config = GetConfig(block);
            if (config == null)
                return false;

            var surfaceIndex = ConfigManager.GetThisSurfaceIndex(block);
            var textSurface = GetTextSurface(block, surfaceIndex);
            if (textSurface == null)
                return false;

            var surface = config.GetSurfaceConfig(surfaceIndex);
            if (surface == null)
                return false;

            _copiedSettings = surface.Clone();
            _copiedSurfaceSettings = TextSurfaceSettings.Capture(textSurface);
            return true;
        }

        public bool Paste(IMyTerminalBlock block)
        {
            if (_copiedSettings == null)
                return false;

            var config = GetConfig(block);
            if (config == null)
                return false;

            var surfaceIndex = ConfigManager.GetThisSurfaceIndex(block);
            var textSurface = GetTextSurface(block, surfaceIndex);
            if (textSurface == null)
                return false;

            if (surfaceIndex < 0)
                return false;

            var target = config.GetSurfaceConfig(surfaceIndex);
            if (!config.CanWriteConfig(target))
                return false;

            // Keep the target AppType identity. Only exact matching slots and component types are copied.
            target.CopyCompatibleFrom(_copiedSettings);

            ConfigManager.Sync(block, config);

            // These properties belong to the Space Engineers text surface rather than the LCD Mod
            // config. Apply them after config synchronization so the copied display settings remain
            // the final local values without changing Script or ContentType.
            _copiedSurfaceSettings?.Apply(textSurface);
            return true;
        }

        public void Clear()
        {
            _copiedSettings = null;
            _copiedSurfaceSettings = null;
        }

        static ScreenProviderConfig GetConfig(IMyTerminalBlock block)
        {
            if (block == null)
                return null;

            return ConfigManager.GetConfigForBlock(block) ?? ConfigManager.TryLoad(block);
        }

        static IMyTextSurface GetTextSurface(IMyTerminalBlock block, int surfaceIndex)
        {
            var provider = block as IMyTextSurfaceProvider;
            if (provider == null || surfaceIndex < 0 || surfaceIndex >= provider.SurfaceCount)
                return null;

            return provider.GetSurface(surfaceIndex) as IMyTextSurface;
        }

        sealed class TextSurfaceSettings
        {
            public Color ScriptForegroundColor;
            public Color ScriptBackgroundColor;
            public string Font;
            public float FontSize;
            public float TextPadding;
            public TextAlignment Alignment;
            public byte BackgroundAlpha;

            public static TextSurfaceSettings Capture(IMyTextSurface surface)
            {
                return new TextSurfaceSettings
                {
                    ScriptForegroundColor = surface.ScriptForegroundColor,
                    ScriptBackgroundColor = surface.ScriptBackgroundColor,
                    Font = surface.Font,
                    FontSize = surface.FontSize,
                    TextPadding = surface.TextPadding,
                    Alignment = surface.Alignment,
                    BackgroundAlpha = surface.BackgroundAlpha
                };
            }

            public void Apply(IMyTextSurface surface)
            {
                if (surface == null)
                    return;

                surface.ScriptForegroundColor = ScriptForegroundColor;
                surface.ScriptBackgroundColor = ScriptBackgroundColor;
                surface.Font = Font;
                surface.FontSize = FontSize;
                surface.TextPadding = TextPadding;
                surface.Alignment = Alignment;
                surface.BackgroundAlpha = BackgroundAlpha;
            }
        }
    }
}
