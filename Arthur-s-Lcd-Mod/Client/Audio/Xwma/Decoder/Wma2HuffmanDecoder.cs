using LcdMod.Common.Compression;
using InvalidDataException = LcdMod.Common.Exceptions.InvalidDataException;

namespace LcdMod.Client.Audio.Xwma.Decoder
{
    internal sealed class Wma2RunLevelCodebook
    {
        Wma2RunLevelCodebook(
            HuffmanTree<int> huffman,
            ushort[] runs,
            ushort[] levels)
        {
            Huffman = huffman;
            Runs = runs;
            Levels = levels;
        }

        public HuffmanTree<int> Huffman { get; private set; }
        public ushort[] Runs { get; private set; }
        public ushort[] Levels { get; private set; }

        public static Wma2RunLevelCodebook Create(
            uint[] codes,
            byte[] bitCounts,
            ushort[] levelCounts)
        {
            if (codes == null || bitCounts == null ||
                levelCounts == null ||
                codes.Length != bitCounts.Length ||
                codes.Length < 3)
            {
                throw new InvalidDataException(
                    "Invalid WMAv2 run/level codebook.");
            }

            ushort[] runs = new ushort[codes.Length];
            ushort[] levels = new ushort[codes.Length];
            int symbol = 2;
            int level = 1;

            for (int i = 0;
                 i < levelCounts.Length && symbol < codes.Length;
                 i++, level++)
            {
                int count = levelCounts[i];

                for (int run = 0;
                     run < count && symbol < codes.Length;
                     run++, symbol++)
                {
                    runs[symbol] = (ushort)run;
                    levels[symbol] = (ushort)level;
                }
            }

            if (symbol != codes.Length)
            {
                throw new InvalidDataException(
                    "The WMAv2 level counts do not cover the codebook.");
            }

            return new Wma2RunLevelCodebook(
                HuffmanTree.CreateIndexed(codes, bitCounts),
                runs,
                levels);
        }
    }
}
