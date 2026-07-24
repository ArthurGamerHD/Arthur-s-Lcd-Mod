using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Utils;

namespace LcdMod.Client.Audio
{
    internal sealed class AudioPocService
    {
        const double TargetSubmittedSeconds = 0.5;

        readonly Queue<PlaybackBuffer> _pendingBuffers =
            new Queue<PlaybackBuffer>();
        readonly Stopwatch _playbackClock = new Stopwatch();
        MyEntity3DSoundEmitter _playerEmitter;
        double _submittedSeconds;

        sealed class PlaybackBuffer
        {
            public byte[] Samples;
            public double DurationSeconds;
        }

        sealed class GameAudioDecodeWork
        {
            public string SoundSubtype;
            public string WavePath;
            public PcmWaveData Pcm;
            public string FailureReason;
            public GameAudioContainerKind ContainerKind;
        }

        public void PlayAudioCommand(string[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                Show("Usage: /lcdmod playaudio filename.wav", "Red");
                return;
            }

            var fileName = args[0].Trim();

            if (!IsSafeFlatWaveFileName(fileName))
            {
                Show("Use a flat .wav filename without folders.", "Red");
                return;
            }

            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
                return;

            if (!utilities.FileExistsInLocalStorage(fileName, typeof(AudioPocService)))
            {
                Show("Local WAV file not found: " + fileName, "Red");
                return;
            }

            PcmWaveData wave;
            string failureReason;

            try
            {
                using (var reader = utilities.ReadBinaryFileInLocalStorage(fileName, typeof(AudioPocService)))
                {
                    if (!PcmWaveReader.TryRead(reader, out wave, out failureReason))
                    {
                        Show("WAV rejected: " + failureReason, "Red");
                        return;
                    }
                }
            }
            catch (Exception error)
            {
                Show("WAV read failed: " + error.Message, "Red");
                return;
            }

            if (wave.WasDownmixedToMono)
                WarnDownmixedStereo(fileName, wave);

            Play(wave);

            Show("Playing " + fileName + " (" + wave.DurationSeconds.ToString("0.00") + "s)");
        }

