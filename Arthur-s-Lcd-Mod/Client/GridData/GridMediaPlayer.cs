using System;
using System.Collections.Generic;
using System.Diagnostics;
using LcdMod.Client.Audio;
using LcdMod.Common.Audio;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using IMySoundBlock = SpaceEngineers.Game.ModAPI.IMySoundBlock;

namespace LcdMod.Client.GridData
{
    public sealed class GridMediaPlayer
    {
        const double TARGET_SUBMITTED_SECONDS = 0.5;
        const double BUFFER_CHUNK_SECONDS = 0.5;
        // Raw PCM voices do not get normal cue-based distance refreshes, so
        // keep the voice spatialized and apply the audible falloff manually.
        const float DEFAULT_AUDIBLE_MAX_DISTANCE = 50f;
        const float SPATIALIZATION_MAX_DISTANCE = 100000f;
        const float VOLUME_UPDATE_EPSILON = 0.0025f;
        const int SPECTRUM_WINDOW_SAMPLES = 1024;
        const int STREAM_SAMPLE_RATE = 24000;
        const int STREAM_BLOCK_ALIGN = 2;

        readonly Queue<PlaybackBuffer> _pendingBuffers = new Queue<PlaybackBuffer>();
        readonly Stopwatch _playbackClock = new Stopwatch();

        MyEntity3DSoundEmitter _emitter;
        IMyTerminalBlock _sourceBlock;
        long _decodeToken;
        double _submittedSeconds;
        double _playbackSegmentStartSeconds;
        double _pausedPositionSeconds;
        int _currentBytesPerSecond;
        PcmWaveData _currentPcm;
        float _lastAppliedEmitterVolume = -1f;
        bool _decodePending;
        bool _streaming;
        bool _paused;
        float _volume = 1f;
        string _lastError;
        string _status = "Idle";

        sealed class PlaybackBuffer
        {
            public byte[] Samples;
            public double DurationSeconds;
        }

        sealed class DecodeWork
        {
            public long Token;
            public string SoundSubtype;
            public string WavePath;
            public PcmWaveData Pcm;
            public string FailureReason;
            public GameAudioContainerKind ContainerKind;
            public string SourceFormatDisplayName;
            public bool IsLocal;
            public AudioAssetMetadata LocalAsset;
            public double StartPositionSeconds;
        }

        public string CurrentSoundSubtype { get; private set; }
        public string CurrentWavePath { get; private set; }
        public double CurrentDurationSeconds { get; private set; }
        public string Status => _status;
        public string LastError => _lastError;
        public int PendingBufferCount => _pendingBuffers.Count;
        public bool IsDecoding => _decodePending;
        public bool IsPaused => _paused;

        public float Volume
        {
            get { return _volume; }
            set
            {
                var volume = Clamp01(value);
                if (Math.Abs(_volume - volume) <= float.Epsilon)
                    return;

                _volume = volume;
                ApplyEmitterVolume(force: true);
            }
        }

        public bool IsPlaying
        {
            get { return !_paused && (_streaming || _decodePending || _pendingBuffers.Count > 0 || IsEmitterPlaying); }
        }

        public bool CanSeek
        {
            get { return _currentPcm != null && CurrentDurationSeconds > 0.0 && !_decodePending; }
        }

        public double CurrentPositionSeconds
        {
            get { return GetCurrentPositionSeconds(); }
        }

        public bool IsEmitterPlaying
        {
            get { return _emitter != null && _emitter.IsPlaying; }
        }

        public bool IsActive
        {
            get { return _streaming || _decodePending || _paused || _pendingBuffers.Count > 0 || IsEmitterPlaying; }
        }

        public bool HasLoadedAudio
        {
            get
            {
                return _currentPcm != null ||
                       CurrentDurationSeconds > 0.0 ||
                       !string.IsNullOrEmpty(CurrentSoundSubtype) ||
                       !string.IsNullOrEmpty(CurrentWavePath);
            }
        }

