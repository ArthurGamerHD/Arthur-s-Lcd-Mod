// ReSharper disable RedundantUsingDirective
using System;
using InvalidDataException = LcdMod.Common.Exceptions.InvalidDataException;

namespace LcdMod.Client.Audio.Xwma.Decoder
{
    /// <summary>
    /// managed WMAv2 decoder for the two xWMA profiles used by space engineers
    /// (if some mod use a different profile, we are screwed)
    /// </summary>
    internal sealed class Wma2DecoderCore
    {
        const int MAX_CHANNEL_COUNT = 2;
        const int FRAME_LENGTH = 2048;
        const int FRAME_LENGTH_BITS = 11;
        const int BLOCK_SIZE_COUNT = 5;
        const int BLOCK_LENGTH_CODE_BITS = 3;
        const int CODEC_DELAY_FRAMES = 2;
        const int MAXIMUM_EXPONENT = 95;
        const int MINIMUM_EXPONENT = -60;

        readonly XwmaFileInfo _file;
        readonly Wma2DecoderProfile _profile;
        readonly int[][] _exponentBands;
        readonly int[] _coefficientEnds;
        readonly float[][] _windows;
        readonly Wma2FastImdct[] _transforms;
        readonly float[][] _exponents;
        readonly bool[] _exponentsInitialized;
        readonly int[] _exponentBlockSizeIndexes;
        readonly float[] _maximumExponents;
        readonly int[][] _quantizedCoefficients;
        readonly float[][] _spectralCoefficients;
        readonly float[][] _transformOutput;
        readonly float[][] _frameOutput;
        readonly bool[] _channelCoded;
        readonly MsbBitReader _bits;
        readonly Wma2Pcm24KMonoSink _pcmSink;

        byte[] _reservoir;
        int _reservoirBitCount;
        bool _resetBlockLengths;
        int _previousBlockLengthBits;
        int _blockLengthBits;
        int _nextBlockLengthBits;
        int _blockPosition;
        bool _midSideStereo;
        int _decodedFrameCount;

        public Wma2DecoderCore(
            XwmaFileInfo file,
            Wma2DecoderProfile profile)
        {
            if (file == null)
                throw new InvalidDataException("Missing parsed xWMA file information.");

            if (profile == null)
                throw new InvalidDataException("Missing WMAv2 decoder profile.");

            if ((profile.Channels < 1 || profile.Channels > MAX_CHANNEL_COUNT) ||
                profile.FrameLength != FRAME_LENGTH ||
                profile.FrameLengthBits != FRAME_LENGTH_BITS ||
                profile.BlockSizeCount != BLOCK_SIZE_COUNT ||
                profile.UsesNoiseCoding ||
                !profile.UsesExponentVlc ||
                !profile.UsesVariableBlockLength ||
                !profile.UsesBitReservoir)
            {
                throw new InvalidDataException(
                    "The WMAv2 decoder profile is outside the restricted implementation.");
            }

            _file = file;
            _profile = profile;
            _exponentBands = BuildExponentBands(profile.SampleRate);
            _coefficientEnds = new int[BLOCK_SIZE_COUNT];
            _windows = new float[BLOCK_SIZE_COUNT][];
            _transforms = new Wma2FastImdct[BLOCK_SIZE_COUNT];

            for (int blockSizeIndex = 0;
                 blockSizeIndex < BLOCK_SIZE_COUNT;
                 blockSizeIndex++)
            {
                int blockLength = FRAME_LENGTH >> blockSizeIndex;
                _coefficientEnds[blockSizeIndex] =
                    (FRAME_LENGTH - (FRAME_LENGTH * 9) / 100) >> blockSizeIndex;
                _windows[blockSizeIndex] = BuildSineWindow(blockLength);
                _transforms[blockSizeIndex] =
                    new Wma2FastImdct(blockLength);
            }

            _exponents = CreateFloatChannels(profile.Channels, FRAME_LENGTH);
            _exponentsInitialized = new bool[profile.Channels];
            _exponentBlockSizeIndexes = new int[profile.Channels];
            _maximumExponents = new float[profile.Channels];
            _quantizedCoefficients = CreateIntChannels(profile.Channels, FRAME_LENGTH);
            _spectralCoefficients = CreateFloatChannels(profile.Channels, FRAME_LENGTH);
            _transformOutput = CreateFloatChannels(profile.Channels, FRAME_LENGTH * 2);
            _frameOutput = CreateFloatChannels(profile.Channels, FRAME_LENGTH * 2);
            _channelCoded = new bool[profile.Channels];
            _bits = new MsbBitReader();
            _pcmSink = new Wma2Pcm24KMonoSink(
                profile.SampleRate,
                profile.Channels,
                file.DeclaredSourceSampleFrames);
            _reservoir = Array.Empty<byte>();

            _resetBlockLengths = true;
            _previousBlockLengthBits = FRAME_LENGTH_BITS;
            _blockLengthBits = FRAME_LENGTH_BITS;
            _nextBlockLengthBits = FRAME_LENGTH_BITS;
        }