        public void PlayGameAudioCommand(string[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                Show("Usage: /lcdmod playgameaudio sound-subtype", "Red");
                return;
            }

            string subtype = args[0].Trim();
            MyAudioDefinition definition = FindSoundDefinition(subtype);
            if (definition == null)
            {
                Show("Sound definition not found: " + subtype, "Red");
                return;
            }

            string relativeWavePath = FindStartWave(definition);
            if (string.IsNullOrEmpty(relativeWavePath))
            {
                Show("Sound has no supported WAV or XWM start wave: " + subtype, "Red");
                return;
            }

            var work = new GameAudioDecodeWork
            {
                SoundSubtype = definition.Id.SubtypeName,
                WavePath = Path.Combine("Audio", relativeWavePath),
                ContainerKind = GameAudioPcmLoader.GetContainerKind(relativeWavePath)
            };

            MyAPIGateway.Parallel.Start(
                delegate { DecodeGameAudio(work); },
                delegate { CompleteGameAudioPlayback(work); });

            Show("Loading game sound: " + work.SoundSubtype);
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

        static string FindStartWave(MyAudioDefinition definition)
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

        static void DecodeGameAudio(GameAudioDecodeWork work)
        {
            GameAudioContainerKind containerKind;
            bool decoded = GameAudioPcmLoader.TryReadInGameContent(
                work.WavePath,
                out work.Pcm,
                out work.FailureReason,
                out containerKind);
            work.ContainerKind = containerKind;

            if (!decoded)
                LogHelper.Log(MyLogSeverity.Warning, "Failed to read game audio PCM: " + work.FailureReason);
        }

        void CompleteGameAudioPlayback(GameAudioDecodeWork work)
        {
            if (work.Pcm == null)
            {
                Show(
                    "Game audio decode failed: " +
                    (string.IsNullOrEmpty(work.FailureReason)
                        ? "Unknown decoder error."
                        : work.FailureReason),
                    "Red");
                return;
            }

            Play(work.Pcm);
            Show(
                "Playing " + work.SoundSubtype +
                " [" + GameAudioPcmLoader.GetContainerDisplayName(work.ContainerKind) + "]" +
                " (" + work.Pcm.DurationSeconds.ToString("0.00") + "s)");
        }

        void Play(PcmWaveData wave)
        {
            if (wave == null || wave.Samples == null ||
                wave.SampleRate == 0 || wave.BlockAlign == 0)
            {
                Show("Decoded PCM is empty or has an invalid format.", "Red");
                return;
            }

            int bytesPerSecond = checked((int)(wave.SampleRate * wave.BlockAlign));
            for (int offset = 0; offset < wave.Samples.Length; offset += bytesPerSecond)
            {
                int length = Math.Min(bytesPerSecond, wave.Samples.Length - offset);
                var samples = new byte[length];
                Buffer.BlockCopy(wave.Samples, offset, samples, 0, length);
                _pendingBuffers.Enqueue(new PlaybackBuffer
                {
                    Samples = samples,
                    DurationSeconds = length / (double)bytesPerSecond
                });
            }

            SubmitPendingBuffers();
        }

        public void Update()
        {
            SubmitPendingBuffers();
        }

        void SubmitPendingBuffers()
        {
            if (_pendingBuffers.Count == 0)
            {
                if (_playerEmitter == null || !_playerEmitter.IsPlaying)
                {
                    _playbackClock.Reset();
                    _submittedSeconds = 0.0;
                }
                return;
            }

            if (!EnsurePlayerEmitter())
                return;

            if (!_playerEmitter.IsPlaying)
            {
                _playbackClock.Reset();
                _submittedSeconds = 0.0;
            }

            while (_pendingBuffers.Count > 0 &&
                   (!_playerEmitter.IsPlaying ||
                    _submittedSeconds - _playbackClock.Elapsed.TotalSeconds <
                    TargetSubmittedSeconds))
            {
                PlaybackBuffer buffer = _pendingBuffers.Dequeue();
                bool starting = !_playerEmitter.IsPlaying;

                _playerEmitter.PlaySound(
                    buffer.Samples,
                    volume: 1f,
                    maxDistance: 25f);
                _submittedSeconds += buffer.DurationSeconds;

                if (starting)
                    _playbackClock.Restart();
            }
        }

        bool EnsurePlayerEmitter()
        {
            var player = MyAPIGateway.Session != null
                ? MyAPIGateway.Session.LocalHumanPlayer
                : null;
            if (player == null)
            {
                Show("Local player is not ready.", "Red");
                return false;
            }

            var character = player.Character as MyEntity;
            if (_playerEmitter != null && _playerEmitter.Entity == character)
                return true;

            if (_playerEmitter != null)
                _playerEmitter.StopSound(forced: true);

            _playerEmitter = new MyEntity3DSoundEmitter(character);
            if (character == null)
                _playerEmitter.SetPosition(player.GetPosition());

            _playbackClock.Reset();
            _submittedSeconds = 0.0;
            return true;
        }

        public void Unload()
        {
            if (_playerEmitter != null)
                _playerEmitter.StopSound(forced: true);

            _playerEmitter = null;
            _pendingBuffers.Clear();
            _playbackClock.Reset();
            _submittedSeconds = 0.0;
        }

        static bool IsSafeFlatWaveFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
                return false;

            return string.Equals(Path.GetExtension(fileName), ".wav", StringComparison.OrdinalIgnoreCase);
        }

        static void WarnDownmixedStereo(string fileName, PcmWaveData wave)
        {
            var message = "Non-mono WAV source is inefficient; downmixing " + fileName + " to mono.";
            Show(message, "Yellow");
            LogHelper.Log(MyLogSeverity.Warning,
                "Audio POC downmixed non-mono source to mono: file=" + fileName +
                ", sourceChannels=" + wave.SourceChannels +
                ", pcmBytes=" + wave.Samples.Length +
                ", duration=" + wave.DurationSeconds.ToString("0.00"));
        }

        static void Show(string text, string font = "White")
        {
            MyAPIGateway.Utilities?.ShowNotification(text, 5000, font);
        }
    }
}
