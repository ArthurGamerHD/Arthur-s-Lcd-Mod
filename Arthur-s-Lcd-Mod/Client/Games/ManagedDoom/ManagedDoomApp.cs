using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Audio;
using LcdMod.Client.Gui;
using LcdMod.Common.Config.Components;
using ManagedDoom;
using ManagedDoom.SE;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using DoomConfig = ManagedDoom.Config;

namespace LcdMod.Client.Apps
{
    public sealed class ManagedDoomApp : App
    {
        const string DoomWadDisplayPath = "Data/DOOM1.WAD";
        const string DoomWadRootPath = "DOOM1.WAD";
        const int WorkerFrameRate = 60;

        const int FrameFree = 0;
        const int FrameWriting = 1;
        const int FrameReady = 2;
        const int FrameReading = 3;
        const int FrameSlotCount = 2;

        static byte[] _cachedWadBytes;

        readonly List<MySprite> _sprites = new List<MySprite>();
        byte[] _displayFrame;
        int _displayFrameVersion;
        GameContent _content;
        GameContent _audioContent;
        SEVideoToTextSprite _video;
        SEMainThreadAudioQueue _audio;
        SEDoomUserInput _input;
        Doom _doom;
        bool _parallelWorkerStarted;

        byte[][] _frameBuffers;
        int[] _frameStates;
        int[] _frameSequences;
        int _producedFrameSequence;
        int _stopWorker;
        string _workerStatusMessage;
        string _workerFailureMessage;

        string _statusMessage;
        string _failureMessage;
        bool _initFailed;
        bool _closed;
        bool _parallelWorkerOwnsContent;
        int _pixelWidth;
        int _pixelHeight;
        int _appliedSfxVolume = -1;
        int _appliedMusicVolume = -1;

        public ManagedDoomApp(IAppHost host) : base(host)
        {
        }

        public byte[] DisplayFrame => _displayFrame;
        public int DisplayFrameVersion => _displayFrameVersion;
        public int FrameWidth => _pixelWidth;
        public int FrameHeight => _pixelHeight;
        public string StatusMessage => _statusMessage;
        public string FailureMessage => _failureMessage;
        public SEDoomUserInput Input => _input;

        /// <summary>
        /// Main-thread dispatch only. The Doom input screen captures controls,
        /// audio emitters are serviced here, and the latest worker framebuffer
        /// is copied for the generated-texture view.
        /// This method never waits for the Doom worker.
        /// </summary>
        public override void Update()
        {
            if (_closed)
                return;

            if (!_parallelWorkerStarted)
            {
                if (_initFailed)
                    return;

                if (!TryInitialize())
                {
                    _initFailed = true;
                    return;
                }
            }

            ApplyAudioVolumes();

            var audio = _audio;
            if (audio != null)
            {
                audio.DispatchPending();
                SyncMenuAudioVolumes(audio);
                audio.UpdateMainThread();
            }

            ConsumeLatestFrame();

            var workerStatus = AtomicRead(ref _workerStatusMessage);
            if (!string.IsNullOrEmpty(workerStatus))
                _statusMessage = workerStatus;
            var workerFailure = AtomicRead(ref _workerFailureMessage);
            if (!string.IsNullOrEmpty(workerFailure))
            {
                _failureMessage = workerFailure;
                _statusMessage = workerFailure;
            }
        }

        public override void LayoutChanged() { }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();

            if (_failureMessage == null && !string.IsNullOrEmpty(_statusMessage))
                Host.DrawMessage(_sprites, _statusMessage, "Warning", Host.ForegroundColor);
            return _sprites;
        }

        public override void Close()
        {
            if (_closed)
                return;

            _closed = true;
            Interlocked.Exchange(ref _stopWorker, 1);
            DoomUserProfileStorage.FlushPending();

            // All engine-facing resources are disposed on the main thread.
            // The worker is not joined: it observes the stop flag and releases
            // its read-only GameContent in its own finally block.
            var audio = _audio;
            _audio = null;
            if (audio != null)
                audio.Dispose();

            var input = _input;
            _input = null;
            if (input != null)
                input.Dispose();

            if (_audioContent != null)
            {
                _audioContent.Dispose();
                _audioContent = null;
            }

            if (!_parallelWorkerOwnsContent && _content != null)
                _content.Dispose();

            _content = null;
            _doom = null;
            _video = null;
            _parallelWorkerStarted = false;
            // The worker may still be finishing its current software render.
            // Keep the two handoff slots alive until that parallel worker
            // observes _stopWorker and exits; Close must not wait for it.
            _displayFrame = null;
            _appliedSfxVolume = -1;
            _appliedMusicVolume = -1;
            base.Close();
        }

