using System.Collections.Generic;
using InvalidDataException = LcdMod.Common.InvalidDataException;

namespace LcdMod.Common.Compression
{
    public interface IHuffmanBitReader
    {
        bool ReadBit();
    }

    public sealed class HuffmanTree<TSymbol>
    {
        sealed class Node
        {
            public int Zero = -1;
            public int One = -1;
            public bool HasSymbol;
            public TSymbol Symbol;
        }

        readonly Node[] _nodes;

        HuffmanTree()
        {
            _nodes = new Node[0];
        }

        public HuffmanTree(
            uint[] codes,
            byte[] bitCounts,
            TSymbol[] symbols)
        {
            if (codes == null || bitCounts == null || symbols == null ||
                codes.Length == 0 ||
                codes.Length != bitCounts.Length ||
                codes.Length != symbols.Length)
            {
                throw new InvalidDataException("Invalid Huffman tree.");
            }

            var nodes = new List<Node> { new Node() };

            for (int symbolIndex = 0; symbolIndex < codes.Length; symbolIndex++)
            {
                int bitCount = bitCounts[symbolIndex];
                uint code = codes[symbolIndex];

                if (bitCount <= 0 || bitCount > 32)
                    throw new InvalidDataException("Invalid Huffman code length.");

                if (bitCount < 32 && code >= (1u << bitCount))
                {
                    throw new InvalidDataException(
                        "A Huffman code exceeds its declared length.");
                }

                int nodeIndex = 0;
                for (int bitIndex = bitCount - 1; bitIndex >= 0; bitIndex--)
                {
                    Node node = nodes[nodeIndex];
                    if (node.HasSymbol)
                    {
                        throw new InvalidDataException(
                            "A Huffman code extends an existing leaf.");
                    }

                    bool one = ((code >> bitIndex) & 1u) != 0;
                    int childIndex = one ? node.One : node.Zero;
                    if (childIndex < 0)
                    {
                        childIndex = nodes.Count;
                        nodes.Add(new Node());
                        if (one)
                            node.One = childIndex;
                        else
                            node.Zero = childIndex;
                    }

                    nodeIndex = childIndex;
                }

                Node leaf = nodes[nodeIndex];
                if (leaf.HasSymbol || leaf.Zero >= 0 || leaf.One >= 0)
                {
                    throw new InvalidDataException(
                        "The Huffman tree contains a duplicate or prefix code.");
                }

                leaf.HasSymbol = true;
                leaf.Symbol = symbols[symbolIndex];
            }

            _nodes = nodes.ToArray();
        }

        public bool IsEmpty
        {
            get { return _nodes.Length == 0; }
        }

        internal static HuffmanTree<TSymbol> CreateEmpty()
        {
            return new HuffmanTree<TSymbol>();
        }

        public TSymbol Decode(IHuffmanBitReader bits)
        {
            if (IsEmpty)
                throw new InvalidDataException("Attempted to decode an empty Huffman tree.");

            if (bits == null)
                throw new InvalidDataException("Missing Huffman bit reader.");

            int nodeIndex = 0;
            for (;;)
            {
                Node node = _nodes[nodeIndex];
                if (node.HasSymbol)
                    return node.Symbol;

                nodeIndex = bits.ReadBit() ? node.One : node.Zero;
                if (nodeIndex < 0 || nodeIndex >= _nodes.Length)
                    throw new InvalidDataException("Invalid Huffman code.");
            }
        }
    }

    public static class HuffmanTree
    {
        public static HuffmanTree<int> CreateIndexed(
            uint[] codes,
            byte[] bitCounts)
        {
            if (codes == null)
                throw new InvalidDataException("Missing Huffman codes.");

            var symbols = new int[codes.Length];
            for (int i = 0; i < symbols.Length; i++)
                symbols[i] = i;

            return new HuffmanTree<int>(codes, bitCounts, symbols);
        }

        public static HuffmanTree<int> CreateCanonicalIndexed(
            int[] bitCounts,
            int maxBitCount = 32,
            bool allowEmpty = false)
        {
            if (bitCounts == null)
                throw new InvalidDataException("Missing Huffman code lengths.");

            if (maxBitCount <= 0 || maxBitCount > 32)
                throw new InvalidDataException("Invalid Huffman maximum code length.");

            int[] counts = new int[maxBitCount + 1];
            int symbolCount = 0;
            int maxObservedBits = 0;

            for (int i = 0; i < bitCounts.Length; i++)
            {
                int bitCount = bitCounts[i];
                if (bitCount < 0 || bitCount > maxBitCount)
                    throw new InvalidDataException("Invalid Huffman code length.");

                if (bitCount == 0)
                    continue;

                counts[bitCount]++;
                symbolCount++;
                if (bitCount > maxObservedBits)
                    maxObservedBits = bitCount;
            }

            if (symbolCount == 0)
            {
                if (!allowEmpty)
                    throw new InvalidDataException("Empty Huffman tree.");

                return HuffmanTree<int>.CreateEmpty();
            }

            long codesRemaining = 1;
            for (int bits = 1; bits <= maxObservedBits; bits++)
            {
                codesRemaining = (codesRemaining << 1) - counts[bits];
                if (codesRemaining < 0)
                    throw new InvalidDataException("Oversubscribed Huffman tree.");
            }

            ulong[] nextCodes = new ulong[maxBitCount + 1];
            ulong code = 0;

            for (int bits = 1; bits <= maxObservedBits; bits++)
            {
                code = (code + (ulong)counts[bits - 1]) << 1;
                nextCodes[bits] = code;
            }

            uint[] codes = new uint[symbolCount];
            byte[] canonicalBitCounts = new byte[symbolCount];
            int[] symbols = new int[symbolCount];
            int entry = 0;

            for (int symbol = 0; symbol < bitCounts.Length; symbol++)
            {
                int bitCount = bitCounts[symbol];
                if (bitCount == 0)
                    continue;

                ulong nextCode = nextCodes[bitCount]++;
                if (nextCode > uint.MaxValue)
                    throw new InvalidDataException("A Huffman code is too large.");

                codes[entry] = (uint)nextCode;
                canonicalBitCounts[entry] = (byte)bitCount;
                symbols[entry] = symbol;
                entry++;
            }

            return new HuffmanTree<int>(codes, canonicalBitCounts, symbols);
        }
    }
}
