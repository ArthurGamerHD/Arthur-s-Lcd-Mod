#if EXPERIMENTAL
using System;
using InvalidDataException = LcdMod.Common.InvalidDataException;

namespace LcdMod.Client.Audio.Xwma.Decoder
{
    /// <summary>
    /// Incrementally downmixes decoded WMAv2 stereo frames and resamples them
    /// to Space Engineers hardcoded 24 kHz mono PCM16 format. Only a small
    /// source-sample ring is retained, so long tracks do not require a second
    /// full-size floating-point PCM buffer.
    /// </summary>
    internal sealed class Wma2Pcm24KMonoSink
    {
        const int FILTER_TAP_COUNT = 32;
        const int FILTER_LEFT_TAP_COUNT = 15;

        const int FILTER_RIGHT_TAP_COUNT =
            FILTER_TAP_COUNT - FILTER_LEFT_TAP_COUNT - 1;

        const int SOURCE_RING_LENGTH = 64;
        const double CUTOFF_SAFETY = 0.94;

        readonly uint _sourceSampleRate;
        readonly int _phaseDivisor;
        readonly double[][] _phaseCoefficients;
        readonly float[] _sourceRing;
        byte[] _pcm;
        int _pcmByteCount;
        int _sourceFrameCount;
        int _outputFrameCount;
        bool _completed;

        public Wma2Pcm24KMonoSink(
            uint sourceSampleRate,
            long estimatedSourceFrames)
        {
            if (sourceSampleRate != 44100u && sourceSampleRate != 48000u)
            {
                throw new InvalidDataException(
                    "Only 44100 Hz and 48000 Hz WMAv2 are supported.");
            }

            if (estimatedSourceFrames < 0)
            {
                throw new InvalidDataException(
                    "The estimated WMAv2 frame count is invalid.");
            }

            _sourceSampleRate = sourceSampleRate;
            _phaseDivisor = GreatestCommonDivisor(
                (int)sourceSampleRate,
                (int)PcmAudioFormat.REQUIRED_SAMPLE_RATE);
            int phaseCount =
                (int)PcmAudioFormat.REQUIRED_SAMPLE_RATE / _phaseDivisor;
            _phaseCoefficients = BuildPhaseCoefficients(
                sourceSampleRate,
                phaseCount);
            _sourceRing = new float[SOURCE_RING_LENGTH];

            long estimatedOutputFrames = ScaleFrameCount(
                estimatedSourceFrames,
                sourceSampleRate);
            long estimatedBytes = estimatedOutputFrames *
                PcmAudioFormat.REQUIRED_MONO_BLOCK_ALIGN;

            int initialCapacity = (int)estimatedBytes;
            if (initialCapacity < 4096)
                initialCapacity = 4096;

            _pcm = new byte[initialCapacity];
        }

        public int SourceFrameCount => _sourceFrameCount;

        public int OutputFrameCount => _outputFrameCount;

        public void AppendStereoFrame(
            float[] left,
            float[] right,
            int frameLength)
        {
            if (_completed)
            {
                throw new InvalidDataException(
                    "PCM was appended after the WMAv2 output was completed.");
            }

            if (left == null || right == null ||
                frameLength < 0 ||
                left.Length < frameLength ||
                right.Length < frameLength)
            {
                throw new InvalidDataException(
                    "Invalid WMAv2 output frame.");
            }

            for (int i = 0; i < frameLength; i++)
            {
                float mono = 0.5f * (left[i] + right[i]);
                _sourceRing[_sourceFrameCount % SOURCE_RING_LENGTH] = mono;
                _sourceFrameCount++;
                EmitReadyOutput(false);
            }
        }

        public PcmWaveData Complete()
        {
            if (_completed)
            {
                throw new InvalidDataException(
                    "The WMAv2 PCM output was already completed.");
            }

            if (_sourceFrameCount <= 0)
                throw new InvalidDataException("WMAv2 produced no PCM samples.");

            EmitReadyOutput(true);
            _completed = true;

            byte[] samples;
            if (_pcmByteCount == _pcm.Length)
            {
                samples = _pcm;
            }
            else
            {
                samples = new byte[_pcmByteCount];
                Array.Copy(_pcm, 0, samples, 0, _pcmByteCount);
            }

            return new PcmWaveData
            {
                Samples = samples,
                Channels = PcmAudioFormat.REQUIRED_MONO_CHANNELS,
                SourceChannels = PcmAudioFormat.SUPPORTED_STEREO_CHANNELS,
                SampleRate = PcmAudioFormat.REQUIRED_SAMPLE_RATE,
                BitsPerSample = PcmAudioFormat.REQUIRED_BITS_PER_SAMPLE,
                BlockAlign = PcmAudioFormat.REQUIRED_MONO_BLOCK_ALIGN,
                WasDownmixedToMono = true
            };
        }

