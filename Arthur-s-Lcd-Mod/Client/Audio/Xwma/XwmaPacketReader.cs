// ReSharper disable RedundantUsingDirective
using System.IO;
using LcdMod.Client.Audio.Xwma.Decoder;
using EndOfStreamException = LcdMod.Common.Exceptions.EndOfStreamException;
using InvalidDataException = LcdMod.Common.Exceptions.InvalidDataException;

namespace LcdMod.Client.Audio.Xwma
{
    public sealed class XwmaPacketHeader
    {
        internal XwmaPacketHeader(
            int packetNumber,
            int superframeIndex,
            int frameCountField,
            int reservoirBitOffset,
            int frameDataBitOffset,
            uint decodedCumulativeBytes,
            uint decodedBytesThisPacket)
        {
            PacketNumber = packetNumber;
            SuperframeIndex = superframeIndex;
            FrameCountField = frameCountField;
            ReservoirBitOffset = reservoirBitOffset;
            FrameDataBitOffset = frameDataBitOffset;
            DecodedCumulativeBytes = decodedCumulativeBytes;
            DecodedBytesThisPacket = decodedBytesThisPacket;
        }

        /// <summary>Zero-based packet number inside the xWMA data chunk.</summary>
        public int PacketNumber { get; private set; }

        /// <summary>Four-bit rolling superframe index. It is diagnostic only.</summary>
        public int SuperframeIndex { get; private set; }

        /// <summary>
        /// Raw four-bit frame-count field. This is not always the number of
        /// newly completed frames because the first frame may continue the
        /// previous packet's bit reservoir.
        /// </summary>
        public int FrameCountField { get; private set; }

        /// <summary>
        /// Number of bits immediately after the header that belong to the
        /// frame started in the previous packet.
        /// </summary>
        public int ReservoirBitOffset { get; private set; }

        /// <summary>
        /// Bit position, from the start of this packet, at which frames that
        /// begin in this packet start.
        /// </summary>
        public int FrameDataBitOffset { get; private set; }

        public uint DecodedCumulativeBytes { get; private set; }
        public uint DecodedBytesThisPacket { get; private set; }

        public bool DeclaresNoAdditionalPcm => DecodedBytesThisPacket == 0;
    }

    /// <summary>
    /// Sequentially reads fixed-size WMAv2 packets from a parsed xWMA stream.
    /// The Buffer array is reused by every ReadNext call and must be consumed
    /// before the next packet is read.
    /// </summary>
    public sealed class XwmaPacketReader
    {
        private readonly Stream _stream;
        private readonly XwmaFileInfo _file;
        private readonly Wma2DecoderProfile _profile;
        private readonly byte[] _buffer;
        private readonly MsbBitReader _bits;
        private int _nextPacketNumber;

        public XwmaPacketReader(
            Stream stream,
            XwmaFileInfo file,
            Wma2DecoderProfile profile)
        {
            if (stream == null)
                throw new InvalidDataException("Missing xWMA packet stream.");

            if (file == null)
                throw new InvalidDataException("Missing parsed xWMA file information.");

            if (profile == null)
                throw new InvalidDataException("Missing WMAv2 decoder profile.");

            if (!stream.CanRead || !stream.CanSeek)
                throw new InvalidDataException("The xWMA packet stream must be readable and seekable.");

            if (file.PacketCount <= 0)
                throw new InvalidDataException("The xWMA file contains no packets.");

            if (profile.PacketSize == 0)
                throw new InvalidDataException("The WMAv2 packet size is zero.");

            _stream = stream;
            _file = file;
            _profile = profile;
            _buffer = new byte[profile.PacketSize];
            _bits = new MsbBitReader();

            Reset();
        }

        /// <summary>
        /// Reused compressed-packet storage. Only the first PacketSize bytes
        /// are valid, and the contents change after each ReadNext call.
        /// </summary>
        public byte[] Buffer => _buffer;

        public int PacketSize => _buffer.Length;

        public int NextPacketNumber => _nextPacketNumber;

        public int PacketCount => _file.PacketCount;

        public bool EndOfPackets => _nextPacketNumber >= _file.PacketCount;

        public void Reset()
        {
            _stream.Position = _file.DataOffset;
            _nextPacketNumber = 0;
        }

        public bool ReadNext(out XwmaPacketHeader header)
        {
            header = null;

            if (EndOfPackets)
                return false;

            ReadExactly(_stream, _buffer, 0, _buffer.Length);
            _bits.Reset(_buffer, 0, _buffer.Length);

            int superframeIndex = (int)_bits.ReadBits(4);
            int frameCountField = (int)_bits.ReadBits(4);
            int reservoirBitOffset =
                (int)_bits.ReadBits(_profile.ReservoirBitOffsetFieldBits);

            int frameDataBitOffset =
                _profile.SuperframeHeaderBits + reservoirBitOffset;

            if (frameDataBitOffset > _buffer.Length * 8)
            {
                throw new InvalidDataException(
                    "Packet " + _nextPacketNumber +
                    " has a reservoir offset beyond the packet boundary.");
            }

            uint cumulative =
                _file.DecodedPacketCumulativeBytes[_nextPacketNumber];
            uint previous = _nextPacketNumber == 0
                ? 0u
                : _file.DecodedPacketCumulativeBytes[_nextPacketNumber - 1];

            if (cumulative < previous)
            {
                throw new InvalidDataException(
                    "The dpds table decreases at packet " +
                    _nextPacketNumber + ".");
            }

            header = new XwmaPacketHeader(
                _nextPacketNumber,
                superframeIndex,
                frameCountField,
                reservoirBitOffset,
                frameDataBitOffset,
                cumulative,
                cumulative - previous);

            _nextPacketNumber++;
            return true;
        }

        private static void ReadExactly(
            Stream stream,
            byte[] buffer,
            int offset,
            int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of the xWMA data chunk.");

                offset += read;
                count -= read;
            }
        }
    }
}
