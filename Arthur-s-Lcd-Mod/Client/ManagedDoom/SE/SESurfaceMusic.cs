using System;
using LcdMod.Client.Audio;
using ManagedDoom.Audio;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Renders Doom MUS lumps with TinyMidiPlayer and plays the resulting
    /// 24 kHz mono PCM through an entity-attached buffered sound emitter.
    /// </summary>
    public sealed class SESurfaceMusic : IMusic, IDisposable
    {
        const int OutputSampleRate = 24000;

        readonly GameContent _content;
        readonly IMyCubeBlock _block;
        readonly MyEntity _entity;
        readonly TinyMidiPlayer _player;

        MyEntity3DSoundEmitter _emitter;
        Bgm _currentBgm;
        byte[] _currentPcm;
        bool _loop;
        bool _disposed;
        int _volume = 8;

        public SESurfaceMusic(GameContent content, IMyCubeBlock block)
        {
            if (content == null)
                throw new ArgumentNullException("content");

            _content = content;
            _block = block;
            _entity = block as MyEntity;
            _player = new TinyMidiPlayer(OutputSampleRate);
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
                    StopEmitter();
                else if (_emitter != null)
                    _emitter.CustomVolume = GetEmitterVolume();
                else if (_currentPcm != null)
                    PlayCurrent();
            }
        }

        public void StartMusic(Bgm bgm, bool loop)
        {
            if (_disposed)
                return;

            StopEmitter();
            _loop = loop;

            if (bgm == Bgm.NONE)
            {
                _currentBgm = Bgm.NONE;
                _currentPcm = null;
                return;
            }

            if ((int)bgm < 0 || (int)bgm >= DoomInfo.BgmNames.Length)
                return;

            if (_currentPcm == null || _currentBgm != bgm)
            {
                string name = DoomInfo.BgmNames[(int)bgm];
                var lumpNumber = _content.Wad.GetLumpNumber("D_" + name.ToUpperInvariant());
                if (lumpNumber < 0)
                {
                    _currentBgm = Bgm.NONE;
                    _currentPcm = null;
                    return;
                }

                try
                {
                    _currentPcm = _player.Render(_content.Wad.ReadLump(lumpNumber));
                    _currentBgm = bgm;
                }
                catch (Exception)
                {
                    // Unsupported replacement formats should disable music,
                    // not prevent the Doom app itself from starting.
                    _currentBgm = Bgm.NONE;
                    _currentPcm = null;
                    return;
                }
            }

            if (_volume > 0)
                PlayCurrent();
        }

        public void Update()
        {
            if (_disposed || !_loop || _currentPcm == null || _volume == 0)
                return;

            if (_emitter == null || !_emitter.IsPlaying)
                PlayCurrent();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            StopEmitter();
            _currentPcm = null;
        }

        void PlayCurrent()
        {
            if (_currentPcm == null || _currentPcm.Length == 0 || _volume == 0)
                return;

            StopEmitter();
            _emitter = new MyEntity3DSoundEmitter(_entity);
            if (_entity == null && _block != null)
                _emitter.SetPosition(_block.GetPosition());

            _emitter.PlaySound(_currentPcm, volume: GetEmitterVolume(), maxDistance: 25f);
            if (!_emitter.IsPlaying)
                StopEmitter();
        }

        float GetEmitterVolume()
        {
            return _volume / (float)MaxVolume;
        }

        void StopEmitter()
        {
            if (_emitter != null)
            {
                // Buffered voices are allocated directly by MyAudio.GetSound.
                // StopSound alone releases the emitter reference but does not
                // destroy that voice, so app recreation eventually loses audio.
                _emitter.StopSound(true, cleanUp: true, cleanupSound: true);
                _emitter = null;
            }
        }
    }
}