        public PcmWaveData Decode(XwmaPacketReader packetReader)
        {
            if (packetReader == null)
                throw new InvalidDataException("Missing xWMA packet reader.");

            packetReader.Reset();
            XwmaPacketHeader header;

            while (packetReader.ReadNext(out header))
                DecodePacket(packetReader.Buffer, header);

            // WMAv2 keeps one final overlap frame in frameOutput. The codec has
            // a two-frame priming delay, so the first two reconstructed frame
            // buffers were intentionally suppressed while decoding.
            if (_decodedFrameCount >= CODEC_DELAY_FRAMES)
            {
                AppendCurrentFrame();
            }

            PcmWaveData result = _pcmSink.Complete();
            ValidateDecodedLength(_pcmSink.SourceFrameCount);
            return result;
        }

        void DecodePacket(
            byte[] packet,
            XwmaPacketHeader header)
        {
            if (packet == null || header == null)
                throw new InvalidDataException("Missing WMAv2 packet data.");

            int packetBitCount = packet.Length * 8;

            // A zero frame-count packet extends the previously saved reservoir
            // but does not complete or emit a frame. The data starts after the
            // first header byte, matching the WMAv2 reservoir convention.
            if (header.FrameCountField == 0)
            {
                if (_reservoirBitCount <= 0)
                {
                    throw new InvalidDataException(
                        "A reservoir-only WMAv2 packet has no saved frame.");
                }

                int appendedBits = packetBitCount - 8;
                _reservoir = Wma2BitPacking.Concatenate(
                    _reservoir,
                    _reservoirBitCount,
                    packet,
                    8,
                    appendedBits);
                _reservoirBitCount += appendedBits;
                return;
            }

            if (_reservoirBitCount > 0)
            {
                byte[] completedFrame = Wma2BitPacking.Concatenate(
                    _reservoir,
                    _reservoirBitCount,
                    packet,
                    _profile.SuperframeHeaderBits,
                    header.ReservoirBitOffset);

                _bits.ResetBits(
                    completedFrame,
                    0,
                    _reservoirBitCount + header.ReservoirBitOffset);
                DecodeFrame();
            }

            int currentFrameCount = header.FrameCountField - 1;
            int currentStartBit = header.FrameDataBitOffset;

            if (currentStartBit < 0 || currentStartBit > packetBitCount)
            {
                throw new InvalidDataException(
                    "The WMAv2 frame-data offset is outside its packet.");
            }

            _bits.ResetBits(
                packet,
                currentStartBit,
                packetBitCount - currentStartBit);
            _resetBlockLengths = true;

            for (int frame = 0; frame < currentFrameCount; frame++)
                DecodeFrame();

            int remainingStartBit = currentStartBit + _bits.PositionBits;
            int remainingBits = packetBitCount - remainingStartBit;

            _reservoir = Wma2BitPacking.CopyBits(
                packet,
                remainingStartBit,
                remainingBits);
            _reservoirBitCount = remainingBits;
        }

