using System.IO;
using ManagedDoom.Video;

namespace ManagedDoom.SE
{
    public sealed class SEVideoToTextSprite : IVideo
    {
        private readonly Renderer renderer;
        private readonly TextWriter output;
        private readonly byte[] frameBuffer;
        private readonly char[] diagnosticFrameBuffer;

        public SEVideoToTextSprite(Config config, GameContent content)
            : this(config, content, null)
        {
        }

        public SEVideoToTextSprite(Config config, GameContent content, TextWriter output)
        {
            renderer = new Renderer(config, content);
            this.output = output;
            frameBuffer = new byte[renderer.Width * renderer.Height * 4];

            if (output != null)
                diagnosticFrameBuffer = new char[renderer.Width * renderer.Height];
        }

        public void Render(Doom doom, Fixed frameFrac)
        {
            RenderTo(doom, frameBuffer, frameFrac);
        }

        /// <summary>
        /// Renders directly into a caller-owned RGBA target. This lets the Doom
        /// worker render into an acquired frame slot without first writing an
        /// intermediate framebuffer and copying it.
        /// </summary>
        public void RenderTo(Doom doom, byte[] target, Fixed frameFrac)
        {
            if (target == null)
                throw new System.ArgumentNullException("target");
            if (target.Length < renderer.Width * renderer.Height * 4)
                throw new System.ArgumentException("The target framebuffer is too small.", "target");

            renderer.Render(doom, target, frameFrac);

            if (output != null)
            {
                for (var i = 0; i < diagnosticFrameBuffer.Length; i++)
                {
                    var offset = i * 4;
                    diagnosticFrameBuffer[i] = Renderer.ColorToChar(
                        target[offset],
                        target[offset + 1],
                        target[offset + 2]);
                }

                output.Write(diagnosticFrameBuffer, 0, diagnosticFrameBuffer.Length);
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

        public byte[] FrameBuffer
        {
            get
            {
                return frameBuffer;
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