        void ApplyAudioVolumes()
        {
            var audio = _audio;
            if (audio == null)
                return;

            var sfxVolume = DoomAudioSettings.GetSfxVolume(Config.TryGetComponent<GeneralConfigComponent>());
            if (sfxVolume != _appliedSfxVolume)
            {
                audio.SetSoundVolumeFromMainThread(sfxVolume);
                _appliedSfxVolume = sfxVolume;
            }

            var musicVolume = DoomAudioSettings.GetMusicVolume(Config.TryGetComponent<GeneralConfigComponent>());
            if (musicVolume != _appliedMusicVolume)
            {
                audio.SetMusicVolumeFromMainThread(musicVolume);
                _appliedMusicVolume = musicVolume;
            }
        }

        void SyncMenuAudioVolumes(SEMainThreadAudioQueue audio)
        {
            var general = Config.TryGetComponent<GeneralConfigComponent>();
            if (general == null)
                return;

            var sfxVolume = audio.SoundVolume;
            var musicVolume = audio.MusicVolume;
            bool changed = false;
            if (sfxVolume != _appliedSfxVolume)
            {
                DoomAudioSettings.SetSfxVolume(general, sfxVolume);
                _appliedSfxVolume = sfxVolume;
                changed = true;
            }
            if (musicVolume != _appliedMusicVolume)
            {
                DoomAudioSettings.SetMusicVolume(general, musicVolume);
                _appliedMusicVolume = musicVolume;
                changed = true;
            }

            if (changed)
            {
                var surface = Host as LcdMod.Client.SurfaceScripts.GameSurfaceScript;
                if (surface != null)
                    surface.SyncDoomAudioSettings();
            }
        }

        bool TryInitialize()
        {
            try
            {
                if (MyAPIGateway.GeneratedTextures == null || !MyAPIGateway.GeneratedTextures.IsSupported)
                    throw new InvalidOperationException("Doom requires beta generated textures.");

                DoomUserProfileStorage.Initialize();

                var doomArgs = new CommandLineArgs(new string[]
                {
                    "-skill",
                    "3",
                    "-nomouse",
                    "-nodeh"
                });

                var doomConfig = new DoomConfig();
                doomConfig.video_highresolution = false;
                // 7 keeps the status bar visible. 8 for borderless with no HUD/status bar.
                doomConfig.video_gamescreensize = 7;
                doomConfig.video_fpsscale = 1;
                doomConfig.video_displaymessage = true;

                var wadBytes = LoadWad();
                _content = GameContent.FromWadBytes("doom1", wadBytes, doomArgs);
                _audioContent = GameContent.FromWadBytes("doom1-audio", wadBytes, doomArgs);
                _video = new SEVideoToTextSprite(doomConfig, _content);

                // Audio has its own WAD streams. Wad.ReadLump uses a mutable
                // stream position, so sharing GameContent between the worker
                // renderer and main-thread audio would create a data race.
                var surfaceSound = new SESurfaceSound(_audioContent, Host.Block);
                var surfaceMusic = new SESurfaceMusic(_audioContent, Host.Block);
                _audio = new SEMainThreadAudioQueue(surfaceSound, surfaceMusic);
                _input = new SEDoomUserInput(Host);

                // Apply persisted settings before Doom queues title music or
                // startup effects. Actual emitters remain main-thread-only.
                _appliedSfxVolume = DoomAudioSettings.GetSfxVolume(Config.TryGetComponent<GeneralConfigComponent>());
                _appliedMusicVolume = DoomAudioSettings.GetMusicVolume(Config.TryGetComponent<GeneralConfigComponent>());
                _audio.SetSoundVolumeFromMainThread(_appliedSfxVolume);
                _audio.SetMusicVolumeFromMainThread(_appliedMusicVolume);

                _doom = new Doom(
                    doomArgs,
                    doomConfig,
                    _content,
                    _video,
                    _audio.Sound,
                    _audio.Music,
                    _input);

                _pixelWidth = _video.Width;
                _pixelHeight = _video.Height;
                var byteCount = _pixelWidth * _pixelHeight * 4;
                _frameBuffers = new byte[FrameSlotCount][];
                _frameStates = new int[FrameSlotCount];
                _frameSequences = new int[FrameSlotCount];
                for (var i = 0; i < FrameSlotCount; i++)
                    _frameBuffers[i] = new byte[byteCount];

                _statusMessage = null;
                _failureMessage = null;
                _workerStatusMessage = null;
                _workerFailureMessage = null;
                _producedFrameSequence = 0;
                _stopWorker = 0;
                _displayFrame = new byte[byteCount];
                _displayFrameVersion = 0;

                // Constructor-time music requests were queued on the main
                // thread, so dispatch them before the producer changes to the
                // parallel worker.
                _audio.DispatchPending();

                var parallel = MyAPIGateway.Parallel;
                if (parallel == null)
                    throw new InvalidOperationException("Space Engineers parallel API is unavailable.");

                var workerDoom = _doom;
                var workerVideo = _video;
                var workerInput = _input;
                var workerContent = _content;

                // StartBackground is the whitelisted ModAPI entry point for a
                // long-lived producer. The mod does not construct its own
                // execution primitive.
                _parallelWorkerOwnsContent = false;
                try
                {
                    parallel.StartBackground(
                        () => WorkerLoop(
                            workerDoom,
                            workerVideo,
                            workerInput,
                            workerContent,
                            parallel));
                    _parallelWorkerOwnsContent = true;
                    _parallelWorkerStarted = true;
                }
                catch
                {
                    _parallelWorkerOwnsContent = false;
                    throw;
                }

                return true;
            }
            catch (Exception e)
            {
                CloseAfterInitializationFailure();
                _failureMessage = "Failed to start Doom: " + e.Message;
                _statusMessage = _failureMessage;
                return false;
            }
        }