        public bool FillSpectrumLevels(float[] levels)
        {
            if (levels == null || levels.Length == 0)
                return false;

            for (int i = 0; i < levels.Length; i++)
                levels[i] = 0f;

            var pcm = _currentPcm;
            if (pcm == null ||
                pcm.Samples == null ||
                pcm.Samples.Length == 0 ||
                pcm.SampleRate == 0 ||
                pcm.BlockAlign < 2 ||
                pcm.BitsPerSample != 16)
            {
                return false;
            }

            int frameCount = pcm.Samples.Length / pcm.BlockAlign;
            if (frameCount <= 0)
                return false;

            int window = Math.Min(SPECTRUM_WINDOW_SAMPLES, frameCount);
            if (window < 32)
                return false;

            int sampleRate = checked((int)pcm.SampleRate);
            int startFrame = (int)(GetCurrentPositionSeconds() * sampleRate);
            if (startFrame > frameCount - window)
                startFrame = frameCount - window;
            if (startFrame < 0)
                startFrame = 0;

            double minHz = 60.0;
            double maxHz = Math.Min(sampleRate * 0.47, 10000.0);
            if (maxHz <= minHz)
                maxHz = Math.Max(minHz + 1.0, sampleRate * 0.45);

            double logSpan = Math.Log(maxHz / minHz);
            for (int i = 0; i < levels.Length; i++)
            {
                double position = (i + 0.5) / levels.Length;
                double frequency = minHz * Math.Exp(logSpan * position);
                levels[i] = MeasureFrequencyLevel(pcm, startFrame, window, frequency);
            }

            return true;
        }

        public void PlayGameSound(IMyTerminalBlock sourceBlock, string soundSubtype, bool startPaused = false, double startPositionSeconds = 0.0)
        {
            _lastError = null;

            if (IsSourceBlockUnavailable(sourceBlock))
            {
                Fail("Screen block is not available.");
                return;
            }

            if (string.IsNullOrWhiteSpace(soundSubtype))
            {
                Fail("Select a sound first.");
                return;
            }

            var definition = FindSoundDefinition(soundSubtype);
            if (definition == null)
            {
                Fail("Sound definition not found: " + soundSubtype);
                return;
            }

            string relativeWavePath = FindStartWave(definition);
            if (string.IsNullOrEmpty(relativeWavePath))
            {
                Fail("Sound has no supported WAV or XWM start wave: " + soundSubtype);
                return;
            }

            StartGameAudioDecode(sourceBlock, definition.Id.SubtypeName, relativeWavePath, startPaused, startPositionSeconds);
        }

        public void PlayGameAudioFile(IMyTerminalBlock sourceBlock, string displayName, string definitionPath, bool startPaused = false, double startPositionSeconds = 0.0)
        {
            _lastError = null;

            if (IsSourceBlockUnavailable(sourceBlock))
            {
                Fail("Screen block is not available.");
                return;
            }

            definitionPath = GameAudioPcmLoader.ToDefinitionAudioPath(definitionPath);
            if (string.IsNullOrWhiteSpace(definitionPath))
            {
                Fail("Select a game audio file first.");
                return;
            }

            if (!GameAudioPcmLoader.IsSupportedAudioPath(definitionPath))
            {
                Fail("Unsupported game audio file: " + definitionPath);
                return;
            }

            StartGameAudioDecode(
                sourceBlock,
                string.IsNullOrEmpty(displayName) ? definitionPath : displayName,
                definitionPath,
                startPaused,
                startPositionSeconds);
        }

        public void PlayLocalAudio(IMyTerminalBlock sourceBlock, AudioAssetMetadata asset, bool startPaused = false, double startPositionSeconds = 0.0)
        {
            _lastError = null;

            if (IsSourceBlockUnavailable(sourceBlock))
            {
                Fail("Screen block is not available.");
                return;
            }

            if (asset == null || string.IsNullOrWhiteSpace(asset.RuntimePath))
            {
                Fail("Select a local audio file first.");
                return;
            }

            if (!AudioLibraryStorage.RuntimeWaveExists(asset))
            {
                Fail("Local audio archive entry not found: " + asset.RuntimePath);
                return;
            }

            StopInternal(false);

            _sourceBlock = sourceBlock;
            CurrentSoundSubtype = string.IsNullOrEmpty(asset.Id) ? asset.SourcePath : asset.Id;
            CurrentWavePath = asset.RuntimePath;
            CurrentDurationSeconds = 0.0;
            _decodePending = true;
            _paused = startPaused;
            _pausedPositionSeconds = SanitizeStartPosition(startPositionSeconds);
            var containerKind = GameAudioPcmLoader.GetContainerKind(CurrentWavePath);
            _status = "Loading local audio";
            long token = ++_decodeToken;

            var work = new DecodeWork
            {
                Token = token,
                SoundSubtype = CurrentSoundSubtype,
                WavePath = CurrentWavePath,
                ContainerKind = containerKind,
                IsLocal = true,
                LocalAsset = asset,
                StartPositionSeconds = _pausedPositionSeconds
            };

            MyAPIGateway.Parallel.Start(
                delegate { DecodeGameAudio(work); },
                delegate { CompleteGameAudioPlayback(work); });
        }

