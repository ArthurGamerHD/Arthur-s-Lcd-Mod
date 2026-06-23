using System.Text;
using LcdMod.Client.Audio;
using LcdMod.Client.Config;
using LcdMod.Client.SurfaceScripts;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public abstract class SliderDoomVolume : TerminalControlsWrapper
    {
        readonly bool _sfx;

        public override IMyTerminalControl TerminalControl { get; }

        protected SliderDoomVolume(string id, string title, string tooltip, bool sfx)
        {
            _sfx = sfx;

            var slider = CreateControl<IMyTerminalControlSlider>(id);
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(0f, DoomAudioSettings.MaxVolume);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute(title);
            slider.Tooltip = MyStringId.GetOrCompute(tooltip);
            TerminalControl = slider;
        }

        public override bool VisibleForScript(string script)
        {
            return script == ManagedDoomSurfaceScript.ID;
        }

        void Writer(IMyTerminalBlock block, StringBuilder builder)
        {
            builder.Append((Getter(block) / DoomAudioSettings.MaxVolume).ToString("P0"));
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            var rounded = (int)(value + 0.5f);
            if (_sfx)
                DoomAudioSettings.SetSfxVolume(config, rounded);
            else
                DoomAudioSettings.SetMusicVolume(config, rounded);

            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            return _sfx
                ? DoomAudioSettings.GetSfxVolume(config)
                : DoomAudioSettings.GetMusicVolume(config);
        }
    }

    public sealed class SliderDoomSfxVolume : SliderDoomVolume
    {
        public SliderDoomSfxVolume()
            : base(
                "SliderDoomSfxVolume",
                "Doom SFX volume",
                "Volume of Doom sound effects emitted by this LCD surface.",
                true)
        {
        }
    }

    public sealed class SliderDoomMusicVolume : SliderDoomVolume
    {
        public SliderDoomMusicVolume()
            : base(
                "SliderDoomMusicVolume",
                "Doom music volume",
                "Volume of Doom music emitted by this LCD surface.",
                false)
        {
        }
    }
}
