#if EXPERIMENTAL
using System;
using System.Text;

namespace LcdMod.Common.Audio
{
    internal static class AudioImportProcessor
    {
        public const int TARGET_SAMPLE_RATE = 24000;
        public const int TARGET_CHANNELS = 1;
        public const int TARGET_BITS_PER_SAMPLE = 16;
        public const int MAX_RUNTIME_WAVE_BYTES = 64 * 1024 * 1024;

        const ushort WAVE_FORMAT_PCM = 1;
        const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
        const int MINIMUM_SAMPLE_RATE = 8000;
        const int MAXIMUM_SAMPLE_RATE = 192000;

        public static void ProcessImport(AudioImportWork work)
        {
            try
            {
                if (work == null)
                    throw new InvalidOperationException("Missing import work.");

                work.SourceSha256 = Sha256Hex(work.SourceBytes);

                var source = WaveReader.Parse(work.SourceBytes);
                work.SourceSampleRate = source.SampleRate;
                work.SourceChannels = source.Channels;
                work.SourceBitsPerSample = source.BitsPerSample;
                work.SourceEncodingName = source.EncodingName;

                if (source.IsCanonicalRuntimeFormat)
                {
                    work.RuntimeWaveBytes = work.SourceBytes;
                    work.WasNormalized = false;
                }
                else
                {
                    var monoSamples = source.DecodeToMonoFloat();
                    var resampled = AudioResampler.ResampleLinear(monoSamples, source.SampleRate, TARGET_SAMPLE_RATE);
                    var pcm = AudioQuantizer.ToPcm16LittleEndian(resampled);
                    work.RuntimeWaveBytes = WaveWriter.WritePcm16Mono(pcm, TARGET_SAMPLE_RATE);
                    work.WasNormalized = true;
                }

                if (work.RuntimeWaveBytes == null || work.RuntimeWaveBytes.Length == 0)
                    throw new InvalidOperationException("Audio import produced no runtime WAV bytes.");

                if (work.RuntimeWaveBytes.Length > MAX_RUNTIME_WAVE_BYTES)
                    throw new InvalidOperationException("Runtime WAV exceeds size limit.");

                var runtime = WaveReader.Parse(work.RuntimeWaveBytes);
                if (!runtime.IsCanonicalRuntimeFormat)
                    throw new InvalidOperationException("Runtime WAV is not canonical PCM 24000 Hz 16-bit mono.");

                work.RuntimeSha256 = Sha256Hex(work.RuntimeWaveBytes);
                work.PcmByteLength = runtime.PcmBytes.LongLength;
                work.DurationTicks = TimeSpan.FromSeconds(runtime.PcmBytes.Length / 48000.0).Ticks;
            }
            catch (Exception error)
            {
                if (work != null)
                    work.Error = error;
            }
        }

        public static string Sha256Hex(byte[] bytes)
        {
            var hash = ManagedSha256.ComputeHash(bytes);
            var builder = new StringBuilder(hash.Length * 2);

            for (var i = 0; i < hash.Length; i++)
                builder.Append(hash[i].ToString("X2"));

            return builder.ToString();
        }

        sealed class SourceWave
        {
            public ushort FormatTag;
            public int Channels;
            public int SampleRate;
            public int BitsPerSample;
            public int BlockAlign;
            public byte[] PcmBytes;

            public string EncodingName
            {
                get
                {
                    if (FormatTag == WAVE_FORMAT_PCM)
                        return "pcm";

                    if (FormatTag == WAVE_FORMAT_IEEE_FLOAT)
                        return "float";

                    return "format_" + FormatTag;
                }
            }

            public bool IsCanonicalRuntimeFormat =>
                FormatTag == WAVE_FORMAT_PCM &&
                SampleRate == TARGET_SAMPLE_RATE &&
                Channels == TARGET_CHANNELS &&
                BitsPerSample == TARGET_BITS_PER_SAMPLE &&
                BlockAlign == 2 &&
                PcmBytes != null &&
                PcmBytes.Length % 2 == 0;

            public float[] DecodeToMonoFloat()
            {
                if (PcmBytes == null || PcmBytes.Length == 0)
                    throw new InvalidOperationException("WAV data chunk is empty.");

                if (PcmBytes.Length % BlockAlign != 0)
                    throw new InvalidOperationException("PCM payload is not frame-aligned.");

                var frames = PcmBytes.Length / BlockAlign;
                var mono = new float[frames];
                var bytesPerSample = BitsPerSample / 8;

                for (var frame = 0; frame < frames; frame++)
                {
                    var frameOffset = frame * BlockAlign;
                    var sum = 0.0;

                    for (var channel = 0; channel < Channels; channel++)
                    {
                        var sampleOffset = frameOffset + channel * bytesPerSample;
                        sum += DecodeSample(sampleOffset);
                    }

                    mono[frame] = ClampFloat((float)(sum / Channels));
                }

                return mono;
            }