        void EmitReadyOutput(bool endOfSource)
        {
            long finalOutputFrameCount = endOfSource
                ? ScaleFrameCount(_sourceFrameCount, _sourceSampleRate)
                : long.MaxValue;

            while ((long)_outputFrameCount < finalOutputFrameCount)
            {
                long sourceNumerator =
                    (long)_outputFrameCount * _sourceSampleRate;
                int sourceIndex = (int)(sourceNumerator /
                    PcmAudioFormat.REQUIRED_SAMPLE_RATE);

                if (!endOfSource &&
                    sourceIndex + FILTER_RIGHT_TAP_COUNT >= _sourceFrameCount)
                {
                    return;
                }

                int remainder = (int)(sourceNumerator %
                    PcmAudioFormat.REQUIRED_SAMPLE_RATE);
                int phase = remainder / _phaseDivisor;
                double[] coefficients = _phaseCoefficients[phase];
                double sample = 0.0;
                double validCoefficientSum = 0.0;

                for (int tap = 0; tap < FILTER_TAP_COUNT; tap++)
                {
                    int inputFrame =
                        sourceIndex + tap - FILTER_LEFT_TAP_COUNT;

                    if (inputFrame < 0 || inputFrame >= _sourceFrameCount)
                        continue;

                    int age = _sourceFrameCount - inputFrame;
                    if (age > SOURCE_RING_LENGTH)
                    {
                        throw new InvalidDataException(
                            "The WMAv2 resampler source ring was overrun.");
                    }

                    double coefficient = coefficients[tap];
                    sample += _sourceRing[
                        inputFrame % SOURCE_RING_LENGTH] * coefficient;
                    validCoefficientSum += coefficient;
                }

                if (validCoefficientSum > 0.000000001 ||
                    validCoefficientSum < -0.000000001)
                {
                    sample /= validCoefficientSum;
                }

                AppendPcm16(FloatToPcm16(sample));
                _outputFrameCount++;
            }
        }

        void AppendPcm16(short sample)
        {
            if (_pcmByteCount >
                PcmAudioFormat.MAXIMUM_PCM_BYTES -
                PcmAudioFormat.REQUIRED_MONO_BLOCK_ALIGN)
            {
                throw new InvalidDataException(
                    "Resampled PCM exceeds the 32 MiB limit.");
            }

            EnsurePcmCapacity(PcmAudioFormat.REQUIRED_MONO_BLOCK_ALIGN);
            _pcm[_pcmByteCount++] = (byte)sample;
            _pcm[_pcmByteCount++] = (byte)(sample >> 8);
        }

        void EnsurePcmCapacity(int additionalBytes)
        {
            int required = _pcmByteCount + additionalBytes;
            if (required <= _pcm.Length)
                return;

            int capacity = _pcm.Length;
            while (capacity < required)
            {
                int growth = capacity < 1048576
                    ? capacity
                    : capacity / 2;

                if (growth < 4096)
                    growth = 4096;

                if (capacity >
                    PcmAudioFormat.MAXIMUM_PCM_BYTES - growth)
                {
                    capacity = PcmAudioFormat.MAXIMUM_PCM_BYTES;
                    break;
                }

                capacity += growth;
            }

            if (capacity < required)
            {
                throw new InvalidDataException(
                    "Resampled PCM exceeds the 32 MiB limit.");
            }

            byte[] expanded = new byte[capacity];
            Array.Copy(_pcm, 0, expanded, 0, _pcmByteCount);
            _pcm = expanded;
        }

        static long ScaleFrameCount(
            long sourceFrames,
            uint sourceSampleRate)
        {
            if (sourceFrames < 0 ||
                sourceFrames >
                (long.MaxValue - sourceSampleRate + 1L) /
                PcmAudioFormat.REQUIRED_SAMPLE_RATE)
            {
                throw new InvalidDataException(
                    "The resampled PCM frame count is too large.");
            }

            long numerator = sourceFrames *
                PcmAudioFormat.REQUIRED_SAMPLE_RATE;
            return (numerator + sourceSampleRate - 1L) /
                sourceSampleRate;
        }

        static double[][] BuildPhaseCoefficients(
            uint sourceSampleRate,
            int phaseCount)
        {
            double[][] phases = new double[phaseCount][];
            double cutoff = 0.5 *
                PcmAudioFormat.REQUIRED_SAMPLE_RATE /
                sourceSampleRate * CUTOFF_SAFETY;
            double halfWindow = FILTER_TAP_COUNT / 2.0;

            for (int phase = 0; phase < phaseCount; phase++)
            {
                double fraction = phase / (double)phaseCount;
                double[] coefficients = new double[FILTER_TAP_COUNT];
                double sum = 0.0;

                for (int tap = 0; tap < FILTER_TAP_COUNT; tap++)
                {
                    int offset = tap - FILTER_LEFT_TAP_COUNT;
                    double distance = offset - fraction;
                    double sincArgument = 2.0 * cutoff * distance;
                    double sinc;

                    if (Math.Abs(sincArgument) < 0.000000000001)
                    {
                        sinc = 1.0;
                    }
                    else
                    {
                        sinc = Math.Sin(Math.PI * sincArgument) /
                            (Math.PI * sincArgument);
                    }

                    double normalizedDistance = distance / halfWindow;
                    double window;

                    if (Math.Abs(normalizedDistance) > 1.0)
                    {
                        window = 0.0;
                    }
                    else
                    {
                        window = 0.42 +
                            0.5 * Math.Cos(
                                Math.PI * normalizedDistance) +
                            0.08 * Math.Cos(
                                2.0 * Math.PI * normalizedDistance);
                    }

                    double coefficient = 2.0 * cutoff * sinc * window;
                    coefficients[tap] = coefficient;
                    sum += coefficient;
                }

                if (sum != 0.0)
                {
                    for (int tap = 0; tap < coefficients.Length; tap++)
                        coefficients[tap] /= sum;
                }

                phases[phase] = coefficients;
            }

            return phases;
        }

        static short FloatToPcm16(double value)
        {
            if (value >= 1.0)
                return 32767;

            if (value <= -1.0)
                return -32768;

            double scaled = value * 32768.0;
            int rounded = scaled >= 0.0
                ? (int)(scaled + 0.5)
                : (int)(scaled - 0.5);

            if (rounded > 32767)
                rounded = 32767;
            else if (rounded < -32768)
                rounded = -32768;

            return (short)rounded;
        }

        static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                int remainder = left % right;
                left = right;
                right = remainder;
            }

            return left;
        }
    }
}
#endif
