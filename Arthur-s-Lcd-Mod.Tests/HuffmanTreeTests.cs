using LcdMod.Common.Compression;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class HuffmanTreeTests
{
    [Fact]
    public void Decode_ReturnsGenericSymbol()
    {
        var tree = new HuffmanTree<string>(
            new uint[] { 0b0, 0b10, 0b11 },
            new byte[] { 1, 2, 2 },
            new[] { "zero", "ten", "eleven" });
        var bits = new TestBitReader(true, true);

        Assert.Equal("eleven", tree.Decode(bits));
    }

    sealed class TestBitReader : IHuffmanBitReader
    {
        readonly bool[] _bits;
        int _position;

        public TestBitReader(params bool[] bits)
        {
            _bits = bits;
        }

        public bool ReadBit()
        {
            return _bits[_position++];
        }
    }
}
