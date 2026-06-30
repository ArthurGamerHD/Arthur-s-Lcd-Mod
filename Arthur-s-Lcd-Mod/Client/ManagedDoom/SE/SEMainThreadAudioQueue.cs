using System;
using System.Threading;
using ManagedDoom.Audio;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Accepts audio requests from the Doom worker and executes the actual
    /// Space Engineers emitter calls when DispatchPending is invoked on the
    /// main thread. The queue is single-producer/single-consumer and never
    /// waits in either execution context.
    /// </summary>
    public sealed class SEMainThreadAudioQueue : IDisposable
    {
        const int QueueCapacity = 2048;
        const int QueueMask = QueueCapacity - 1;

        enum CommandKind
        {
            StartSound,
            StopSound,
            ResetSound,
            PauseSound,
            ResumeSound,
            SetSoundVolume,
            StartMusic,
            SetMusicVolume
        }

        struct AudioCommand
        {
            public CommandKind Kind;
            public Mobj Source;
            public Sfx Sfx;
            public SfxType SfxType;
            public Bgm Bgm;
            public int Value;
            public bool Loop;
        }

        sealed class QueuedSound : ISound
        {
            readonly SEMainThreadAudioQueue _owner;
            int _volume = 15;

            public QueuedSound(SEMainThreadAudioQueue owner)
            {
                _owner = owner;
            }

            public int MaxVolume => 15;

            public int Volume
            {
                get { return AtomicRead(ref _volume); }
                set
                {
                    var volume = ClampVolume(value);
                    AtomicWrite(ref _volume, volume);
                    _owner.Enqueue(new AudioCommand
                    {
                        Kind = CommandKind.SetSoundVolume,
                        Value = volume
                    });
                }
            }

            public void SetListener(Mobj listener)
            {
                // SESurfaceSound intentionally emits all SFX from the block,
                // so there is no engine-side listener state to dispatch.
            }

            public void Update()
            {
                // SESurfaceSound.Update is run by UpdateMainThread.
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
                _owner.Enqueue(new AudioCommand
                {
                    Kind = CommandKind.StartSound,
                    Source = mobj,
                    Sfx = sfx,
                    SfxType = type,
                    Value = volume
                });
            }

            public void StopSound(Mobj mobj)
            {
                _owner.Enqueue(new AudioCommand
                {
                    Kind = CommandKind.StopSound,
                    Source = mobj
                });
            }

            public void Reset()
            {
                _owner.Enqueue(new AudioCommand { Kind = CommandKind.ResetSound });
            }

            public void Pause()
            {
                _owner.Enqueue(new AudioCommand { Kind = CommandKind.PauseSound });
            }

            public void Resume()
            {
                _owner.Enqueue(new AudioCommand { Kind = CommandKind.ResumeSound });
            }

            public void SetVolumeFromMainThread(int value)
            {
                AtomicWrite(ref _volume, ClampVolume(value));
            }
        }

        sealed class QueuedMusic : IMusic
        {
            readonly SEMainThreadAudioQueue _owner;
            int _volume = 8;

            public QueuedMusic(SEMainThreadAudioQueue owner)
            {
                _owner = owner;
            }

            public int MaxVolume => 15;

            public int Volume
            {
                get { return AtomicRead(ref _volume); }
                set
                {
                    var volume = ClampVolume(value);
                    AtomicWrite(ref _volume, volume);
                    _owner.Enqueue(new AudioCommand
                    {
                        Kind = CommandKind.SetMusicVolume,
                        Value = volume
                    });
                }
            }

            public void StartMusic(Bgm bgm, bool loop)
            {
                _owner.Enqueue(new AudioCommand
                {
                    Kind = CommandKind.StartMusic,
                    Bgm = bgm,
                    Loop = loop
                });
            }

            public void SetVolumeFromMainThread(int value)
            {
                AtomicWrite(ref _volume, ClampVolume(value));
            }
        }

        readonly SESurfaceSound _sound;
        readonly SESurfaceMusic _music;
        readonly AudioCommand[] _commands = new AudioCommand[QueueCapacity];
        readonly QueuedSound _queuedSound;
        readonly QueuedMusic _queuedMusic;

        int _writePosition;
        int _readPosition;
        int _droppedCommands;
        int _disposed;

        public SEMainThreadAudioQueue(SESurfaceSound sound, SESurfaceMusic music)
        {
            if (sound == null)
                throw new ArgumentNullException("sound");
            if (music == null)
                throw new ArgumentNullException("music");

            _sound = sound;
            _music = music;
            _queuedSound = new QueuedSound(this);
            _queuedMusic = new QueuedMusic(this);
        }

        public ISound Sound => _queuedSound;
        public IMusic Music => _queuedMusic;
        public int DroppedCommandCount => AtomicRead(ref _droppedCommands);

        /// <summary>
        /// Runs queued emitter/music operations. Call only from the Space
        /// Engineers main thread. This method never waits for the worker.
        /// </summary>
        public void DispatchPending()
        {
            if (AtomicRead(ref _disposed) != 0)
                return;

            var read = _readPosition;
            var write = AtomicRead(ref _writePosition);

            while (read != write)
            {
                var command = _commands[read & QueueMask];
                read++;
                AtomicWrite(ref _readPosition, read);
                Dispatch(command);
                write = AtomicRead(ref _writePosition);
            }
        }

        public void UpdateMainThread()
        {
            if (AtomicRead(ref _disposed) != 0)
                return;

            _sound.Update();
            _music.Update();
        }

        public void SetSoundVolumeFromMainThread(int volume)
        {
            volume = ClampVolume(volume);
            _queuedSound.SetVolumeFromMainThread(volume);
            _sound.Volume = volume;
        }

        public void SetMusicVolumeFromMainThread(int volume)
        {
            volume = ClampVolume(volume);
            _queuedMusic.SetVolumeFromMainThread(volume);
            _music.Volume = volume;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // Dispose is called by ManagedDoomApp.Close on the main thread.
            _music.Dispose();
            _sound.Dispose();
        }

        void Enqueue(AudioCommand command)
        {
            if (AtomicRead(ref _disposed) != 0)
                return;

            var write = _writePosition;
            var read = AtomicRead(ref _readPosition);
            if (write - read >= QueueCapacity)
            {
                Interlocked.Increment(ref _droppedCommands);
                return;
            }

            _commands[write & QueueMask] = command;
            AtomicWrite(ref _writePosition, write + 1);
        }

        void Dispatch(AudioCommand command)
        {
            switch (command.Kind)
            {
                case CommandKind.StartSound:
                    _sound.StartSound(
                        command.Source,
                        command.Sfx,
                        command.SfxType,
                        command.Value);
                    break;

                case CommandKind.StopSound:
                    _sound.StopSound(command.Source);
                    break;

                case CommandKind.ResetSound:
                    _sound.Reset();
                    break;

                case CommandKind.PauseSound:
                    _sound.Pause();
                    break;

                case CommandKind.ResumeSound:
                    _sound.Resume();
                    break;

                case CommandKind.SetSoundVolume:
                    _sound.Volume = command.Value;
                    break;

                case CommandKind.StartMusic:
                    _music.StartMusic(command.Bgm, command.Loop);
                    break;

                case CommandKind.SetMusicVolume:
                    _music.Volume = command.Value;
                    break;
            }
        }

        static int AtomicRead(ref int value)
        {
            return Interlocked.CompareExchange(ref value, 0, 0);
        }

        static void AtomicWrite(ref int location, int value)
        {
            Interlocked.Exchange(ref location, value);
        }

        static int ClampVolume(int value)
        {
            if (value < 0)
                return 0;
            if (value > 15)
                return 15;
            return value;
        }
    }
}
