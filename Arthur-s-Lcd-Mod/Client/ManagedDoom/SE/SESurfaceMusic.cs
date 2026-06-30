using System;
using LcdMod.Client.Audio;
using ManagedDoom.Audio;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Renders Doom MUS lumps with TinyMidiPlayer and plays the resulting
    /// 24 kHz mono PCM through an entity-attached buffered sound emitter.
    /// </summary>
    public sealed class SESurfaceMusic : IMusic, IDisposable
    {
        const int OutputSampleRate = 24000;
        const float AudibleMaxDistance = 25f;
        const float SpatializationMaxDistance = 100000f;
        const float VolumeUpdateEpsilon = 0.0025f;

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
        float _lastAppliedVolume = -1f;

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
                    ApplyEmitterVolume(force: true);
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
            if (_disposed)
                return;

            if (_entity == null && _emitter != null && _block != null &&
                !_block.MarkedForClose && !_block.Closed)
                _emitter.SetPosition(_block.GetPosition());

            if (_emitter != null)
                ApplyEmitterVolume(force: false);

            if (!_loop || _currentPcm == null || _volume == 0)
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
            _emitter = new MyEntity3DSoundEmitter(_entity, dopplerScaler: 0.0f)
            {
                Force3D = true,

                // Raw PCM receives only its initial distance calculation. Give
                // the engine a practically flat range so direction remains 3D,
                // then apply the intended 25 m falloff through VolumeMultiplier.
                CustomMaxDistance = SpatializationMaxDistance
            };

            if (_entity == null && _block != null)
                _emitter.SetPosition(_block.GetPosition());

            _emitter.PlaySound(
                _currentPcm,
                volume: 1f,
                maxDistance: SpatializationMaxDistance);

            if (!_emitter.IsPlaying)
            {
                StopEmitter();
                return;
            }

            ApplyEmitterVolume(force: true);
        }

        void ApplyEmitterVolume(bool force)
        {
            if (_emitter == null)
                return;

            float appliedVolume = GetBaseVolume() * GetDistanceGain();
            if (!force && Math.Abs(appliedVolume - _lastAppliedVolume) < VolumeUpdateEpsilon)
                return;

            _emitter.VolumeMultiplier = appliedVolume;
            _lastAppliedVolume = appliedVolume;
        }

        float GetBaseVolume()
        {
            return _volume / (float)MaxVolume;
        }

        float GetDistanceGain()
        {
            if (_block == null || _block.MarkedForClose || _block.Closed)
                return 0f;

            var session = MyAPIGateway.Session;
            var camera = session != null ? session.Camera : null;
            if (camera == null)
                return 1f;

            double distanceSquared = Vector3D.DistanceSquared(
                camera.Position,
                _block.GetPosition());
            double maxDistanceSquared = AudibleMaxDistance * AudibleMaxDistance;
            if (distanceSquared >= maxDistanceSquared)
                return 0f;

            double distance = Math.Sqrt(distanceSquared);
            return (float)(1d - distance / AudibleMaxDistance);
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
                _lastAppliedVolume = -1f;
            }
        }
    }
}