            float DecodeSample(int offset)
            {
                if (FormatTag == WAVE_FORMAT_PCM)
                {
                    if (BitsPerSample == 8)
                        return (PcmBytes[offset] - 128) / 128f;

                    if (BitsPerSample == 16)
                    {
                        var sample = (short)(PcmBytes[offset] | (PcmBytes[offset + 1] << 8));
                        return sample / 32768f;
                    }

                    if (BitsPerSample == 24)
                    {
                        var sample = PcmBytes[offset] | (PcmBytes[offset + 1] << 8) | (PcmBytes[offset + 2] << 16);
                        if ((sample & 0x800000) != 0)
                            sample = sample | unchecked((int)0xff000000);

                        return sample / 8388608f;
                    }

                    if (BitsPerSample == 32)
                    {
                        var sample = PcmBytes[offset] |
                                     (PcmBytes[offset + 1] << 8) |
                                     (PcmBytes[offset + 2] << 16) |
                                     (PcmBytes[offset + 3] << 24);
                        return sample / 2147483648f;
                    }
                }

                if (FormatTag == WAVE_FORMAT_IEEE_FLOAT && BitsPerSample == 32)
                    return ClampFloat(BitConverter.ToSingle(PcmBytes, offset));

                throw new InvalidOperationException("Unsupported WAV sample format.");
            }
        }

        static float ClampFloat(float value)
        {
            if (value > 1f)
                return 1f;

            if (value < -1f)
                return -1f;

            return value;
        }

        static class WaveReader
        {
            public static SourceWave Parse(byte[] bytes)
            {
                if (bytes == null || bytes.Length < 12)
                    throw new InvalidOperationException("File is too small to be a RIFF/WAVE file.");

                var reader = new ByteReader(bytes);
                var riff = reader.ReadFourCc();
                reader.ReadUInt32();
                var wave = reader.ReadFourCc();

                if (riff != "RIFF" || wave != "WAVE")
                    throw new InvalidOperationException("Expected RIFF/WAVE container.");

                var result = new SourceWave();
                var hasFormat = false;

                while (reader.Position + 8 <= reader.Length)
                {
                    var chunkId = reader.ReadFourCc();
                    var chunkSize = reader.ReadUInt32();
                    var chunkStart = reader.Position;
                    var chunkEnd = chunkStart + (long)chunkSize;

                    if (chunkEnd > reader.Length)
                        throw new InvalidOperationException("WAV chunk exceeds file length.");

                    if (chunkId == "fmt ")
                    {
                        if (chunkSize < 16)
                            throw new InvalidOperationException("Invalid fmt chunk.");

                        result.FormatTag = reader.ReadUInt16();
                        result.Channels = reader.ReadUInt16();
                        result.SampleRate = (int)reader.ReadUInt32();
                        reader.ReadUInt32();
                        result.BlockAlign = reader.ReadUInt16();
                        result.BitsPerSample = reader.ReadUInt16();
                        hasFormat = true;
                    }
                    else if (chunkId == "data")
                    {
                        if (chunkSize == 0)
                            throw new InvalidOperationException("WAV data chunk is empty.");

                        result.PcmBytes = reader.ReadBytes((int)chunkSize);
                    }

                    reader.Position = (int)chunkEnd;
                    if ((chunkSize & 1) != 0 && reader.Position < reader.Length)
                        reader.Position++;
                }

                Validate(result, hasFormat);
                return result;
            }

            static void Validate(SourceWave wave, bool hasFormat)
            {
                if (!hasFormat)
                    throw new InvalidOperationException("Missing fmt chunk.");

                if (wave.PcmBytes == null)
                    throw new InvalidOperationException("Missing data chunk.");

                if (wave.FormatTag != WAVE_FORMAT_PCM && wave.FormatTag != WAVE_FORMAT_IEEE_FLOAT)
                    throw new InvalidOperationException("Only PCM integer and 32-bit IEEE float WAV files are supported.");

                if (wave.Channels != 1 && wave.Channels != 2)
                    throw new InvalidOperationException("Only mono and stereo WAV files are supported.");

                if (wave.SampleRate < MINIMUM_SAMPLE_RATE || wave.SampleRate > MAXIMUM_SAMPLE_RATE)
                    throw new InvalidOperationException("WAV sample rate is outside the supported range.");

                if (wave.FormatTag == WAVE_FORMAT_PCM &&
                    wave.BitsPerSample != 8 &&
                    wave.BitsPerSample != 16 &&
                    wave.BitsPerSample != 24 &&
                    wave.BitsPerSample != 32)
                    throw new InvalidOperationException("Unsupported PCM bit depth.");

                if (wave.FormatTag == WAVE_FORMAT_IEEE_FLOAT && wave.BitsPerSample != 32)
                    throw new InvalidOperationException("Only 32-bit IEEE float WAV files are supported.");

                var expectedBlockAlign = wave.Channels * (wave.BitsPerSample / 8);
                if (wave.BlockAlign != expectedBlockAlign)
                    throw new InvalidOperationException("WAV block alignment does not match channel count and bit depth.");

                if (wave.PcmBytes.Length % wave.BlockAlign != 0)
                    throw new InvalidOperationException("WAV data payload is not frame-aligned.");
            }
        }