        void StartGameAudioDecode(IMyTerminalBlock sourceBlock, string displayName, string relativeWavePath, bool startPaused, double startPositionSeconds)
        {
            StopInternal(false);

            _sourceBlock = sourceBlock;
            CurrentSoundSubtype = displayName;
            CurrentWavePath = GameAudioPcmLoader.ToAudioGameContentPath(relativeWavePath);
            CurrentDurationSeconds = 0.0;
            _decodePending = true;
            _paused = startPaused;
            _pausedPositionSeconds = SanitizeStartPosition(startPositionSeconds);
            var containerKind = GameAudioPcmLoader.GetContainerKind(CurrentWavePath);
            _status = "Loading audio";
            long token = ++_decodeToken;

            var work = new DecodeWork
            {
                Token = token,
                SoundSubtype = CurrentSoundSubtype,
                WavePath = CurrentWavePath,
                ContainerKind = containerKind,
                StartPositionSeconds = _pausedPositionSeconds
            };

            MyAPIGateway.Parallel.Start(
                delegate { DecodeGameAudio(work); },
                delegate { CompleteGameAudioPlayback(work); });
        }

        public void Stop()
        {
            ResetPlaybackEngine();
        }

        public void StartStream(IMyTerminalBlock sourceBlock, string title)
        {
            _lastError = null;

            if (IsSourceBlockUnavailable(sourceBlock))
            {
                Fail("Screen block is not available.");
                return;
            }

            StopInternal(false);

            _sourceBlock = sourceBlock;
            CurrentSoundSubtype = string.IsNullOrWhiteSpace(title) ? "Audio stream" : title;
            CurrentWavePath = "stream";
            CurrentDurationSeconds = 0.0;
            _currentBytesPerSecond = STREAM_SAMPLE_RATE * STREAM_BLOCK_ALIGN;
            _streaming = true;
            _paused = false;
            _status = "Streaming";
        }

        public void AppendStreamChunk(byte[] pcmBytes, double durationSeconds)
        {
            if (!_streaming || pcmBytes == null || pcmBytes.Length == 0)
                return;

            _pendingBuffers.Enqueue(new PlaybackBuffer
            {
                Samples = pcmBytes,
                DurationSeconds = durationSeconds > 0.0 ? durationSeconds : pcmBytes.Length / (double)(STREAM_SAMPLE_RATE * STREAM_BLOCK_ALIGN)
            });

            if (!_paused)
                SubmitPendingBuffers();
        }

        public void EndStream()
        {
            _streaming = false;
        }

        public void ResetPlaybackEngine()
        {
            StopInternal(true);
            _lastError = null;
            _status = "Stopped";
        }

        public void Pause()
        {
            if (!IsActive)
                return;

            _pausedPositionSeconds = GetCurrentPositionSeconds();
            _paused = true;

            if (_emitter != null)
                StopEmitterSound(forced: false);

            RebuildPendingBuffersFromPosition(_pausedPositionSeconds);
            _playbackClock.Reset();
            _submittedSeconds = 0.0;
            _playbackSegmentStartSeconds = _pausedPositionSeconds;
            _status = "Paused";
        }

        public void Resume()
        {
            if (!_paused)
                return;

            _paused = false;
            RebuildPendingBuffersFromPosition(_pausedPositionSeconds);
            _status = _pendingBuffers.Count > 0 ? "Playing" : "Finished";
            SubmitPendingBuffers();
        }

        public void SeekRelative(double deltaSeconds)
        {
            SeekTo(GetCurrentPositionSeconds() + deltaSeconds);
        }

