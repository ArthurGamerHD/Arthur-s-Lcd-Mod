using System;

namespace ManagedDoom
{
    public sealed class WadByteStream : IDisposable
    {
        private readonly byte[] data;
        private int position;

        public WadByteStream(byte[] data)
        {
            this.data = data;
            position = 0;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            var remaining = data.Length - position;
            if (remaining <= 0)
            {
                return 0;
            }

            var read = Math.Min(count, remaining);
            Array.Copy(data, position, buffer, offset, read);
            position += read;
            return read;
        }

        public int Position
        {
            get { return position; }
            set { position = value; }
        }

        public void Dispose()
        {
        }
    }
}
