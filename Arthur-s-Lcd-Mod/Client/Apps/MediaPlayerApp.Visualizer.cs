using System;
using Adk.Gui.Lcd;
using Sandbox.ModAPI;
using SeAudioVisualizer.Core;
using SeAudioVisualizer.SpaceEngineers;

namespace LcdMod.Client.Apps
{
    public sealed partial class MediaPlayerApp
    {
        const int VISUALIZER_TICKS_PER_SECOND = 60;
        const int PCM16_BYTES_PER_SAMPLE = 2;

        SpaceEngineersVisualizerHost _textureVisualizer;
        byte[] _visualizerPcm;
        int _visualizerSilentTicks;
        bool _visualizerClosed;
        bool _visualizerDetached;
        Common.Config.Components.MediaVisualizerInstanceSettings[] _appliedVisualizerInstances;
        bool _hasAppliedVisualizerSettings;
        int _appliedVisualizerWidth;
        int _appliedVisualizerHeight;

        internal string MediaVisualizerTexture => _player != null && _player.Video != null
            ? _player.Video.TextureName
            : _textureVisualizer?.CurrentTexture?.MaterialKey;

        bool CanRenderVisualizer => !_visualizerClosed && !_visualizerDetached && _interactiveHost != null &&
            _nativeViewModel.ShowVisualizer && MyAPIGateway.GeneratedTextures.IsSupported;

        bool NeedsVisualizerFrames => CanRenderVisualizer && _visualizerSilentTicks < 120;

        void TickVisualizerTexture()
        {
            if (!CanRenderVisualizer || (_player != null && _player.Video != null))
            {
                ReleaseVisualizerTexture();
                return;
            }

            if (_textureVisualizer == null)
            {
                int side = Math.Min(
                    AudioVisualizerOptions.DefaultTextureSize,
                    MyAPIGateway.GeneratedTextures.MaxTextureSide);

                if (side <= 0)
                    return;

                var options = new AudioVisualizerOptions
                {
                    TextureWidth = side,
                    TextureHeight = side,

                    // Space Engineers is the configuration gate. The Core still owns
                    // the original visualizer behavior/composition for every allowed
                    // upstream type.
                    EnabledVisualizers = AudioVisualizerFlags.None
                };

                var hostOptions = new VisualizerHostOptions
                {
                    // The app ticks at 60 Hz while generated textures are published
                    // at 30 Hz.
                    RenderEveryNthTick = 2,
                    TextureNameHint = "MediaPlayerVisualizer",

                    // Pull the current media visualizer rectangle whenever the host is
                    // about to start a new worker frame. The mixin preserves this
                    // aspect ratio and safely replaces the generated texture if the
                    // dimensions changed.
                    RenderSizeProvider = GetVisualizerRenderSize
                };

                _textureVisualizer =
                    SpaceEngineersVisualizerHost.Create(
                        options,
                        hostOptions);
                // Drawn behind the player on the app's background layer, so the screen shows through.
                _textureVisualizer.Visualizers.TransparentBackground = true;

                // Feed one sequential 60 Hz slice at a time. At the default 24 kHz
                // mono PCM16 format this is 400 samples / 800 bytes per tick.
                int samplesPerTick = options.SampleRate / VISUALIZER_TICKS_PER_SECOND;
                if (samplesPerTick <= 0)
                    samplesPerTick = 1;

                _visualizerPcm = new byte[samplesPerTick * PCM16_BYTES_PER_SAMPLE];
                _visualizerSilentTicks = 0;
                _appliedVisualizerInstances = null;
                _hasAppliedVisualizerSettings = false;
            }

            ApplyVisualizerSettings();

            if (_player != null && _player.IsPlaying)
            {
                _visualizerSilentTicks = 0;
                _player.FillVisualizerPcm(_visualizerPcm);
            }
            else
            {
                _visualizerSilentTicks++;
                Array.Clear(_visualizerPcm, 0, _visualizerPcm.Length);
            }

            // The Core owns the rolling 2048-sample (default) analysis history.
            _textureVisualizer.PushPcm16Mono(
                _visualizerPcm,
                0,
                _visualizerPcm.Length);

            // The player timestamp is the canonical visualizer clock. Color cycling,
            // rotations, shader phase, trail decay and related state now follow the
            // music timeline rather than worker completion / wall-clock time.
            double playbackSeconds = _player == null
                ? 0.0
                : _player.CurrentPositionSeconds;

            if (_textureVisualizer.Tick(playbackSeconds))
                LcdDynamicTexture.NotifyContentChanged(MediaVisualizerTexture);
        }

