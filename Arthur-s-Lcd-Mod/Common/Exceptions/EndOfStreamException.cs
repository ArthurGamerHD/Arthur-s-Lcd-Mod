// ReSharper disable RedundantUsingDirective
using System;

namespace LcdMod.Common.Exceptions
{
    public class EndOfStreamException : Exception
    {
        public EndOfStreamException(string msg) : base(string.IsNullOrEmpty(msg) ? "Reached the end of the stream" : msg)
        { }
        
        public EndOfStreamException() : this(string.Empty)
        { }
    }
}