        sealed class ByteReader
        {
            readonly byte[] _bytes;

            public int Position;
            public int Length => _bytes.Length;

            public ByteReader(byte[] bytes)
            {
                _bytes = bytes;
            }

            public ushort ReadUInt16()
            {
                Ensure(2);
                var value = (ushort)(_bytes[Position] | (_bytes[Position + 1] << 8));
                Position += 2;
                return value;
            }

            public uint ReadUInt32()
            {
                Ensure(4);
                var value = (uint)(_bytes[Position] |
                                   (_bytes[Position + 1] << 8) |
                                   (_bytes[Position + 2] << 16) |
                                   (_bytes[Position + 3] << 24));
                Position += 4;
                return value;
            }

            public byte[] ReadBytes(int count)
            {
                Ensure(count);
                var result = new byte[count];
                Array.Copy(_bytes, Position, result, 0, count);
                Position += count;
                return result;
            }

            public string ReadFourCc()
            {
                Ensure(4);
                var result = Encoding.ASCII.GetString(_bytes, Position, 4);
                Position += 4;
                return result;
            }

            void Ensure(int count)
            {
                if (count < 0 || Position + count > _bytes.Length)
                    throw new InvalidOperationException("Unexpected end of WAV file.");
            }
        }

        static class AudioResampler
        {
            public static float[] ResampleLinear(float[] source, int sourceSampleRate, int targetSampleRate)
            {
                if (source == null || source.Length == 0)
                    return new float[0];

                if (sourceSampleRate == targetSampleRate)
                    return source;

                var targetLength = (int)Math.Max(1, Math.Round(source.Length * (double)targetSampleRate / sourceSampleRate));
                var result = new float[targetLength];
                var scale = sourceSampleRate / (double)targetSampleRate;

                for (var i = 0; i < targetLength; i++)
                {
                    var position = i * scale;
                    var index = (int)position;
                    var fraction = position - index;

                    if (index >= source.Length - 1)
                    {
                        result[i] = source[source.Length - 1];
                        continue;
                    }

                    result[i] = (float)(source[index] + (source[index + 1] - source[index]) * fraction);
                }

                return result;
            }
        }

        static class AudioQuantizer
        {
            public static byte[] ToPcm16LittleEndian(float[] samples)
            {
                if (samples == null)
                    return new byte[0];

                var bytes = new byte[samples.Length * 2];

                for (var i = 0; i < samples.Length; i++)
                {
                    var sample = ClampFloat(samples[i]);
                    var scaled = sample < 0f ? sample * 32768f : sample * 32767f;
                    var value = (short)Math.Round(scaled);
                    var offset = i * 2;

                    bytes[offset] = (byte)(value & 0xff);
                    bytes[offset + 1] = (byte)((value >> 8) & 0xff);
                }

                return bytes;
            }
        }

        static class WaveWriter
        {
            public static byte[] WritePcm16Mono(byte[] pcmBytes, int sampleRate)
            {
                if (pcmBytes == null)
                    throw new InvalidOperationException("Missing PCM bytes.");

                if (pcmBytes.Length % 2 != 0)
                    throw new InvalidOperationException("PCM bytes are not sample-aligned.");

                var bytes = new byte[44 + pcmBytes.Length];
                WriteFourCc(bytes, 0, "RIFF");
                WriteUInt32(bytes, 4, (uint)(36 + pcmBytes.Length));
                WriteFourCc(bytes, 8, "WAVE");
                WriteFourCc(bytes, 12, "fmt ");
                WriteUInt32(bytes, 16, 16);
                WriteUInt16(bytes, 20, WAVE_FORMAT_PCM);
                WriteUInt16(bytes, 22, 1);
                WriteUInt32(bytes, 24, (uint)sampleRate);
                WriteUInt32(bytes, 28, (uint)(sampleRate * 2));
                WriteUInt16(bytes, 32, 2);
                WriteUInt16(bytes, 34, 16);
                WriteFourCc(bytes, 36, "data");
                WriteUInt32(bytes, 40, (uint)pcmBytes.Length);
                Array.Copy(pcmBytes, 0, bytes, 44, pcmBytes.Length);
                return bytes;
            }

            static void WriteFourCc(byte[] bytes, int offset, string value)
            {
                for (var i = 0; i < 4; i++)
                    bytes[offset + i] = (byte)value[i];
            }