        public void SeekTo(double seconds)
        {
            if (!CanSeek)
                return;

            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
                seconds = 0.0;

            if (seconds < 0.0)
                seconds = 0.0;
            if (seconds > CurrentDurationSeconds)
                seconds = CurrentDurationSeconds;

            CloseEmitter();

            _pausedPositionSeconds = seconds;
            RebuildPendingBuffersFromPosition(seconds);

            if (_paused)
            {
                _status = "Paused";
                return;
            }

            _status = _pendingBuffers.Count > 0 ? "Playing" : "Finished";
            SubmitPendingBuffers();
        }

        public void Update()
        {
            if (_sourceBlock != null && IsSourceBlockUnavailable(_sourceBlock))
            {
                StopInternal(true);
                return;
            }

            if (_paused)
                return;

            UpdateEmitterPosition();
            ApplyEmitterVolume(force: false);
            SubmitPendingBuffers();
        }

        public void Unload()
        {
            StopInternal(true);
        }

        static MyAudioDefinition FindSoundDefinition(string subtype)
        {
            if (MyDefinitionManager.Static == null)
                return null;

            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition != null &&
                    string.Equals(
                        definition.Id.SubtypeName,
                        subtype,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }
            }

            return null;
        }

        public static bool IsSupportedSoundDefinition(MyAudioDefinition definition)
        {
            return !string.IsNullOrEmpty(FindStartWave(definition));
        }

        public static string FindStartWave(MyAudioDefinition definition)
        {
            AudioWavesDefinition data = definition;
            if (data == null || data.Waves == null)
                return null;

            for (int i = 0; i < data.Waves.Count; i++)
            {
                string path = data.Waves[i].Start;
                if (!string.IsNullOrWhiteSpace(path) &&
                    GameAudioPcmLoader.IsSupportedAudioPath(path))
                {
                    return path;
                }
            }

            return null;
        }

        static void DecodeGameAudio(DecodeWork work)
        {
            bool decoded;
            GameAudioContainerKind containerKind;

            if (work.IsLocal)
            {
                decoded = DecodeLocalAudio(work, out containerKind);
            }
            else
            {
                decoded = GameAudioPcmLoader.TryReadInGameContent(
                    work.WavePath,
                    out work.Pcm,
                    out work.FailureReason,
                    out containerKind);
            }

            work.ContainerKind = containerKind;

            if (work.Pcm != null)
                work.SourceFormatDisplayName = work.Pcm.SourceFormatDisplayName;

            if (!decoded)
                LogHelper.Log(MyLogSeverity.Warning, "Media player decode failed: " + work.FailureReason);
        }

        static bool DecodeLocalAudio(DecodeWork work, out GameAudioContainerKind containerKind)
        {
            containerKind = GameAudioContainerKind.Unknown;

            try
            {
                byte[] runtimeWaveBytes;
                if (!AudioLibraryStorage.TryReadRuntimeWave(work.LocalAsset, out runtimeWaveBytes, out work.FailureReason))
                    return false;

                CanonicalWavePayload payload;
                if (!CanonicalWaveReader.TryRead(runtimeWaveBytes, out payload, out work.FailureReason))
                    return false;

                work.Pcm = new PcmWaveData
                {
                    Samples = payload.PcmBytes,
                    Channels = 1,
                    SourceChannels = 1,
                    SampleRate = 24000,
                    SourceSampleRate = 24000,
                    BitsPerSample = 16,
                    SourceBitsPerSample = 16,
                    BlockAlign = 2,
                    SourceFormatDisplayName = "pcm wav"
                };
                containerKind = GameAudioContainerKind.PcmWave;
                return true;
            }
            catch (Exception error)
            {
                work.Pcm = null;
                work.FailureReason = error.Message;
                return false;
            }
        }