        void DecodeFrame()
        {
            _blockPosition = 0;

            do
            {
                DecodeBlock();
            }
            while (_blockPosition < FRAME_LENGTH);

            if (_decodedFrameCount >= CODEC_DELAY_FRAMES)
            {
                AppendCurrentFrame();
            }

            for (int channel = 0; channel < _profile.Channels; channel++)
            {
                Array.Copy(
                    _frameOutput[channel],
                    FRAME_LENGTH,
                    _frameOutput[channel],
                    0,
                    FRAME_LENGTH);
            }

            _decodedFrameCount++;
        }

        void DecodeBlock()
        {
            ReadBlockLengths();

            int blockLength = 1 << _blockLengthBits;
            int blockSizeIndex = FRAME_LENGTH_BITS - _blockLengthBits;

            if (blockSizeIndex < 0 || blockSizeIndex >= BLOCK_SIZE_COUNT)
                throw new InvalidDataException("Invalid WMAv2 block size.");

            if (_blockPosition > FRAME_LENGTH - blockLength)
                throw new InvalidDataException("A WMAv2 block exceeds its frame.");

            _midSideStereo = _profile.Channels == 2 && _bits.ReadBit();
            bool anyChannelCoded = false;

            for (int channel = 0; channel < _profile.Channels; channel++)
            {
                _channelCoded[channel] = _bits.ReadBit();
                anyChannelCoded |= _channelCoded[channel];
            }

            if (anyChannelCoded)
                DecodeCodedBlock(blockSizeIndex, blockLength);

            SynthesizeBlock(blockSizeIndex, blockLength);
            _blockPosition += blockLength;
        }

        void ReadBlockLengths()
        {
            if (_resetBlockLengths)
            {
                _resetBlockLengths = false;
                _previousBlockLengthBits = ReadBlockLengthBits();
                _blockLengthBits = ReadBlockLengthBits();
            }
            else
            {
                _previousBlockLengthBits = _blockLengthBits;
                _blockLengthBits = _nextBlockLengthBits;
            }

            _nextBlockLengthBits = ReadBlockLengthBits();
        }

        int ReadBlockLengthBits()
        {
            int value = (int)_bits.ReadBits(BLOCK_LENGTH_CODE_BITS);

            if (value < 0 || value >= BLOCK_SIZE_COUNT)
                throw new InvalidDataException("Invalid WMAv2 variable block-length code.");

            return FRAME_LENGTH_BITS - value;
        }

        void DecodeCodedBlock(
            int blockSizeIndex,
            int blockLength)
        {
            int totalGain = 1;
            int gainPart;

            do
            {
                gainPart = (int)_bits.ReadBits(7);

                if (totalGain > int.MaxValue - gainPart)
                    throw new InvalidDataException("WMAv2 total gain is too large.");

                totalGain += gainPart;
            }
            while (gainPart == 127);

            int coefficientBitCount = TotalGainToCoefficientBits(totalGain);
            bool refreshExponents =
                blockLength == FRAME_LENGTH || _bits.ReadBit();

            if (refreshExponents)
            {
                for (int channel = 0; channel < _profile.Channels; channel++)
                {
                    if (_channelCoded[channel])
                    {
                        DecodeExponents(channel, blockSizeIndex, blockLength);
                        _exponentBlockSizeIndexes[channel] = blockSizeIndex;
                        _exponentsInitialized[channel] = true;
                    }
                }
            }

            for (int channel = 0; channel < _profile.Channels; channel++)
            {
                if (_channelCoded[channel] &&
                    !_exponentsInitialized[channel])
                {
                    throw new InvalidDataException(
                        "WMAv2 attempted to reuse uninitialized exponents.");
                }
            }

            int coefficientCount = _coefficientEnds[blockSizeIndex];

            for (int channel = 0; channel < _profile.Channels; channel++)
            {
                if (!_channelCoded[channel])
                    continue;

                Array.Clear(
                    _quantizedCoefficients[channel],
                    0,
                    blockLength);

                Wma2RunLevelCodebook codebook =
                    channel == 1 && _midSideStereo
                    ? Wma2Codebooks.MidSideCoefficients
                    : Wma2Codebooks.NormalCoefficients;

                DecodeCoefficients(
                    codebook,
                    _quantizedCoefficients[channel],
                    coefficientCount,
                    blockLength,
                    coefficientBitCount);
            }

            Dequantize(blockSizeIndex, blockLength, totalGain);

            if (_profile.Channels == 2 && _midSideStereo && _channelCoded[1])
            {
                if (!_channelCoded[0])
                {
                    Array.Clear(_spectralCoefficients[0], 0, blockLength);
                    _channelCoded[0] = true;
                }

                for (int i = 0; i < blockLength; i++)
                {
                    float middle = _spectralCoefficients[0][i];
                    float side = _spectralCoefficients[1][i];
                    _spectralCoefficients[0][i] = middle + side;
                    _spectralCoefficients[1][i] = middle - side;
                }
            }
        }

