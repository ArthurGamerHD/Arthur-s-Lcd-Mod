using System;
using System.Collections.Generic;
using System.IO;

namespace ManagedDoom
{
    public static class CompatibilityExtensions
    {
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key))
            {
                return false;
            }

            dictionary.Add(key, value);
            return true;
        }

        public static void ReadExactly(this Stream stream, byte[] buffer)
        {
            ReadExactly(stream, buffer, 0, buffer.Length);
        }

        public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                var read = stream.Read(buffer, offset, count);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
                count -= read;
            }
        }
    }
}
