using System;
using System.Collections.Generic;

namespace LcdMod.Client.Audio
{
    /// <summary>
    /// Small dependency-free MIDI/MUS renderer for game-mod environments.
    /// Produces signed 16-bit mono PCM at the requested sample rate.
    /// The synthesizer intentionally uses generated waveforms instead of a
    /// SoundFont, keeping the code and asset footprint small.
    /// </summary>
    public sealed class TinyMidiPlayer
    {
        const int DefaultTempo = 500000;
        const int MaxVoices = 24;
        const int WaveTableSize = 1024;
        const int MaxRenderSeconds = 10 * 60;

        enum EventType : byte
        {
            NoteOff,
            NoteOn,
            Control,
            Program,
            PitchBend,
            Tempo,
            End
        }

        sealed class MidiEvent
        {
            public long Tick;
            public int Order;
            public EventType Type;
            public int Channel;
            public int Data1;
            public int Data2;
        }

        sealed class Voice
        {
            public bool Active;
            public bool Released;
            public bool Sustained;
            public bool Percussion;
            public int Channel;
            public int Note;
            public int Velocity;
            public int Program;
            public int Age;
            public double Phase;
            public double Step;
            public double Envelope;
            public uint Noise;
        }

        sealed class PcmWriter
        {
            byte[] _data;
            int _length;

            public PcmWriter(int capacity)
            {
                _data = new byte[Math.Max(1024, capacity)];
            }

            public int SampleCount => _length / 2;

            public void Write(short sample)
            {
                Ensure(2);
                _data[_length++] = (byte)sample;
                _data[_length++] = (byte)(sample >> 8);
            }

            public byte[] ToArray()
            {
                var result = new byte[_length];
                Buffer.BlockCopy(_data, 0, result, 0, _length);
                return result;
            }

            void Ensure(int count)
            {
                if (_length + count <= _data.Length)
                    return;

                var size = _data.Length * 2;
                while (size < _length + count)
                    size *= 2;

                var replacement = new byte[size];
                Buffer.BlockCopy(_data, 0, replacement, 0, _length);
                _data = replacement;
            }
        }

        static readonly double[] SineTable = BuildSineTable();

        readonly int _sampleRate;
        readonly Voice[] _voices = new Voice[MaxVoices];
        readonly int[] _program = new int[16];
        readonly int[] _volume = new int[16];
        readonly int[] _expression = new int[16];
        readonly int[] _pitchBend = new int[16];
        readonly bool[] _sustain = new bool[16];

        int _voiceAge;

        public TinyMidiPlayer(int sampleRate)
        {
            if (sampleRate < 8000 || sampleRate > 192000)
                throw new ArgumentException("The sample rate is outside the supported range.", "sampleRate");

            _sampleRate = sampleRate;
            for (var i = 0; i < _voices.Length; i++)
                _voices[i] = new Voice();

            ResetChannels();
        }

        public int SampleRate => _sampleRate;

        public byte[] Render(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            if (IsMus(data))
                return RenderMus(data);

            if (IsMidi(data))
                return RenderMidi(data);

            throw new ArgumentException("The data is neither a Doom MUS score nor a Standard MIDI file.", "data");
        }

        public byte[] RenderMus(byte[] data)
        {
            var events = ParseMus(data);
            return RenderEvents(events, 140, false);
        }

        public byte[] RenderMidi(byte[] data)
        {
            int division;
            var events = ParseMidi(data, out division);
            return RenderEvents(events, division, true);
        }

        static bool IsMus(byte[] data)
        {
            return data.Length >= 4 && data[0] == (byte)'M' && data[1] == (byte)'U'
                && data[2] == (byte)'S' && data[3] == 0x1A;
        }

        static bool IsMidi(byte[] data)
        {
            return data.Length >= 4 && data[0] == (byte)'M' && data[1] == (byte)'T'
                && data[2] == (byte)'h' && data[3] == (byte)'d';
        }

