using System;
using System.IO;

namespace LcdMod.Client.Audio.Xwma
{
    /// <summary>
    /// Writes a conventional 44-byte RIFF/PCM WAVE header followed by the
    /// samples contained in PcmWaveData. The writer deliberately uses a
    /// Try-style API so malformed input can be rejected without creating
    /// parser-specific exception dependencies.
    /// </summary>
    internal static class PcmWaveWriter
    {
        private const uint FORMAT_CHUNK_SIZE = 16;
        private const uint WAVE_HEADER_SIZE = 44;

        public static bool TryWriteFile(
            Stream stream,
            PcmWaveData data,
            out string failureReason)
        {
            failureReason = string.Empty;
            
            try
            {
                return TryWrite(stream, data, out failureReason);
            }
            catch (Exception error)
            {
                failureReason = error.Message;
                return false;
            }
        }

        public static bool TryWrite(
            Stream stream,
            PcmWaveData data,
            out string failureReason)
        {
            failureReason = string.Empty;

            if (stream == null || !stream.CanWrite)
            {
                failureReason = "WAV output stream must be writable.";
                return false;
            }

            if (!Validate(data, out failureReason))
                return false;

            uint sampleBytes = (uint)data.Samples.Length;
            uint riffSize = 36u + sampleBytes;
            uint averageBytesPerSecond =
                PcmAudioFormat.REQUIRED_SAMPLE_RATE * data.BlockAlign;

            try
            {
                WriteFourCc(stream, 'R', 'I', 'F', 'F');
                WriteUInt32LittleEndian(stream, riffSize);
                WriteFourCc(stream, 'W', 'A', 'V', 'E');

                WriteFourCc(stream, 'f', 'm', 't', ' ');
                WriteUInt32LittleEndian(stream, FORMAT_CHUNK_SIZE);
                WriteUInt16LittleEndian(stream, PcmAudioFormat.WAVE_FORMAT_PCM);
                WriteUInt16LittleEndian(stream, data.Channels);
                WriteUInt32LittleEndian(stream, data.SampleRate);
                WriteUInt32LittleEndian(stream, averageBytesPerSecond);
                WriteUInt16LittleEndian(stream, data.BlockAlign);
                WriteUInt16LittleEndian(stream, data.BitsPerSample);

                WriteFourCc(stream, 'd', 'a', 't', 'a');
                WriteUInt32LittleEndian(stream, sampleBytes);
                stream.Write(data.Samples, 0, data.Samples.Length);
                return true;
            }
            catch (Exception error)
            {
                failureReason = error.Message;
                return false;
            }
        }

        private static bool Validate(
            PcmWaveData data,
            out string failureReason)
        {
            failureReason = string.Empty;

            if (data == null)
            {
                failureReason = "Missing PCM data.";
                return false;
            }

            if (data.Samples == null)
            {
                failureReason = "Missing PCM sample buffer.";
                return false;
            }

            if (data.Samples.Length == 0)
            {
                failureReason = "PCM sample buffer is empty.";
                return false;
            }

            if (data.Samples.Length > PcmAudioFormat.MAXIMUM_PCM_BYTES)
            {
                failureReason = "PCM payload exceeds the 32 MiB limit.";
                return false;
            }

            if (data.SampleRate != PcmAudioFormat.REQUIRED_SAMPLE_RATE)
            {
                failureReason = "Expected 24000 Hz PCM.";
                return false;
            }

            if (data.BitsPerSample != PcmAudioFormat.REQUIRED_BITS_PER_SAMPLE)
            {
                failureReason = "Expected 16-bit PCM.";
                return false;
            }

            ushort requiredBlockAlign;

            if (data.Channels == PcmAudioFormat.REQUIRED_MONO_CHANNELS)
            {
                requiredBlockAlign = PcmAudioFormat.REQUIRED_MONO_BLOCK_ALIGN;
            }
            else if (data.Channels == PcmAudioFormat.SUPPORTED_STEREO_CHANNELS)
            {
                requiredBlockAlign = PcmAudioFormat.REQUIRED_STEREO_BLOCK_ALIGN;
            }
            else
            {
                failureReason = "Expected mono or stereo PCM.";
                return false;
            }

            if (data.BlockAlign != requiredBlockAlign)
            {
                failureReason = "PCM block alignment does not match the channel count.";
                return false;
            }

            if ((data.Samples.Length % data.BlockAlign) != 0)
            {
                failureReason = "PCM payload is not sample-frame aligned.";
                return false;
            }

            // standard RIFF/WAVE file uses unsigned 32-bit chunk sizes.
            // Check if MaximumPcmBytes is below that limit, so we don't need to catch
            // System.OverflowException (is not whitelisted)
            if ((long)data.Samples.Length + WAVE_HEADER_SIZE > uint.MaxValue)
            {
                failureReason = "PCM payload is too large for a RIFF/WAVE file.";
                return false;
            }

            return true;
        }

        private static void WriteFourCc(
            Stream stream,
            char c0,
            char c1,
            char c2,
            char c3)
        {
            stream.WriteByte((byte)c0);
            stream.WriteByte((byte)c1);
            stream.WriteByte((byte)c2);
            stream.WriteByte((byte)c3);
        }

        private static void WriteUInt16LittleEndian(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        private static void WriteUInt32LittleEndian(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }
    }
}
