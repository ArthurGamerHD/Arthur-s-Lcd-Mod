// ReSharper disable RedundantUsingDirective
using System.IO;
using LcdMod.Client.Audio.Xwma;
using Xunit;

namespace Arthur_s_Lcd_Mod.Tests
{
    public sealed class XwmaPacketReaderTests
    {
        [Fact]
        public void ReadsFirst48000HzPacketHeader()
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "Spazzmatica_Polka.xwm");

            using (Stream stream = File.OpenRead(path))
            {
                XwmaFileInfo file = XwmaParser.Parse(stream);
                Wma2DecoderProfile profile = Wma2DecoderProfile.FromFile(file);
                XwmaPacketReader packets =
                    new XwmaPacketReader(stream, file, profile);

                XwmaPacketHeader header;
                Assert.True(packets.ReadNext(out header));
                Assert.Equal(0, header.PacketNumber);
                Assert.Equal(0, header.SuperframeIndex);
                Assert.Equal(4, header.FrameCountField);
                Assert.Equal(0, header.ReservoirBitOffset);
                Assert.Equal(20, header.FrameDataBitOffset);
                Assert.Equal(18432u, header.DecodedBytesThisPacket);
            }
        }

        [Fact]
        public void ReadsFirst44100HzPacketHeader()
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "Mus_victory_KA_1.xwm");

            using (Stream stream = File.OpenRead(path))
            {
                XwmaFileInfo file = XwmaParser.Parse(stream);
                Wma2DecoderProfile profile = Wma2DecoderProfile.FromFile(file);
                XwmaPacketReader packets =
                    new XwmaPacketReader(stream, file, profile);

                XwmaPacketHeader header;
                Assert.True(packets.ReadNext(out header));
                Assert.Equal(0, header.SuperframeIndex);
                Assert.Equal(7, header.FrameCountField);
                Assert.Equal(0, header.ReservoirBitOffset);
                Assert.Equal(20, header.FrameDataBitOffset);
            }
        }

        [Fact]
        public void AllowsFinalPacketWithNoDpdsGrowth()
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "Mus_victory_KA_1.xwm");

            using (Stream stream = File.OpenRead(path))
            {
                XwmaFileInfo file = XwmaParser.Parse(stream);
                Wma2DecoderProfile profile = Wma2DecoderProfile.FromFile(file);
                XwmaPacketReader packets =
                    new XwmaPacketReader(stream, file, profile);

                XwmaPacketHeader header;
                XwmaPacketHeader lastHeader = null;
                while (packets.ReadNext(out header))
                {
                    lastHeader = header;
                }

                Assert.NotNull(lastHeader);
                Assert.Equal(197, lastHeader.PacketNumber);
                Assert.Equal(5, lastHeader.SuperframeIndex);
                Assert.Equal(0, lastHeader.FrameCountField);
                Assert.Equal(816, lastHeader.ReservoirBitOffset);
                Assert.True(lastHeader.DeclaresNoAdditionalPcm);
            }
        }
    }
}