        List<MidiEvent> ParseMus(byte[] data)
        {
            if (!IsMus(data) || data.Length < 16)
                throw new ArgumentException("Invalid MUS header.", "data");

            var scoreLength = ReadUInt16LE(data, 4);
            var scoreStart = ReadUInt16LE(data, 6);
            if (scoreStart < 16 || scoreStart > data.Length)
                throw new ArgumentException("Invalid MUS score offset.", "data");

            var end = scoreStart + scoreLength;
            if (end < scoreStart || end > data.Length)
                end = data.Length;

            var events = new List<MidiEvent>(1024);
            var velocities = new int[16];
            for (var i = 0; i < velocities.Length; i++)
                velocities[i] = 64;

            var position = scoreStart;
            long tick = 0;
            var order = 0;
            var ended = false;

            while (position < end && !ended)
            {
                bool last;
                do
                {
                    var descriptor = ReadByte(data, ref position, end);
                    last = (descriptor & 0x80) != 0;
                    var type = (descriptor >> 4) & 7;
                    var musChannel = descriptor & 15;
                    var channel = MapMusChannel(musChannel);

                    switch (type)
                    {
                        case 0:
                        {
                            var note = ReadByte(data, ref position, end) & 0x7F;
                            Add(events, tick, order++, EventType.NoteOff, channel, note, 0);
                            break;
                        }
                        case 1:
                        {
                            var noteData = ReadByte(data, ref position, end);
                            var note = noteData & 0x7F;
                            if ((noteData & 0x80) != 0)
                                velocities[musChannel] = ReadByte(data, ref position, end) & 0x7F;

                            Add(events, tick, order++, EventType.NoteOn, channel, note, velocities[musChannel]);
                            break;
                        }
                        case 2:
                        {
                            var bend = ReadByte(data, ref position, end);
                            Add(events, tick, order++, EventType.PitchBend, channel, bend << 6, 0);
                            break;
                        }
                        case 3:
                        {
                            var system = ReadByte(data, ref position, end);
                            if (system == 10)
                                Add(events, tick, order++, EventType.Control, channel, 120, 0);
                            else if (system == 11)
                                Add(events, tick, order++, EventType.Control, channel, 123, 0);
                            else if (system == 14)
                                Add(events, tick, order++, EventType.Control, channel, 121, 0);
                            break;
                        }
                        case 4:
                        {
                            var controller = ReadByte(data, ref position, end);
                            var value = ReadByte(data, ref position, end) & 0x7F;
                            if (controller == 0)
                                Add(events, tick, order++, EventType.Program, channel, value, 0);
                            else
                                Add(events, tick, order++, EventType.Control, channel, MapMusController(controller), value);
                            break;
                        }
                        case 6:
                            ended = true;
                            Add(events, tick, order++, EventType.End, 0, 0, 0);
                            break;
                        default:
                            throw new ArgumentException("Unsupported or malformed MUS event.", "data");
                    }
                }
                while (!last && !ended && position < end);

                if (!ended && last)
                    tick += ReadVariableLength(data, ref position, end);
            }

            if (events.Count == 0 || events[events.Count - 1].Type != EventType.End)
                Add(events, tick, order, EventType.End, 0, 0, 0);

            return events;
        }