            static void WriteUInt16(byte[] bytes, int offset, ushort value)
            {
                bytes[offset] = (byte)(value & 0xff);
                bytes[offset + 1] = (byte)((value >> 8) & 0xff);
            }

            static void WriteUInt32(byte[] bytes, int offset, uint value)
            {
                bytes[offset] = (byte)(value & 0xff);
                bytes[offset + 1] = (byte)((value >> 8) & 0xff);
                bytes[offset + 2] = (byte)((value >> 16) & 0xff);
                bytes[offset + 3] = (byte)((value >> 24) & 0xff);
            }
        }

        static class ManagedSha256
        {
            static readonly uint[] K =
            {
                0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
                0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
                0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
                0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
                0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
                0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
                0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
                0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
                0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
                0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
                0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
                0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
                0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
                0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
                0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
                0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
            };

            public static byte[] ComputeHash(byte[] bytes)
            {
                if (bytes == null)
                    throw new InvalidOperationException("Cannot hash null bytes.");

                var bitLength = (ulong)bytes.Length * 8UL;
                var paddedLength = bytes.Length + 1 + 8;
                while ((paddedLength % 64) != 0)
                    paddedLength++;

                var padded = new byte[paddedLength];
                Array.Copy(bytes, padded, bytes.Length);
                padded[bytes.Length] = 0x80;

                for (var i = 0; i < 8; i++)
                    padded[paddedLength - 1 - i] = (byte)((bitLength >> (8 * i)) & 0xff);

                var h0 = 0x6a09e667u;
                var h1 = 0xbb67ae85u;
                var h2 = 0x3c6ef372u;
                var h3 = 0xa54ff53au;
                var h4 = 0x510e527fu;
                var h5 = 0x9b05688cu;
                var h6 = 0x1f83d9abu;
                var h7 = 0x5be0cd19u;
                var w = new uint[64];

                for (var chunk = 0; chunk < padded.Length; chunk += 64)
                {
                    for (var i = 0; i < 16; i++)
                    {
                        var offset = chunk + i * 4;
                        w[i] = ((uint)padded[offset] << 24) |
                               ((uint)padded[offset + 1] << 16) |
                               ((uint)padded[offset + 2] << 8) |
                               padded[offset + 3];
                    }

                    for (var i = 16; i < 64; i++)
                    {
                        var s0 = RotateRight(w[i - 15], 7) ^ RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
                        var s1 = RotateRight(w[i - 2], 17) ^ RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
                        w[i] = unchecked(w[i - 16] + s0 + w[i - 7] + s1);
                    }

                    var a = h0;
                    var b = h1;
                    var c = h2;
                    var d = h3;
                    var e = h4;
                    var f = h5;
                    var g = h6;
                    var h = h7;

                    for (var i = 0; i < 64; i++)
                    {
                        var s1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                        var ch = (e & f) ^ (~e & g);
                        var temp1 = unchecked(h + s1 + ch + K[i] + w[i]);
                        var s0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                        var maj = (a & b) ^ (a & c) ^ (b & c);
                        var temp2 = unchecked(s0 + maj);

                        h = g;
                        g = f;
                        f = e;
                        e = unchecked(d + temp1);
                        d = c;
                        c = b;
                        b = a;
                        a = unchecked(temp1 + temp2);
                    }

                    h0 = unchecked(h0 + a);
                    h1 = unchecked(h1 + b);
                    h2 = unchecked(h2 + c);
                    h3 = unchecked(h3 + d);
                    h4 = unchecked(h4 + e);
                    h5 = unchecked(h5 + f);
                    h6 = unchecked(h6 + g);
                    h7 = unchecked(h7 + h);
                }

                var hash = new byte[32];
                WriteUInt32BigEndian(hash, 0, h0);
                WriteUInt32BigEndian(hash, 4, h1);
                WriteUInt32BigEndian(hash, 8, h2);
                WriteUInt32BigEndian(hash, 12, h3);
                WriteUInt32BigEndian(hash, 16, h4);
                WriteUInt32BigEndian(hash, 20, h5);
                WriteUInt32BigEndian(hash, 24, h6);
                WriteUInt32BigEndian(hash, 28, h7);
                return hash;
            }

            static uint RotateRight(uint value, int bits)
            {
                return (value >> bits) | (value << (32 - bits));
            }

            static void WriteUInt32BigEndian(byte[] bytes, int offset, uint value)
            {
                bytes[offset] = (byte)((value >> 24) & 0xff);
                bytes[offset + 1] = (byte)((value >> 16) & 0xff);
                bytes[offset + 2] = (byte)((value >> 8) & 0xff);
                bytes[offset + 3] = (byte)(value & 0xff);
            }
        }
    }
}
#endif