        void DecodeExponents(
            int channel,
            int blockSizeIndex,
            int blockLength)
        {
            int[] bands = _exponentBands[blockSizeIndex];
            float[] destination = _exponents[channel];
            int position = 0;
            int lastExponent = 36;
            float maximum = 0.0f;

            for (int band = 0; band < bands.Length; band++)
            {
                int symbol = Wma2Codebooks.ScaleFactors.Decode(_bits);
                lastExponent += symbol - 60;

                if (lastExponent < MINIMUM_EXPONENT ||
                    lastExponent > MAXIMUM_EXPONENT)
                {
                    throw new InvalidDataException(
                        "A WMAv2 exponent is outside the supported range.");
                }

                float value = (float)Math.Pow(10.0, lastExponent / 16.0);
                if (value > maximum)
                    maximum = value;

                int count = bands[band];
                if (count < 0 || position > blockLength - count)
                {
                    throw new InvalidDataException(
                        "The WMAv2 exponent bands exceed the block length.");
                }

                for (int i = 0; i < count; i++)
                    destination[position++] = value;
            }

            if (position != blockLength || maximum <= 0.0f)
            {
                throw new InvalidDataException(
                    "Invalid WMAv2 exponent-band coverage.");
            }

            _maximumExponents[channel] = maximum;
        }

        void DecodeCoefficients(
            Wma2RunLevelCodebook codebook,
            int[] destination,
            int coefficientCount,
            int blockLength,
            int escapedLevelBitCount)
        {
            int position = 0;

            while (position < coefficientCount)
            {
                int symbol = codebook.Huffman.Decode(_bits);

                if (symbol == 1)
                    return;

                int level;
                int run;

                if (symbol == 0)
                {
                    level = (int)_bits.ReadBits(escapedLevelBitCount);
                    run = (int)_bits.ReadBits(FRAME_LENGTH_BITS);
                }
                else
                {
                    if (symbol < 0 || symbol >= codebook.Runs.Length)
                        throw new InvalidDataException("Invalid WMAv2 coefficient symbol.");

                    run = codebook.Runs[symbol];
                    level = codebook.Levels[symbol];
                }

                if (run < 0 || position > coefficientCount - run)
                {
                    throw new InvalidDataException(
                        "WMAv2 coefficient run exceeds the coded spectrum.");
                }

                position += run;

                // WMAv2 permits the final EOB marker to be omitted when a
                // run reaches the coded coefficient limit exactly.
                if (position >= coefficientCount)
                    return;

                if (position >= blockLength)
                {
                    throw new InvalidDataException(
                        "WMAv2 coefficient position exceeds the block.");
                }

                bool positive = _bits.ReadBit();
                destination[position] = positive ? level : -level;
                position++;
            }
        }