        List<MidiEvent> ParseMidi(byte[] data, out int division)
        {
            if (!IsMidi(data) || data.Length < 14)
                throw new ArgumentException("Invalid MIDI header.", "data");

            var headerLength = ReadInt32BE(data, 4);
            if (headerLength < 6 || 8 + headerLength > data.Length)
                throw new ArgumentException("Invalid MIDI header length.", "data");

            var format = ReadUInt16BE(data, 8);
            var trackCount = ReadUInt16BE(data, 10);
            division = ReadUInt16BE(data, 12);
            if (format > 1 || trackCount <= 0 || division <= 0 || (division & 0x8000) != 0)
                throw new ArgumentException("Only PPQ MIDI format 0 and 1 files are supported.", "data");

            var events = new List<MidiEvent>(2048);
            var position = 8 + headerLength;
            var order = 0;

            for (var track = 0; track < trackCount; track++)
            {
                if (position + 8 > data.Length || data[position] != (byte)'M'
                    || data[position + 1] != (byte)'T' || data[position + 2] != (byte)'r'
                    || data[position + 3] != (byte)'k')
                    throw new ArgumentException("Missing MIDI track header.", "data");

                var trackLength = ReadInt32BE(data, position + 4);
                position += 8;
                var trackEnd = position + trackLength;
                if (trackLength < 0 || trackEnd < position || trackEnd > data.Length)
                    throw new ArgumentException("Invalid MIDI track length.", "data");

                long tick = 0;
                var runningStatus = 0;

                while (position < trackEnd)
                {
                    tick += ReadVariableLength(data, ref position, trackEnd);
                    if (position >= trackEnd)
                        break;

                    var first = data[position++];
                    int status;
                    int firstData = -1;
                    if ((first & 0x80) != 0)
                    {
                        status = first;
                        if (status < 0xF0)
                            runningStatus = status;
                    }
                    else
                    {
                        if (runningStatus == 0)
                            throw new ArgumentException("MIDI running status without a status byte.", "data");
                        status = runningStatus;
                        firstData = first;
                    }

                    if (status == 0xFF)
                    {
                        var metaType = ReadByte(data, ref position, trackEnd);
                        var length = ReadVariableLength(data, ref position, trackEnd);
                        if (length < 0 || position + length > trackEnd)
                            throw new ArgumentException("Invalid MIDI meta event length.", "data");

                        if (metaType == 0x51 && length == 3)
                        {
                            var tempo = (data[position] << 16) | (data[position + 1] << 8) | data[position + 2];
                            Add(events, tick, order++, EventType.Tempo, 0, tempo, 0);
                        }
                        position += length;
                        runningStatus = 0;
                        continue;
                    }

                    if (status == 0xF0 || status == 0xF7)
                    {
                        var length = ReadVariableLength(data, ref position, trackEnd);
                        if (length < 0 || position + length > trackEnd)
                            throw new ArgumentException("Invalid MIDI SysEx length.", "data");
                        position += length;
                        runningStatus = 0;
                        continue;
                    }

                    var command = status & 0xF0;
                    var channel = status & 15;
                    var data1 = firstData >= 0 ? firstData : ReadByte(data, ref position, trackEnd);
                    var data2 = 0;
                    if (command != 0xC0 && command != 0xD0)
                        data2 = ReadByte(data, ref position, trackEnd);

                    switch (command)
                    {
                        case 0x80:
                            Add(events, tick, order++, EventType.NoteOff, channel, data1 & 0x7F, data2 & 0x7F);
                            break;
                        case 0x90:
                            Add(events, tick, order++, data2 == 0 ? EventType.NoteOff : EventType.NoteOn,
                                channel, data1 & 0x7F, data2 & 0x7F);
                            break;
                        case 0xB0:
                            Add(events, tick, order++, EventType.Control, channel, data1 & 0x7F, data2 & 0x7F);
                            break;
                        case 0xC0:
                            Add(events, tick, order++, EventType.Program, channel, data1 & 0x7F, 0);
                            break;
                        case 0xE0:
                            Add(events, tick, order++, EventType.PitchBend, channel,
                                (data1 & 0x7F) | ((data2 & 0x7F) << 7), 0);
                            break;
                    }
                }

                position = trackEnd;
            }

            events.Sort(CompareEvents);
            var endTick = events.Count == 0 ? 0 : events[events.Count - 1].Tick;
            Add(events, endTick, order, EventType.End, 0, 0, 0);
            events.Sort(CompareEvents);
            return events;
        }

