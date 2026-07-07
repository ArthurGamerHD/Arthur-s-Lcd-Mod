using System;

namespace LcdMod.Common
{
    public class NotImplementedException : Exception
    {
        public NotImplementedException(string msg) : base(string.IsNullOrEmpty(msg) ? "Not Implemented" : msg)
        { }
        
        public NotImplementedException() : this(string.Empty)
        { }
    }
}