        void CloseAfterInitializationFailure()
        {
            Interlocked.Exchange(ref _stopWorker, 1);

            if (_audio != null)
            {
                _audio.Dispose();
                _audio = null;
            }

            if (_input != null)
            {
                _input.Dispose();
                _input = null;
            }

            if (_audioContent != null)
            {
                _audioContent.Dispose();
                _audioContent = null;
            }

            if (!_parallelWorkerOwnsContent && _content != null)
                _content.Dispose();

            _content = null;
            _doom = null;
            _video = null;
            _parallelWorkerStarted = false;
            _frameBuffers = null;
            _frameStates = null;
            _frameSequences = null;
        }

        void WorkerLoop(
            Doom doom,
            SEVideoToTextSprite video,
            SEDoomUserInput input,
            GameContent content,
            VRage.Game.ModAPI.IMyParallelTask parallel)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var frameInterval = 1.0 / WorkerFrameRate;
                var nextFrameTime = stopwatch.Elapsed.TotalSeconds;
                var ticAccumulator = 0;

                while (AtomicRead(ref _stopWorker) == 0)
                {
                    var now = stopwatch.Elapsed.TotalSeconds;
                    if (now < nextFrameTime)
                    {
                        parallel.Sleep(nextFrameTime - now > 0.002 ? 1 : 0);
                        continue;
                    }

                    // Do not spend seconds replaying stale frames after the
                    // process or game was suspended.
                    if (now - nextFrameTime > 0.25)
                        nextFrameTime = now;

                    var completed = false;
                    ticAccumulator += GameConst.TicRate;
                    if (ticAccumulator >= WorkerFrameRate)
                    {
                        ticAccumulator -= WorkerFrameRate;
                        input.UpdateEvents(doom);
                        completed = doom.Update() == UpdateResult.Completed;
                    }

                    var slot = AcquireWritableFrameSlot();
                    if (slot >= 0)
                    {
                        var published = false;
                        try
                        {
                            var frameFrac = Fixed.FromFloat(
                                (float)ticAccumulator / WorkerFrameRate);
                            video.RenderTo(doom, _frameBuffers[slot], frameFrac);
                            PublishFrame(slot);
                            published = true;
                        }
                        finally
                        {
                            if (!published)
                                AtomicWrite(ref _frameStates[slot], FrameFree);
                        }
                    }

                    if (completed)
                    {
                        AtomicWrite(ref _workerStatusMessage, "Doom completed.");
                        break;
                    }

                    nextFrameTime += frameInterval;
                }
            }
            catch (Exception e)
            {
                AtomicWrite(ref _workerFailureMessage, "Doom worker failed: " + e.Message);
            }
            finally
            {
                // GameContent belongs exclusively to this parallel worker.
                // Audio uses a separate WAD-backed GameContent instance on the
                // main thread, so no shutdown wait or cross-context disposal is
                // required here.
                try
                {
                    if (content != null)
                        content.Dispose();
                }
                catch
                {
                    // Shutdown should still complete if WAD disposal fails.
                }
            }
        }

        int AcquireWritableFrameSlot()
        {
            var states = _frameStates;
            if (states == null)
                return -1;

            for (var i = 0; i < FrameSlotCount; i++)
            {
                if (Interlocked.CompareExchange(
                    ref states[i],
                    FrameWriting,
                    FrameFree) == FrameFree)
                    return i;
            }

            // Both slots are occupied. Reclaim an unconsumed ready frame, but
            // never the slot currently being copied by the main thread.
            var first = 0;
            var second = 1;
            if (AtomicRead(ref _frameSequences[first]) >
                AtomicRead(ref _frameSequences[second]))
            {
                first = 1;
                second = 0;
            }

            if (Interlocked.CompareExchange(
                ref states[first],
                FrameWriting,
                FrameReady) == FrameReady)
                return first;

            if (Interlocked.CompareExchange(
                ref states[second],
                FrameWriting,
                FrameReady) == FrameReady)
                return second;

            // The main thread may be reading one slot while the other changes
            // ownership. Dropping this worker frame is always preferable to
            // waiting or touching a frame being consumed.
            return -1;
        }

        void PublishFrame(int slot)
        {
            var sequence = Interlocked.Increment(ref _producedFrameSequence);
            AtomicWrite(ref _frameSequences[slot], sequence);
            AtomicWrite(ref _frameStates[slot], FrameReady);
        }

        void ConsumeLatestFrame()
        {
            var slot = AcquireLatestReadyFrame();
            if (slot < 0)
                return;

            var consumedSequence = AtomicRead(ref _frameSequences[slot]);
            try
            {
                Buffer.BlockCopy(_frameBuffers[slot], 0, _displayFrame, 0, _displayFrame.Length);
                _displayFrameVersion++;
            }
            finally
            {
                AtomicWrite(ref _frameStates[slot], FrameFree);
            }

            // Free an older frame that was superseded while this frame was
            // acquired. Claim the second ready slot before checking its
            // sequence so the worker cannot publish a newer frame between the
            // sequence read and the release.
            for (var i = 0; i < FrameSlotCount; i++)
            {
                if (i == slot)
                    continue;

                if (Interlocked.CompareExchange(
                    ref _frameStates[i],
                    FrameReading,
                    FrameReady) != FrameReady)
                    continue;

                if (AtomicRead(ref _frameSequences[i]) <= consumedSequence)
                    AtomicWrite(ref _frameStates[i], FrameFree);
                else
                    AtomicWrite(ref _frameStates[i], FrameReady);
            }
        }

        int AcquireLatestReadyFrame()
        {
            var states = _frameStates;
            if (states == null)
                return -1;

            for (var attempt = 0; attempt < FrameSlotCount; attempt++)
            {
                var selected = -1;
                var selectedSequence = int.MinValue;

                for (var i = 0; i < FrameSlotCount; i++)
                {
                    if (AtomicRead(ref states[i]) != FrameReady)
                        continue;

                    var sequence = AtomicRead(ref _frameSequences[i]);
                    if (sequence > selectedSequence)
                    {
                        selected = i;
                        selectedSequence = sequence;
                    }
                }

                if (selected < 0)
                    return -1;

                if (Interlocked.CompareExchange(
                    ref states[selected],
                    FrameReading,
                    FrameReady) == FrameReady)
                    return selected;
            }

            return -1;
        }

        static int AtomicRead(ref int value)
        {
            return Interlocked.CompareExchange(ref value, 0, 0);
        }

        static void AtomicWrite(ref int location, int value)
        {
            Interlocked.Exchange(ref location, value);
        }

        static string AtomicRead(ref string value)
        {
            return Interlocked.CompareExchange(ref value, null, null);
        }

        static void AtomicWrite(ref string location, string value)
        {
            Interlocked.Exchange(ref location, value);
        }

        static byte[] LoadWad()
        {
            if (_cachedWadBytes != null)
                return _cachedWadBytes;

            if (TryReadWad(DoomWadDisplayPath, out _cachedWadBytes))
                return _cachedWadBytes;

            if (TryReadWad(DoomWadRootPath, out _cachedWadBytes))
                return _cachedWadBytes;

            throw new FileNotFoundException("Missing " + DoomWadDisplayPath + ".");
        }

        static bool TryReadWad(string relativePath, out byte[] bytes)
        {
            bytes = null;

            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
                return false;

            if (LcdModSessionComponent.ModItem != null)
            {
                var modItem = (MyObjectBuilder_Checkpoint.ModItem)LcdModSessionComponent.ModItem;
                if (!utilities.FileExistsInModLocation(relativePath, modItem))
                    return false;

                using (var reader = utilities.ReadBinaryFileInModLocation(relativePath, modItem))
                {
                    var length = reader.BaseStream.Length;
                    if (length <= 0L || length > int.MaxValue)
                        return false;

                    bytes = reader.ReadBytes((int)length);
                }
            }

            return bytes != null && bytes.Length != 0;
        }

    }
}