        byte[] RenderEvents(List<MidiEvent> events, int timeBase, bool tempoBased)
        {
            ResetSynth();
            var writer = new PcmWriter(_sampleRate * 8);
            var tempo = DefaultTempo;
            long previousTick = 0;
            double sampleRemainder = 0;
            var maxSamples = _sampleRate * MaxRenderSeconds;

            for (var i = 0; i < events.Count; i++)
            {
                var item = events[i];
                var deltaTicks = item.Tick - previousTick;
                if (deltaTicks < 0)
                    deltaTicks = 0;

                double exactSamples;
                if (tempoBased)
                    exactSamples = (double)deltaTicks * tempo * _sampleRate / (timeBase * 1000000.0);
                else
                    exactSamples = (double)deltaTicks * _sampleRate / timeBase;

                exactSamples += sampleRemainder;
                var sampleCount = (int)exactSamples;
                sampleRemainder = exactSamples - sampleCount;

                if (writer.SampleCount + sampleCount > maxSamples)
                    sampleCount = maxSamples - writer.SampleCount;

                if (sampleCount > 0)
                    RenderSamples(writer, sampleCount);

                previousTick = item.Tick;
                if (writer.SampleCount >= maxSamples)
                    break;

                if (item.Type == EventType.Tempo)
                {
                    if (item.Data1 > 0)
                        tempo = item.Data1;
                }
                else if (item.Type == EventType.NoteOn)
                    NoteOn(item.Channel, item.Data1, item.Data2);
                else if (item.Type == EventType.NoteOff)
                    NoteOff(item.Channel, item.Data1);
                else if (item.Type == EventType.Program)
                    _program[item.Channel & 15] = item.Data1 & 127;
                else if (item.Type == EventType.Control)
                    ControlChange(item.Channel, item.Data1, item.Data2);
                else if (item.Type == EventType.PitchBend)
                    SetPitchBend(item.Channel, item.Data1);
                else if (item.Type == EventType.End)
                    break;
            }

            // Preserve short release tails without adding a large pause to
            // looping tracks.
            AllNotesOff(-1);
            RenderSamples(writer, _sampleRate / 5);
            return writer.ToArray();
        }

        void ResetSynth()
        {
            for (var i = 0; i < _voices.Length; i++)
                _voices[i].Active = false;
            _voiceAge = 0;
            ResetChannels();
        }

        void ResetChannels()
        {
            for (var i = 0; i < 16; i++)
            {
                _program[i] = 0;
                _volume[i] = 100;
                _expression[i] = 127;
                _pitchBend[i] = 8192;
                _sustain[i] = false;
            }
        }

        void NoteOn(int channel, int note, int velocity)
        {
            channel &= 15;
            note &= 127;
            velocity &= 127;
            if (velocity == 0)
            {
                NoteOff(channel, note);
                return;
            }

            Voice selected = null;
            for (var i = 0; i < _voices.Length; i++)
            {
                if (!_voices[i].Active)
                {
                    selected = _voices[i];
                    break;
                }
            }

            if (selected == null)
            {
                selected = _voices[0];
                for (var i = 1; i < _voices.Length; i++)
                {
                    if (_voices[i].Envelope < selected.Envelope
                        || (_voices[i].Envelope == selected.Envelope && _voices[i].Age < selected.Age))
                        selected = _voices[i];
                }
            }

            selected.Active = true;
            selected.Released = false;
            selected.Sustained = false;
            selected.Percussion = channel == 9;
            selected.Channel = channel;
            selected.Note = note;
            selected.Velocity = velocity;
            selected.Program = _program[channel];
            selected.Age = ++_voiceAge;
            selected.Phase = 0;
            selected.Step = GetStep(note, _pitchBend[channel]);
            selected.Envelope = selected.Percussion ? 1.0 : 0;
            selected.Noise = (uint)(0x9E3779B9u ^ (uint)(note * 1103515245) ^ (uint)_voiceAge);
        }

