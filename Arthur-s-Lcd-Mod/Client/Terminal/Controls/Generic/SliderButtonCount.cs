using System;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Layout;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SliderButtonCount : TerminalControlsWrapper
    {
        const float TITLE_HEIGHT_PIXELS = 40f;

        public override IMyTerminalControl TerminalControl { get; }

        public SliderButtonCount()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("ButtonCountSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(0f, 1f);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("Button count");
            slider.Tooltip = MyStringId.GetOrCompute(
                "Number of buttons. Values are snapped to complete rows and columns.");
            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock block, StringBuilder text)
        {
            float width;
            float height;
            float spacing;
            var scale = GetLayoutMetrics(block, out width, out height, out spacing);

            var config = ConfigManager.GetComponentForTerminalApp<ButtonPanelConfigComponent>(block);
            var configuredCount = config == null ? ButtonPanelLayout.DefaultButtonCount : config.ButtonCount;
            var layout = ButtonPanelLayout.Create(
                configuredCount,
                width,
                height,
                ButtonPanelLayout.PreferredButtonSizePixels * scale,
                spacing);

            if (configuredCount == ButtonPanelLayout.AutomaticButtonCount)
                text.Append(LocHelper.GetLoc("LcdMod_AutoOffset")).Append(": ");

            text.Append(layout.ButtonCount);
            text.Append(" buttons (");
            text.Append(layout.Columns);
            text.Append(" × ");
            text.Append(layout.Rows);
            text.Append(')');
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            float width;
            float height;
            float spacing;
            GetLayoutMetrics(block, out width, out height, out spacing);
            var count = ButtonPanelLayout.FromSlider(value, width, height, spacing);

            ConfigManager.ModifyComponentForTerminalApp<ButtonPanelConfigComponent>(
                block,
                config => config.ButtonCount = count);
        }

        float Getter(IMyTerminalBlock block)
        {
            float width;
            float height;
            float spacing;
            var scale = GetLayoutMetrics(block, out width, out height, out spacing);
            var config = ConfigManager.GetComponentForTerminalApp<ButtonPanelConfigComponent>(block);
            var count = config == null ? ButtonPanelLayout.DefaultButtonCount : config.ButtonCount;
            if (count == ButtonPanelLayout.AutomaticButtonCount)
            {
                count = ButtonPanelLayout.Create(
                    count,
                    width,
                    height,
                    ButtonPanelLayout.PreferredButtonSizePixels * scale,
                    spacing).ButtonCount;
            }

            return ButtonPanelLayout.ToSlider(count, width, height, spacing);
        }

        float GetLayoutMetrics(IMyTerminalBlock block, out float width, out float height, out float spacing)
        {
            var surface = GetThisSurface(block);
            var general = ConfigManager.GetComponentForCurrentSurface<GeneralConfigComponent>(
                block,
                LcdMod.Common.Helpers.Constants.GENERAL);
            var scale = general.GetScale();
            spacing = ButtonPanelLayout.SpacingPixels * scale;

            if (surface == null)
            {
                width = ButtonPanelLayout.MinimumButtonSizePixels;
                height = ButtonPanelLayout.MinimumButtonSizePixels;
                return scale;
            }

            var paddingRatio = Math.Max(0f, Math.Min(1f, surface.TextPadding / 100f));
            width = Math.Max(1f, surface.SurfaceSize.X * (1f - paddingRatio));
            height = Math.Max(1f, surface.SurfaceSize.Y * (1f - paddingRatio));

            if (general != null && general.TitleVisible && SurfaceAspectRatioHelper.CanShowTitle(surface))
            {
                var fontScale = surface.FontSize > 0f ? surface.FontSize : 1f;
                height = Math.Max(1f, height - TITLE_HEIGHT_PIXELS * scale * fontScale);
            }

            return scale;
        }
    }
}