        void ApplyVisualizerSettings()
        {
            EnsureVisualizerDefaults();
            var instances = VisualizerInstances;
            var texture = _textureVisualizer.CurrentTexture;
            int width = texture == null ? AudioVisualizerOptions.DefaultTextureSize : texture.Width;
            int height = texture == null ? AudioVisualizerOptions.DefaultTextureSize : texture.Height;
            if (_hasAppliedVisualizerSettings && ReferenceEquals(_appliedVisualizerInstances, instances) &&
                _appliedVisualizerWidth == width && _appliedVisualizerHeight == height)
                return;

            _appliedVisualizerInstances = instances;
            _hasAppliedVisualizerSettings = true;
            _appliedVisualizerWidth = width;
            _appliedVisualizerHeight = height;
            MediaPlayer.MediaVisualizerSettingsResolver.Apply(instances, _textureVisualizer.Visualizers, width, height);
            _textureVisualizer.EnabledVisualizers = MediaPlayerComponent.VisualizerEnabled
                ? MediaPlayer.MediaVisualizerSettingsResolver.EnabledFlags(instances)
                : AudioVisualizerFlags.None;
        }

        internal Common.Config.Components.MediaVisualizerInstanceSettings[] VisualizerInstances =>
            MediaPlayerComponent.VisualizerInstancesConfigured ||
            (MediaPlayerComponent.VisualizerInstances != null && MediaPlayerComponent.VisualizerInstances.Length > 0)
                ? MediaPlayerComponent.VisualizerInstances
                : null;

        /// <summary>Replaces the visualizer settings; <paramref name="persist"/> also saves and syncs the block config.</summary>
        internal void SetVisualizerInstances(Common.Config.Components.MediaVisualizerInstanceSettings[] instances, bool persist)
        {
            MediaPlayerComponent.VisualizerInstances =
                instances ?? Array.Empty<Common.Config.Components.MediaVisualizerInstanceSettings>();
            MediaPlayerComponent.VisualizerInstancesConfigured = true;
            if (!persist)
                return;
            MarkLocalMediaInteraction();
            SyncConfig();
        }

        VisualizerRenderSize GetVisualizerRenderSize()
        {
            int maxSide = MyAPIGateway.GeneratedTextures.MaxTextureSide;

            if (maxSide <= 0)
                maxSide = AudioVisualizerOptions.DefaultTextureSize;
            if (maxSide > AudioVisualizerOptions.MaximumTextureSize)
                maxSide = AudioVisualizerOptions.MaximumTextureSize;

            float width = 0f;
            float height = 0f;

            if (width <= 0f || height <= 0f)
            {
                int side = Math.Min(AudioVisualizerOptions.DefaultTextureSize, maxSide);
                return new VisualizerRenderSize(side, side);
            }

            // Keep at least the normal 512px long-edge resolution, but allow the
            // texture to grow with a larger screen up to SE's 1024px hard limit.
            // A single uniform scale keeps the source aspect ratio intact.
            float longest = Math.Max(width, height);
            float targetLongest = Math.Max(AudioVisualizerOptions.DefaultTextureSize, longest);
            if (targetLongest > maxSide)
                targetLongest = maxSide;

            float scale = targetLongest / longest;
            int pixelWidth = Math.Max(1, (int)Math.Round(width * scale));
            int pixelHeight = Math.Max(1, (int)Math.Round(height * scale));

            if (pixelWidth > maxSide) pixelWidth = maxSide;
            if (pixelHeight > maxSide) pixelHeight = maxSide;

            return new VisualizerRenderSize(pixelWidth, pixelHeight);
        }

        void ReleaseVisualizerTexture()
        {
            if (_textureVisualizer == null)
                return;

            if (MediaVisualizerTexture != null)
                LcdDynamicTexture.NotifyDisposed(MediaVisualizerTexture);

            _textureVisualizer.Dispose();
            _textureVisualizer = null;
            _visualizerPcm = null;
            _visualizerSilentTicks = 0;
        }

        internal void SuspendVisualizer()
        {
            _visualizerDetached = true;
            ReleaseVisualizerTexture();
        }

        public override void Close()
        {
            _visualizerClosed = true;
            ReleaseVisualizerTexture();
            base.Close();
        }
    }
}