        void Dequantize(
            int blockSizeIndex,
            int blockLength,
            int totalGain)
        {
            float normalization = 2.0f / blockLength;
            int coefficientCount = _coefficientEnds[blockSizeIndex];

            for (int channel = 0; channel < _profile.Channels; channel++)
            {
                if (!_channelCoded[channel])
                    continue;

                float maximumExponent = _maximumExponents[channel];
                if (maximumExponent <= 0.0f)
                    throw new InvalidDataException("Invalid WMAv2 exponent scale.");

                float multiplier =
                    (float)Math.Pow(10.0, totalGain * 0.05) /
                    maximumExponent * normalization;
                int exponentBlockSizeIndex =
                    _exponentBlockSizeIndexes[channel];
                int[] source = _quantizedCoefficients[channel];
                float[] exponents = _exponents[channel];
                float[] destination = _spectralCoefficients[channel];

                for (int i = 0; i < coefficientCount; i++)
                {
                    int exponentIndex =
                        (i << blockSizeIndex) >> exponentBlockSizeIndex;

                    if (exponentIndex < 0 ||
                        exponentIndex >= exponents.Length)
                    {
                        throw new InvalidDataException(
                            "WMAv2 exponent reuse index is outside its buffer.");
                    }

                    destination[i] =
                        source[i] * exponents[exponentIndex] * multiplier;
                }

                Array.Clear(
                    destination,
                    coefficientCount,
                    blockLength - coefficientCount);
            }
        }

        void SynthesizeBlock(
            int blockSizeIndex,
            int blockLength)
        {
            for (int channel = 0; channel < _profile.Channels; channel++)
            {
                if (_channelCoded[channel])
                {
                    _transforms[blockSizeIndex].Transform(
                        _spectralCoefficients[channel],
                        _transformOutput[channel]);
                }
                else if (!(_midSideStereo && channel == 1))
                {
                    Array.Clear(
                        _transformOutput[channel],
                        0,
                        blockLength * 2);
                }
            }

            // When only the middle channel was transmitted, WMAv2 uses the
            // same transform output for both reconstructed channels.
            if (_profile.Channels == 2 && _midSideStereo && !_channelCoded[1])
            {
                Array.Copy(
                    _transformOutput[0],
                    0,
                    _transformOutput[1],
                    0,
                    blockLength * 2);
            }

            int outputIndex =
                FRAME_LENGTH / 2 + _blockPosition - blockLength / 2;

            if (outputIndex < 0 ||
                outputIndex > _frameOutput[0].Length - blockLength * 2)
            {
                throw new InvalidDataException(
                    "WMAv2 overlap-add position is outside the frame buffer.");
            }

            for (int channel = 0; channel < _profile.Channels; channel++)
            {
                ApplyWindow(
                    _transformOutput[channel],
                    _frameOutput[channel],
                    outputIndex,
                    blockLength);
            }
        }

        void ApplyWindow(
            float[] input,
            float[] output,
            int outputOffset,
            int blockLength)
        {
            if (_blockLengthBits <= _previousBlockLengthBits)
            {
                float[] window =
                    _windows[FRAME_LENGTH_BITS - _blockLengthBits];

                for (int i = 0; i < blockLength; i++)
                {
                    output[outputOffset + i] += input[i] * window[i];
                }
            }
            else
            {
                int previousLength = 1 << _previousBlockLengthBits;
                int padding = (blockLength - previousLength) / 2;
                float[] window =
                    _windows[FRAME_LENGTH_BITS - _previousBlockLengthBits];

                for (int i = 0; i < previousLength; i++)
                {
                    output[outputOffset + padding + i] +=
                        input[padding + i] * window[i];
                }

                for (int i = 0; i < padding; i++)
                {
                    output[outputOffset + padding + previousLength + i] =
                        input[padding + previousLength + i];
                }
            }

            int rightOutput = outputOffset + blockLength;
            int rightInput = blockLength;

            if (_blockLengthBits <= _nextBlockLengthBits)
            {
                float[] window =
                    _windows[FRAME_LENGTH_BITS - _blockLengthBits];

                for (int i = 0; i < blockLength; i++)
                {
                    output[rightOutput + i] =
                        input[rightInput + i] *
                        window[blockLength - 1 - i];
                }
            }
            else
            {
                int nextLength = 1 << _nextBlockLengthBits;
                int padding = (blockLength - nextLength) / 2;
                float[] window =
                    _windows[FRAME_LENGTH_BITS - _nextBlockLengthBits];

                for (int i = 0; i < padding; i++)
                    output[rightOutput + i] = input[rightInput + i];

                for (int i = 0; i < nextLength; i++)
                {
                    output[rightOutput + padding + i] =
                        input[rightInput + padding + i] *
                        window[nextLength - 1 - i];
                }

                Array.Clear(
                    output,
                    rightOutput + padding + nextLength,
                    padding);
            }
        }

