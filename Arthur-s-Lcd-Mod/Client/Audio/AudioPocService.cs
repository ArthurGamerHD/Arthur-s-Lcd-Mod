#if EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.IO;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Utils;

namespace LcdMod.Client.Audio
{
    internal sealed class AudioPocService
    {
        readonly List<MyEntity3DSoundEmitter> _activeEmitters = new List<MyEntity3DSoundEmitter>();

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

            var player = MyAPIGateway.Session?.LocalHumanPlayer;
            if (player == null)
            {
                Show("Local player is not ready.", "Red");
                return;
            }

            var character = player.Character as MyEntity;
            var emitter = new MyEntity3DSoundEmitter(character);

            if (character == null)
                emitter.SetPosition(player.GetPosition());

            emitter.PlaySound(wave.Samples, volume: 1f, maxDistance: 25f);
            _activeEmitters.Add(emitter);

            Show("Playing " + fileName + " (" + wave.DurationSeconds.ToString("0.00") + "s)");
        }

        public void Update()
        {
            for (var i = _activeEmitters.Count - 1; i >= 0; i--)
            {
                var emitter = _activeEmitters[i];

                if (emitter == null || !emitter.IsPlaying)
                    _activeEmitters.RemoveAt(i);
            }
        }

        public void Unload()
        {
            for (var i = 0; i < _activeEmitters.Count; i++)
            {
                var emitter = _activeEmitters[i];
                if (emitter != null)
                    emitter.StopSound(forced: true);
            }

            _activeEmitters.Clear();
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
#endif
