#if EXPERIMENTAL
using System;
using InvalidDataException = LcdMod.Common.InvalidDataException;

namespace LcdMod.Client.Audio.Xwma.Decoder
{
    /// <summary>
    /// Not sure what this math is mathing, but wma/ffmpeg does, so I think we do too
    /// </summary>
    internal sealed class Wma2FastImdct
    {
        const double DECODER_SCALE = -1.0 / 32768.0;

        readonly int _coefficientCount;
        readonly int _outputCount;
        readonly double[] _real;
        readonly double[] _imaginary;
        readonly double[] _dct;
        readonly double[] _inputCos;
        readonly double[] _inputSin;
        readonly double[] _outputCos;
        readonly double[] _outputSin;

        public Wma2FastImdct(int coefficientCount)
        {
            if (coefficientCount < 2 ||
                (coefficientCount & (coefficientCount - 1)) != 0)
            {
                throw new InvalidDataException(
                    "The WMAv2 IMDCT length must be a power of two.");
            }

            _coefficientCount = coefficientCount;
            _outputCount = coefficientCount * 2;
            _real = new double[_outputCount];
            _imaginary = new double[_outputCount];
            _dct = new double[_coefficientCount];
            _inputCos = new double[_coefficientCount];
            _inputSin = new double[_coefficientCount];
            _outputCos = new double[_coefficientCount];
            _outputSin = new double[_coefficientCount];

            for (int i = 0; i < _coefficientCount; i++)
            {
                double inputAngle = -Math.PI * i /
                    (2.0 * _coefficientCount);
                double outputAngle = -Math.PI * (2.0 * i + 1.0) /
                    (4.0 * _coefficientCount);

                _inputCos[i] = Math.Cos(inputAngle);
                _inputSin[i] = Math.Sin(inputAngle);
                _outputCos[i] = Math.Cos(outputAngle);
                _outputSin[i] = Math.Sin(outputAngle);
            }
        }

        public int CoefficientCount => _coefficientCount;

        public int OutputCount => _outputCount;

        public void Transform(float[] coefficients, float[] output)
        {
            if (coefficients == null ||
                coefficients.Length < _coefficientCount)
            {
                throw new InvalidDataException(
                    "The WMAv2 coefficient buffer is too small.");
            }

            if (output == null || output.Length < _outputCount)
            {
                throw new InvalidDataException(
                    "The WMAv2 IMDCT output buffer is too small.");
            }

            Array.Clear(_real, 0, _real.Length);
            Array.Clear(_imaginary, 0, _imaginary.Length);

            for (int i = 0; i < _coefficientCount; i++)
            {
                double value = coefficients[i];
                _real[i] = value * _inputCos[i];
                _imaginary[i] = value * _inputSin[i];
            }

            ForwardFft(_real, _imaginary);

            for (int i = 0; i < _coefficientCount; i++)
            {
                _dct[i] =
                    _real[i] * _outputCos[i] -
                    _imaginary[i] * _outputSin[i];
            }

            int quarter = _coefficientCount / 2;
            int index = 0;

            for (int i = quarter; i < _coefficientCount; i++)
                output[index++] = (float)(_dct[i] * DECODER_SCALE);

            for (int i = _coefficientCount - 1; i >= 0; i--)
                output[index++] = (float)(-_dct[i] * DECODER_SCALE);

            for (int i = 0; i < quarter; i++)
                output[index++] = (float)(-_dct[i] * DECODER_SCALE);
        }

        static void ForwardFft(double[] real, double[] imaginary)
        {
            int length = real.Length;

            for (int i = 1, j = 0; i < length; i++)
            {
                int bit = length >> 1;

                while ((j & bit) != 0)
                {
                    j ^= bit;
                    bit >>= 1;
                }

                j ^= bit;

                if (i < j)
                {
                    double swap = real[i];
                    real[i] = real[j];
                    real[j] = swap;

                    swap = imaginary[i];
                    imaginary[i] = imaginary[j];
                    imaginary[j] = swap;
                }
            }

            for (int size = 2; size <= length; size <<= 1)
            {
                int half = size >> 1;
                double angle = -2.0 * Math.PI / size;
                double stepReal = Math.Cos(angle);
                double stepImaginary = Math.Sin(angle);

                for (int start = 0; start < length; start += size)
                {
                    double twiddleReal = 1.0;
                    double twiddleImaginary = 0.0;

                    for (int offset = 0; offset < half; offset++)
                    {
                        int even = start + offset;
                        int odd = even + half;
                        double oddReal =
                            real[odd] * twiddleReal -
                            imaginary[odd] * twiddleImaginary;
                        double oddImaginary =
                            real[odd] * twiddleImaginary +
                            imaginary[odd] * twiddleReal;

                        double evenReal = real[even];
                        double evenImaginary = imaginary[even];

                        real[even] = evenReal + oddReal;
                        imaginary[even] = evenImaginary + oddImaginary;
                        real[odd] = evenReal - oddReal;
                        imaginary[odd] = evenImaginary - oddImaginary;

                        double nextReal =
                            twiddleReal * stepReal -
                            twiddleImaginary * stepImaginary;
                        twiddleImaginary =
                            twiddleReal * stepImaginary +
                            twiddleImaginary * stepReal;
                        twiddleReal = nextReal;
                    }
                }
            }
        }
    }
}
#endif