        void CompleteGameAudioPlayback(DecodeWork work)
        {
            if (work == null || work.Token != _decodeToken)
                return;

            _decodePending = false;

            if (work.Pcm == null)
            {
                Fail("Decode failed: " + (string.IsNullOrEmpty(work.FailureReason) ? "Unknown decoder error." : work.FailureReason));
                return;
            }

            CurrentSoundSubtype = work.SoundSubtype;
            CurrentWavePath = work.WavePath;
            CurrentDurationSeconds = work.Pcm.DurationSeconds;
            _currentPcm = work.Pcm;
            _currentBytesPerSecond = checked((int)(work.Pcm.SampleRate * work.Pcm.BlockAlign));
            var startPosition = ClampToDuration(work.StartPositionSeconds);
            RebuildPendingBuffersFromPosition(startPosition);

            if (_paused)
            {
                _status = "Paused";
                return;
            }

            _status = "Playing " + GetPlaybackFormatDisplayName(work);
            if (work.Pcm.WasResampled || work.Pcm.WasDownmixedToMono)
                _status += " (normalized to 24 kHz mono)";
            SubmitPendingBuffers();
        }

        double ClampToDuration(double seconds)
        {
            seconds = SanitizeStartPosition(seconds);
            if (CurrentDurationSeconds > 0.0 && seconds > CurrentDurationSeconds)
                return CurrentDurationSeconds;

            return seconds;
        }

        static double SanitizeStartPosition(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
                return 0.0;

            return seconds;
        }


        static string GetPlaybackFormatDisplayName(DecodeWork work)
        {
            if (work != null && !string.IsNullOrEmpty(work.SourceFormatDisplayName))
                return work.SourceFormatDisplayName;

            return work == null
                ? "audio"
                : GameAudioPcmLoader.GetContainerDisplayName(work.ContainerKind);
        }

        void RebuildPendingBuffersFromPosition(double seconds)
        {
            _pendingBuffers.Clear();
            _playbackClock.Reset();
            _submittedSeconds = 0.0;

            if (_currentPcm == null ||
                _currentPcm.Samples == null ||
                _currentPcm.SampleRate == 0 ||
                _currentPcm.BlockAlign == 0)
            {
                _playbackSegmentStartSeconds = 0.0;
                return;
            }

            int bytesPerSecond = _currentBytesPerSecond;
            if (bytesPerSecond <= 0)
                bytesPerSecond = checked((int)(_currentPcm.SampleRate * _currentPcm.BlockAlign));

            int blockAlign = _currentPcm.BlockAlign;
            int startOffset = (int)(seconds * bytesPerSecond);
            startOffset -= startOffset % blockAlign;
            if (startOffset < 0)
                startOffset = 0;
            if (startOffset > _currentPcm.Samples.Length)
                startOffset = _currentPcm.Samples.Length;

            _playbackSegmentStartSeconds = startOffset / (double)bytesPerSecond;
            _pausedPositionSeconds = _playbackSegmentStartSeconds;

            QueuePcmFromOffset(startOffset, bytesPerSecond, blockAlign);
        }

        void QueuePcmFromOffset(int startOffset, int bytesPerSecond, int blockAlign)
        {
            if (_currentPcm == null || _currentPcm.Samples == null)
                return;

            int chunkBytes = Math.Max(blockAlign, (int)(bytesPerSecond * BUFFER_CHUNK_SECONDS));
            chunkBytes -= chunkBytes % blockAlign;
            if (chunkBytes <= 0)
                chunkBytes = bytesPerSecond;

            for (int offset = startOffset; offset < _currentPcm.Samples.Length; offset += chunkBytes)
            {
                int length = Math.Min(chunkBytes, _currentPcm.Samples.Length - offset);
                if (length <= 0)
                    break;

                var samples = new byte[length];
                Buffer.BlockCopy(_currentPcm.Samples, offset, samples, 0, length);
                _pendingBuffers.Enqueue(new PlaybackBuffer
                {
                    Samples = samples,
                    DurationSeconds = length / (double)bytesPerSecond
                });
            }
        }

        double GetCurrentPositionSeconds()
        {
            if (!_paused)
                AdvancePlaybackSegmentIfEmitterStopped();

            if (CurrentDurationSeconds <= 0.0)
                return 0.0;

            double position;
            if (_paused)
            {
                position = _pausedPositionSeconds;
            }
            else if (IsEmitterPlaying)
            {
                position = _playbackSegmentStartSeconds + _playbackClock.Elapsed.TotalSeconds;
            }
            else if (_pendingBuffers.Count > 0)
            {
                position = _playbackSegmentStartSeconds;
            }
            else if (_decodePending)
            {
                position = 0.0;
            }
            else
            {
                position = string.IsNullOrEmpty(CurrentSoundSubtype) ? 0.0 : CurrentDurationSeconds;
            }

            if (position < 0.0)
                return 0.0;
            if (position > CurrentDurationSeconds)
                return CurrentDurationSeconds;

            return position;
        }

