// ReSharper disable RedundantUsingDirective
using System;
using System.IO;

namespace LcdMod.Client.Audio.Xwma.Decoder
{

    internal static class XwmaPcmDecoder
    {
        public static bool TryDecode(
            Stream xwmaStream,
            out PcmWaveData pcm,
            out string failureReason)
        {
            pcm = null;
            failureReason = string.Empty;

            if (xwmaStream == null)
            {
                failureReason = "Missing xWMA input stream.";
                return false;
            }

            if (!xwmaStream.CanRead || !xwmaStream.CanSeek)
            {
                failureReason = "The xWMA input stream must be readable and seekable.";
                return false;
            }

            try
            {
                XwmaFileInfo file = XwmaParser.Parse(xwmaStream);
                Wma2DecoderProfile profile =
                    Wma2DecoderProfile.FromFile(file);
                XwmaPacketReader packets =
                    new XwmaPacketReader(xwmaStream, file, profile);
                Wma2DecoderCore decoder =
                    new Wma2DecoderCore(file, profile);
                pcm = decoder.Decode(packets);
                return true;
            }
            catch (Exception error)
            {
                failureReason = string.IsNullOrEmpty(error.Message)
                    ? "The xWMA stream could not be decoded."
                    : error.Message;
                pcm = null;
                return false;
            }
        }

        public static bool TryDecodeFile(
            Stream stream,
            out PcmWaveData pcm,
            out string failureReason)
        {
            pcm = null;
            failureReason = string.Empty;

            try
            {
                return TryDecode(stream, out pcm, out failureReason);
            }
            catch (Exception error)
            {
                failureReason = string.IsNullOrEmpty(error.Message)
                    ? "The xWMA file could not be opened."
                    : error.Message;
                pcm = null;
                return false;
            }
        }

        public static bool TryDecodeToWaveFile(
            Stream xwmaStream,
            Stream outputWave,
            out string failureReason)
        {
            PcmWaveData pcm;

            if (!TryDecode(
                xwmaStream,
                out pcm,
                out failureReason))
            {
                return false;
            }

            return PcmWaveWriter.TryWriteFile(
                outputWave,
                pcm,
                out failureReason);
        }
    }
}