        void NoteOff(int channel, int note)
        {
            channel &= 15;
            note &= 127;
            for (var i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];
                if (!voice.Active || voice.Channel != channel || voice.Note != note)
                    continue;

                if (_sustain[channel])
                    voice.Sustained = true;
                else
                    voice.Released = true;
            }
        }

        void ControlChange(int channel, int controller, int value)
        {
            channel &= 15;
            value &= 127;
            switch (controller & 127)
            {
                case 7:
                    _volume[channel] = value;
                    break;
                case 11:
                    _expression[channel] = value;
                    break;
                case 64:
                {
                    var enabled = value >= 64;
                    if (_sustain[channel] && !enabled)
                    {
                        for (var i = 0; i < _voices.Length; i++)
                        {
                            if (_voices[i].Active && _voices[i].Channel == channel && _voices[i].Sustained)
                            {
                                _voices[i].Sustained = false;
                                _voices[i].Released = true;
                            }
                        }
                    }
                    _sustain[channel] = enabled;
                    break;
                }
                case 120:
                    AllSoundsOff(channel);
                    break;
                case 121:
                    _volume[channel] = 100;
                    _expression[channel] = 127;
                    _pitchBend[channel] = 8192;
                    _sustain[channel] = false;
                    UpdatePitch(channel);
                    break;
                case 123:
                    AllNotesOff(channel);
                    break;
            }
        }

        void SetPitchBend(int channel, int value)
        {
            channel &= 15;
            if (value < 0)
                value = 0;
            else if (value > 16383)
                value = 16383;
            _pitchBend[channel] = value;
            UpdatePitch(channel);
        }

