using System;
using System.Collections.Generic;

namespace ManagedDoom
{
    internal static class Console
    {
        public static void Write(string value)
        {
        }

        public static void WriteLine()
        {
        }

        public static void WriteLine(string value)
        {
        }
    }

    public sealed class Tuple<T1, T2>
    {
        public Tuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        public T1 Item1 { get; private set; }

        public T2 Item2 { get; private set; }
    }

    public static class Tuple
    {
        public static Tuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2)
        {
            return new Tuple<T1, T2>(item1, item2);
        }
    }

    public sealed class ExceptionDispatchInfo
    {
        private readonly Exception exception;

        private ExceptionDispatchInfo(Exception exception)
        {
            this.exception = exception;
        }

        public static ExceptionDispatchInfo Capture(Exception exception)
        {
            return new ExceptionDispatchInfo(exception);
        }

        public void Throw()
        {
            throw exception;
        }
    }

    public class EndOfStreamException : Exception
    {
    }

    public class NotImplementedException : Exception
    {
    }

    internal static class File
    {
        public static bool Exists(string path)
        {
            return false;
        }

        public static IEnumerable<string> ReadLines(string path)
        {
            return Array.Empty<string>();
        }

        public static byte[] ReadAllBytes(string path)
        {
            throw new Exception("Desktop file access is not available inside Space Engineers.");
        }

        public static void WriteAllBytes(string path, byte[] bytes)
        {
        }
    }

    internal static class Directory
    {
        public static string GetCurrentDirectory()
        {
            return string.Empty;
        }
    }

    internal sealed class Process
    {
        public ProcessModule MainModule { get; private set; }

        private Process()
        {
            MainModule = new ProcessModule();
        }

        public static Process GetCurrentProcess()
        {
            return new Process();
        }
    }

    internal sealed class ProcessModule
    {
        public string FileName
        {
            get { return string.Empty; }
        }
    }

}
