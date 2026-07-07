using System;

namespace LcdMod.Common
{
    public class InvalidDataException : Exception
    {
        public InvalidDataException(string msg, Exception upstream) : base(string.IsNullOrEmpty(msg) ? "Invalid data" : msg, upstream)
        {
            
        }
        public InvalidDataException(string msg) : base(string.IsNullOrEmpty(msg) ? "Invalid data" : msg)
        { }
        
        public InvalidDataException() : this(string.Empty)
        { }
    }
}