        void SubmitPendingBuffers()
        {
            AdvancePlaybackSegmentIfEmitterStopped();

            if (_pendingBuffers.Count == 0)
            {
                if (_emitter == null || !_emitter.IsPlaying)
                {
                    _playbackClock.Reset();
                    _submittedSeconds = 0.0;
                    if (!_decodePending && !_paused)
                        _status = _streaming ? "Buffering stream" : string.IsNullOrEmpty(CurrentSoundSubtype) ? "Idle" : "Finished";
                }
                return;
            }

            if (!EnsureEmitter())
                return;

            UpdateEmitterPosition();
            ApplyEmitterVolume(force: false);

            if (!_emitter.IsPlaying)
            {
                _playbackClock.Reset();
                _submittedSeconds = 0.0;
            }

            while (_pendingBuffers.Count > 0 &&
                   (!_emitter.IsPlaying ||
                    _submittedSeconds - _playbackClock.Elapsed.TotalSeconds < TARGET_SUBMITTED_SECONDS))
            {
                PlaybackBuffer buffer = _pendingBuffers.Dequeue();
                bool starting = !_emitter.IsPlaying;

                _emitter.PlaySound(
                    buffer.Samples,
                    volume: 1f,
                    maxDistance: GetSpatializationMaxDistance());
                if (starting)
                    ApplyEmitterVolume(force: true);

                _submittedSeconds += buffer.DurationSeconds;

                if (starting)
                    _playbackClock.Restart();
            }
        }

        void AdvancePlaybackSegmentIfEmitterStopped()
        {
            if (_emitter == null || _emitter.IsPlaying || _submittedSeconds <= 0.0)
                return;

            _playbackSegmentStartSeconds += _submittedSeconds;
            if (CurrentDurationSeconds > 0.0 && _playbackSegmentStartSeconds > CurrentDurationSeconds)
                _playbackSegmentStartSeconds = CurrentDurationSeconds;

            _submittedSeconds = 0.0;
            _playbackClock.Reset();
        }

        bool EnsureEmitter()
        {
            if (_sourceBlock == null || _sourceBlock.MarkedForClose)
            {
                Fail("Screen block is no longer available.");
                return false;
            }

            var entity = _sourceBlock as MyEntity;
            if (_emitter != null && _emitter.Entity == entity)
                return true;

            CloseEmitter();

            _emitter = new MyEntity3DSoundEmitter(entity, dopplerScaler: 0.0f)
            {
                Force3D = true,
                CustomMaxDistance = GetSpatializationMaxDistance()
            };
            if (entity == null)
                _emitter.SetPosition(_sourceBlock.GetPosition());

            _lastAppliedEmitterVolume = -1f;
            _playbackClock.Reset();
            _submittedSeconds = 0.0;
            return true;
        }

        void UpdateEmitterPosition()
        {
            if (_emitter == null || _sourceBlock == null || _emitter.Entity != null)
                return;

            _emitter.SetPosition(_sourceBlock.GetPosition());
        }

        void UpdateEmitterDistanceLimit()
        {
            if (_emitter == null)
                return;

            _emitter.CustomMaxDistance = GetSpatializationMaxDistance();
        }

        void ApplyEmitterVolume(bool force)
        {
            if (_emitter == null)
                return;

            UpdateEmitterDistanceLimit();
            var appliedVolume = _volume * GetDistanceGain();
            if (!force && Math.Abs(appliedVolume - _lastAppliedEmitterVolume) < VOLUME_UPDATE_EPSILON)
                return;

            _emitter.VolumeMultiplier = appliedVolume;
            _lastAppliedEmitterVolume = appliedVolume;
        }