        void ValidateDecodedLength(int actualSourceFrames)
        {
            long declared = _file.DeclaredSourceSampleFrames;

            if (actualSourceFrames <= 0)
                throw new InvalidDataException("WMAv2 produced no PCM samples.");

            // One known game asset has a dpds final value some frames larger
            // than the complete transform frames. Keep dpds as a sanity bound,
            // not as an instruction to pad decoded PCM.
            long difference = declared - actualSourceFrames;
            if (difference < 0)
                difference = -difference;

            if (difference > FRAME_LENGTH)
            {
                throw new InvalidDataException(
                    "WMAv2 output length differs substantially from dpds.");
            }
        }

        static int TotalGainToCoefficientBits(int totalGain)
        {
            if (totalGain < 15)
                return 13;
            if (totalGain < 32)
                return 12;
            if (totalGain < 40)
                return 11;
            if (totalGain < 45)
                return 10;
            return 9;
        }

        static int[][] BuildExponentBands(uint sampleRate)
        {
            int[][] result = new int[BLOCK_SIZE_COUNT][];

            for (int blockSizeIndex = 0;
                 blockSizeIndex < BLOCK_SIZE_COUNT;
                 blockSizeIndex++)
            {
                int tableIndex =
                    FRAME_LENGTH_BITS - 7 - blockSizeIndex;

                if (tableIndex >= 0 && tableIndex < 3 &&
                    sampleRate >= 44100u)
                {
                    byte[] table =
                        Wma2Codebooks.ExponentBands44100[tableIndex];
                    int count = table[0];
                    int[] bands = new int[count];

                    for (int i = 0; i < count; i++)
                        bands[i] = table[i + 1];

                    result[blockSizeIndex] = bands;
                    continue;
                }

                int blockLength = FRAME_LENGTH >> blockSizeIndex;
                int[] temporary = new int[Wma2Codebooks.CriticalFrequencies.Length];
                int bandCount = 0;
                int previousPosition = 0;

                for (int i = 0;
                     i < Wma2Codebooks.CriticalFrequencies.Length;
                     i++)
                {
                    long frequency = Wma2Codebooks.CriticalFrequencies[i];
                    long numerator =
                        blockLength * 2L * frequency +
                        ((long)sampleRate << 1);
                    int position = (int)(numerator /
                        (4L * sampleRate));
                    position <<= 2;

                    if (position > blockLength)
                        position = blockLength;

                    if (position > previousPosition)
                        temporary[bandCount++] = position - previousPosition;

                    if (position >= blockLength)
                        break;

                    previousPosition = position;
                }

                if (bandCount == 0)
                    throw new InvalidDataException("No WMAv2 exponent bands were generated.");

                int[] generated = new int[bandCount];
                Array.Copy(temporary, 0, generated, 0, bandCount);
                result[blockSizeIndex] = generated;
            }

            return result;
        }

        static float[] BuildSineWindow(int blockLength)
        {
            float[] window = new float[blockLength];

            for (int i = 0; i < blockLength; i++)
            {
                window[i] = (float)Math.Sin(
                    Math.PI * (i + 0.5) / (2.0 * blockLength));
            }

            return window;
        }

        void AppendCurrentFrame()
        {
            if (_profile.Channels == 1)
            {
                _pcmSink.AppendMonoFrame(
                    _frameOutput[0],
                    FRAME_LENGTH);
                return;
            }

            _pcmSink.AppendStereoFrame(
                _frameOutput[0],
                _frameOutput[1],
                FRAME_LENGTH);
        }

        static float[][] CreateFloatChannels(
            int channelCount,
            int length)
        {
            float[][] channels = new float[channelCount][];
            for (int channel = 0; channel < channelCount; channel++)
                channels[channel] = new float[length];
            return channels;
        }

        static int[][] CreateIntChannels(
            int channelCount,
            int length)
        {
            int[][] channels = new int[channelCount][];
            for (int channel = 0; channel < channelCount; channel++)
                channels[channel] = new int[length];
            return channels;
        }
    }
}
