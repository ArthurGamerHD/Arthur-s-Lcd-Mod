using System.IO;
using ManagedDoom.Video;

namespace ManagedDoom.SE
{
    public sealed class SEVideoToTextSprite : IVideo
    {
        private readonly Renderer renderer;
        private readonly TextWriter output;
        private readonly char[] lowFrameBuffer;
        private readonly char[] middleFrameBuffer;
        private readonly char[] highFrameBuffer;

        public SEVideoToTextSprite(Config config, GameContent content)
            : this(config, content, null)
        {
        }

        public SEVideoToTextSprite(Config config, GameContent content, TextWriter output)
        {
            renderer = new Renderer(config, content);
            this.output = output;
            var frameBufferLength = renderer.Width * renderer.Height;
            lowFrameBuffer = new char[frameBufferLength];
            middleFrameBuffer = new char[frameBufferLength];
            highFrameBuffer = new char[frameBufferLength];
        }

        public void Render(Doom doom, Fixed frameFrac)
        {
            renderer.Render(
                doom,
                lowFrameBuffer,
                middleFrameBuffer,
                highFrameBuffer,
                frameFrac);

            if (output != null)
            {
                // Preserve the old diagnostic output behavior by writing the
                // most significant layer.
                output.Write(highFrameBuffer, 0, highFrameBuffer.Length);
                output.Flush();
            }
        }

        public void InitializeWipe()
        {
            renderer.InitializeWipe();
        }

        public bool HasFocus()
        {
            return true;
        }

        public char[] FrameBuffer
        {
            get
            {
                return highFrameBuffer;
            }
        }

        public char[] LowFrameBuffer
        {
            get
            {
                return lowFrameBuffer;
            }
        }

        public char[] MiddleFrameBuffer
        {
            get
            {
                return middleFrameBuffer;
            }
        }

        public char[] HighFrameBuffer
        {
            get
            {
                return highFrameBuffer;
            }
        }

        public int Width
        {
            get
            {
                return renderer.Width;
            }
        }

        public int Height
        {
            get
            {
                return renderer.Height;
            }
        }

        public int MaxWindowSize
        {
            get
            {
                return renderer.MaxWindowSize;
            }
        }

        public int WindowSize
        {
            get
            {
                return renderer.WindowSize;
            }

            set
            {
                renderer.WindowSize = value;
            }
        }

        public bool DisplayMessage
        {
            get
            {
                return renderer.DisplayMessage;
            }

            set
            {
                renderer.DisplayMessage = value;
            }
        }

        public int MaxGammaCorrectionLevel
        {
            get
            {
                return renderer.MaxGammaCorrectionLevel;
            }
        }

        public int GammaCorrectionLevel
        {
            get
            {
                return renderer.GammaCorrectionLevel;
            }

            set
            {
                renderer.GammaCorrectionLevel = value;
            }
        }

        public int WipeBandCount
        {
            get
            {
                return renderer.WipeBandCount;
            }
        }

        public int WipeHeight
        {
            get
            {
                return renderer.WipeHeight;
            }
        }
    }
}