        void UpdatePitch(int channel)
        {
            for (var i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].Active && _voices[i].Channel == channel)
                    _voices[i].Step = GetStep(_voices[i].Note, _pitchBend[channel]);
            }
        }

        void AllNotesOff(int channel)
        {
            for (var i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].Active && (channel < 0 || _voices[i].Channel == channel))
                {
                    _voices[i].Sustained = false;
                    _voices[i].Released = true;
                }
            }
        }

        void AllSoundsOff(int channel)
        {
            for (var i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].Active && (channel < 0 || _voices[i].Channel == channel))
                    _voices[i].Active = false;
            }
        }

        void RenderSamples(PcmWriter writer, int count)
        {
            const double masterGain = 0.18;
            var attackStep = 1.0 / Math.Max(1, _sampleRate / 200); // 5 ms
            var decayStep = 0.35 / Math.Max(1, _sampleRate / 12); // about 83 ms
            var releaseStep = 1.0 / Math.Max(1, _sampleRate / 6); // about 167 ms

            for (var sampleIndex = 0; sampleIndex < count; sampleIndex++)
            {
                double mix = 0;
                for (var i = 0; i < _voices.Length; i++)
                {
                    var voice = _voices[i];
                    if (!voice.Active)
                        continue;

                    if (voice.Percussion)
                    {
                        voice.Released = true;
                        voice.Envelope -= 1.0 / Math.Max(1, _sampleRate / 8);
                    }
                    else if (voice.Released)
                        voice.Envelope -= releaseStep;
                    else if (voice.Envelope < 1)
                        voice.Envelope += attackStep;
                    else if (voice.Envelope > 0.65)
                        voice.Envelope -= decayStep;

                    if (voice.Envelope <= 0)
                    {
                        voice.Active = false;
                        continue;
                    }
                    if (voice.Envelope > 1)
                        voice.Envelope = 1;

                    var channelGain = (_volume[voice.Channel] / 127.0)
                        * (_expression[voice.Channel] / 127.0)
                        * (voice.Velocity / 127.0);
                    mix += GetWave(voice) * voice.Envelope * channelGain;

                    voice.Phase += voice.Step;
                    if (voice.Phase >= 1.0)
                        voice.Phase -= (int)voice.Phase;
                }

                mix *= masterGain;
                // Gentle soft clipping keeps dense arrangements controlled.
                mix = mix / (1.0 + Math.Abs(mix));
                var output = (int)(mix * 32767.0);
                if (output < short.MinValue)
                    output = short.MinValue;
                else if (output > short.MaxValue)
                    output = short.MaxValue;
                writer.Write((short)output);
            }
        }

        double GetWave(Voice voice)
        {
            if (voice.Percussion)
            {
                voice.Noise = voice.Noise * 1664525u + 1013904223u;
                var noise = ((voice.Noise >> 8) & 0xFFFF) / 32767.5 - 1.0;
                var tone = SineTable[(int)(voice.Phase * WaveTableSize) & (WaveTableSize - 1)];
                return noise * 0.75 + tone * 0.25;
            }

            var family = (voice.Program >> 3) & 7;
            var phase = voice.Phase;
            if (family == 0 || family == 7)
                return SineTable[(int)(phase * WaveTableSize) & (WaveTableSize - 1)];
            if (family == 1 || family == 5)
                return 1.0 - 4.0 * Math.Abs(phase - 0.5);
            if (family == 2 || family == 4)
                return phase < 0.5 ? 1.0 : -1.0;
            if (family == 3)
                return phase * 2.0 - 1.0;
            return phase < 0.25 ? 1.0 : -0.5;
        }

        double GetStep(int note, int bend)
        {
            var bendSemitones = ((bend - 8192) / 8192.0) * 2.0;
            var frequency = 440.0 * Math.Pow(2.0, (note - 69 + bendSemitones) / 12.0);
            return frequency / _sampleRate;
        }

        static double[] BuildSineTable()
        {
            var result = new double[WaveTableSize];
            for (var i = 0; i < result.Length; i++)
                result[i] = Math.Sin(i * Math.PI * 2.0 / result.Length);
            return result;
        }

        static void Add(List<MidiEvent> events, long tick, int order, EventType type,
            int channel, int data1, int data2)
        {
            events.Add(new MidiEvent
            {
                Tick = tick,
                Order = order,
                Type = type,
                Channel = channel & 15,
                Data1 = data1,
                Data2 = data2
            });
        }

        static int CompareEvents(MidiEvent left, MidiEvent right)
        {
            var tick = left.Tick.CompareTo(right.Tick);
            return tick != 0 ? tick : left.Order.CompareTo(right.Order);
        }

        static int MapMusChannel(int channel)
        {
            if (channel == 15)
                return 9;
            if (channel >= 9)
                return channel + 1;
            return channel;
        }

        static int MapMusController(int controller)
        {
            switch (controller)
            {
                case 1: return 0;
                case 2: return 1;
                case 3: return 7;
                case 4: return 10;
                case 5: return 11;
                case 6: return 91;
                case 7: return 93;
                case 8: return 64;
                case 9: return 67;
                default: return controller;
            }
        }

        static int ReadByte(byte[] data, ref int position, int end)
        {
            if (position >= end)
                throw new ArgumentException("Unexpected end of music data.", "data");
            return data[position++];
        }

        static int ReadVariableLength(byte[] data, ref int position, int end)
        {
            var value = 0;
            for (var i = 0; i < 4; i++)
            {
                var current = ReadByte(data, ref position, end);
                value = (value << 7) | (current & 0x7F);
                if ((current & 0x80) == 0)
                    return value;
            }
            throw new ArgumentException("Variable-length value is too long.", "data");
        }

        static int ReadUInt16LE(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8);
        }

        static int ReadUInt16BE(byte[] data, int offset)
        {
            return (data[offset] << 8) | data[offset + 1];
        }

        static int ReadInt32BE(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16)
                | (data[offset + 2] << 8) | data[offset + 3];
        }
    }
}