        float GetDistanceGain()
        {
            if (_sourceBlock == null || _sourceBlock.MarkedForClose || _sourceBlock.Closed)
                return 0f;

            var session = MyAPIGateway.Session;
            var camera = session == null ? null : session.Camera;
            if (camera == null)
                return 1f;

            var audibleMaxDistance = GetAudibleMaxDistance();
            double distanceSquared = Vector3D.DistanceSquared(camera.Position, _sourceBlock.GetPosition());
            double maxDistanceSquared = audibleMaxDistance * audibleMaxDistance;
            if (distanceSquared >= maxDistanceSquared)
                return 0f;

            double distance = Math.Sqrt(distanceSquared);
            return (float)(1d - distance / audibleMaxDistance);
        }

        float GetSpatializationMaxDistance()
        {
            return Math.Max(SPATIALIZATION_MAX_DISTANCE, GetAudibleMaxDistance());
        }

        float GetAudibleMaxDistance()
        {
            var soundBlock = _sourceBlock as IMySoundBlock;
            if (soundBlock == null)
                return DEFAULT_AUDIBLE_MAX_DISTANCE;

            var range = soundBlock.Range;
            if (float.IsNaN(range) || float.IsInfinity(range) || range <= 0f)
                return DEFAULT_AUDIBLE_MAX_DISTANCE;

            return range;
        }

        void CloseEmitter()
        {
            if (_emitter == null)
                return;

            StopEmitterSound(forced: true);
            _emitter = null;
        }

        void StopEmitterSound(bool forced)
        {
            if (_emitter == null)
                return;

            try
            {
                _emitter.StopSound(forced, cleanUp: true, cleanupSound: true);
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Media player emitter reset failed: " + error.Message);
            }

            _lastAppliedEmitterVolume = -1f;
        }

        void StopInternal(bool clearIdentity)
        {
            ++_decodeToken;
            _decodePending = false;
            _pendingBuffers.Clear();

            CloseEmitter();
            _playbackClock.Reset();
            _submittedSeconds = 0.0;
            _playbackSegmentStartSeconds = 0.0;
            _pausedPositionSeconds = 0.0;
            _currentBytesPerSecond = 0;
            _currentPcm = null;
            _streaming = false;
            _paused = false;
            _status = "Stopped";

            if (clearIdentity)
            {
                CurrentSoundSubtype = null;
                CurrentWavePath = null;
                CurrentDurationSeconds = 0.0;
                _sourceBlock = null;
            }
        }

        static bool IsSourceBlockUnavailable(IMyTerminalBlock block)
        {
            if (block == null || block.MarkedForClose)
                return true;

            var functional = block as IMyFunctionalBlock;
            return functional != null && !functional.IsFunctional;
        }


        static float MeasureFrequencyLevel(PcmWaveData pcm, int startFrame, int window, double frequency)
        {
            if (pcm == null || pcm.Samples == null || window <= 0 || pcm.SampleRate == 0)
                return 0f;

            double k = Math.Round(window * frequency / pcm.SampleRate);
            if (k < 1.0)
                k = 1.0;

            double omega = 2.0 * Math.PI * k / window;
            double coeff = 2.0 * Math.Cos(omega);
            double s0 = 0.0;
            double s1 = 0.0;
            double s2 = 0.0;
            int blockAlign = pcm.BlockAlign;
            byte[] samples = pcm.Samples;
            double windowDenominator = Math.Max(1, window - 1);

            for (int i = 0; i < window; i++)
            {
                int byteIndex = (startFrame + i) * blockAlign;
                if (byteIndex + 1 >= samples.Length)
                    break;

                short sample = (short)(samples[byteIndex] | (samples[byteIndex + 1] << 8));
                double value = sample / 32768.0;
                double envelope = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / windowDenominator);

                s0 = value * envelope + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }

            double power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
            if (power <= 0.0 || double.IsNaN(power) || double.IsInfinity(power))
                return 0f;

            double magnitude = Math.Sqrt(power) / window;
            double level = Math.Pow(Math.Min(1.0, magnitude * 18.0), 0.65);
            if (double.IsNaN(level) || double.IsInfinity(level) || level <= 0.0)
                return 0f;
            if (level >= 1.0)
                return 1f;

            return (float)level;
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        void Fail(string message)
        {
            _lastError = string.IsNullOrEmpty(message) ? "Unknown media player error." : message;
            _status = "Error";
            LogHelper.Log(MyLogSeverity.Warning, "Media player: " + _lastError);
        }
    }
}
