using System;
using System.Collections.Generic;
using ManagedDoom.Audio;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Plays Doom DMX sound-effect lumps through Space Engineers' buffered
    /// entity emitter. The buffered voice format is fixed by the game to
    /// 24 kHz, signed 16-bit, mono PCM.
    /// </summary>
    public sealed class SESurfaceSound : ISound, IDisposable
    {
        const int OutputSampleRate = 24000;
        const int MaxChannels = 8;
        const int SfxCount = (int)Sfx.RADIO + 1;

        sealed class ActiveSound
        {
            public Mobj Source;
            public MyEntity3DSoundEmitter Emitter;
            public int Volume;
        }

        readonly GameContent _content;
        readonly IMyCubeBlock _block;
        readonly MyEntity _entity;
        readonly byte[][] _sampleCache = new byte[SfxCount][];
        readonly bool[] _sampleLoaded = new bool[SfxCount];
        readonly List<ActiveSound> _active = new List<ActiveSound>(MaxChannels);

        bool _paused;
        bool _disposed;
        int _volume = 15;

        public SESurfaceSound(GameContent content, IMyCubeBlock block)
        {
            if (content == null)
                throw new ArgumentNullException("content");

            _content = content;
            _block = block;
            _entity = block as MyEntity;
        }

        public int MaxVolume => 15;

        public int Volume
        {
            get { return _volume; }
            set
            {
                if (value < 0)
                    _volume = 0;
                else if (value > MaxVolume)
                    _volume = MaxVolume;
                else
                    _volume = value;

                if (_volume == 0)
                {
                    StopAll();
                    return;
                }

                for (var i = 0; i < _active.Count; i++)
                    _active[i].Emitter.CustomVolume = GetEmitterVolume(_active[i].Volume);
            }
        }

        public void SetListener(Mobj listener)
        {
            // All sounds intentionally originate from the LCD/cockpit block.
        }

        public void Update()
        {
            if (_disposed)
                return;

            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var emitter = _active[i].Emitter;

                if (_entity == null && _block != null && !_block.MarkedForClose && !_block.Closed)
                    emitter.SetPosition(_block.GetPosition());

                if (!emitter.IsPlaying)
                {
                    ReleaseEmitter(emitter);
                    _active.RemoveAt(i);
                }
            }
        }

        public void StartSound(Sfx sfx)
        {
            StartSound(null, sfx, SfxType.Diffuse, MaxVolume);
        }

        public void StartSound(Mobj mobj, Sfx sfx, SfxType type)
        {
            StartSound(mobj, sfx, type, MaxVolume);
        }

        public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume)
        {
            if (_disposed || _paused || _volume == 0 || volume <= 0 || sfx == Sfx.NONE)
                return;

            var samples = GetSamples(sfx);
            if (samples == null || samples.Length == 0)
                return;

            // Doom traditionally has eight software SFX channels. Reuse the
            // oldest one if a ninth sound starts before an earlier one ends.
            if (_active.Count >= MaxChannels)
            {
                ReleaseEmitter(_active[0].Emitter);
                _active.RemoveAt(0);
            }

            var emitter = new MyEntity3DSoundEmitter(_entity, dopplerScaler: 0.0f)
            {
                Force3D = true,
                CustomMaxDistance = 25f
            };

            if (_entity == null && _block != null)
                emitter.SetPosition(_block.GetPosition());

            var emitterVolume = GetEmitterVolume(volume);
            emitter.PlaySound(samples, volume: emitterVolume, maxDistance: 25f);
            if (!emitter.IsPlaying)
            {
                ReleaseEmitter(emitter);
                return;
            }

            _active.Add(new ActiveSound
            {
                Source = mobj,
                Emitter = emitter,
                Volume = volume
            });
        }

        public void StopSound(Mobj mobj)
        {
            if (mobj == null)
                return;

            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].Source == mobj)
                {
                    ReleaseEmitter(_active[i].Emitter);
                    _active.RemoveAt(i);
                }
            }
        }

        public void Reset()
        {
            StopAll();
        }

        public void Pause()
        {
            _paused = true;
            StopAll();
        }

        public void Resume()
        {
            if (!_disposed)
                _paused = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _paused = true;
            StopAll();
        }

        float GetEmitterVolume(int soundVolume)
        {
            if (soundVolume < 0)
                soundVolume = 0;
            else if (soundVolume > MaxVolume)
                soundVolume = MaxVolume;

            return (_volume / (float)MaxVolume) * (soundVolume / (float)MaxVolume);
        }

        void StopAll()
        {
            for (var i = 0; i < _active.Count; i++)
                ReleaseEmitter(_active[i].Emitter);

            _active.Clear();
        }

        static void ReleaseEmitter(MyEntity3DSoundEmitter emitter)
        {
            if (emitter == null)
                return;

            // StopSound detaches the voice from the emitter, but buffered
            // source voices are only destroyed by Cleanup. Without this call
            // every completed SFX or app recreation leaks an XAudio voice.
            emitter.StopSound(true, cleanUp: true, cleanupSound: true);
        }

        byte[] GetSamples(Sfx sfx)
        {
            var index = (int)sfx;
            if (index <= 0 || index >= SfxCount)
                return null;

            if (_sampleLoaded[index])
                return _sampleCache[index];

            _sampleLoaded[index] = true;

            string soundName = DoomInfo.SfxNames[index];
            var lumpNumber = _content.Wad.GetLumpNumber("DS" + soundName.ToUpperInvariant());

            // Vanilla Doom requests some Doom II-only names even when running
            // DOOM1.WAD. Match the original fallback behavior.
            if (lumpNumber < 0)
                lumpNumber = _content.Wad.GetLumpNumber("DSPISTOL");

            if (lumpNumber < 0)
                return null;

            _sampleCache[index] = ConvertDmxToPcm(_content.Wad.ReadLump(lumpNumber));
            return _sampleCache[index];
        }

        static byte[] ConvertDmxToPcm(byte[] lump)
        {
            if (lump == null || lump.Length < 9)
                return null;

            var format = ReadUInt16(lump, 0);
            var sourceRate = ReadUInt16(lump, 2);
            var declaredCount = ReadUInt32(lump, 4);

            // Doom's DMX sound-effect format is type 3: unsigned 8-bit mono.
            if (format != 3 || sourceRate == 0)
                return null;

            var available = lump.Length - 8;
            var sourceCount = available;
            if (declaredCount > 0 && declaredCount <= (uint)available)
                sourceCount = (int)declaredCount;

            if (sourceCount <= 0)
                return null;

            var outputCount = (int)(((long)sourceCount * OutputSampleRate + sourceRate - 1) / sourceRate);
            var output = new byte[outputCount * 2];

            // Linear resampling is enough for Doom's low-rate effects and
            // avoids any non-whitelisted codec or DSP dependency.
            for (var i = 0; i < outputCount; i++)
            {
                var sourceNumerator = (long)i * sourceRate;
                var sourceIndex = (int)(sourceNumerator / OutputSampleRate);
                var fraction = (int)(sourceNumerator % OutputSampleRate);

                if (sourceIndex >= sourceCount)
                    sourceIndex = sourceCount - 1;

                var nextIndex = sourceIndex + 1;
                if (nextIndex >= sourceCount)
                    nextIndex = sourceIndex;

                var sample0 = (lump[8 + sourceIndex] - 128) << 8;
                var sample1 = (lump[8 + nextIndex] - 128) << 8;
                var sample = sample0 + (int)(((long)(sample1 - sample0) * fraction) / OutputSampleRate);

                output[i * 2] = (byte)sample;
                output[i * 2 + 1] = (byte)(sample >> 8);
            }

            return output;
        }

        static int ReadUInt16(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8);
        }

        static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
        }
    }
}
