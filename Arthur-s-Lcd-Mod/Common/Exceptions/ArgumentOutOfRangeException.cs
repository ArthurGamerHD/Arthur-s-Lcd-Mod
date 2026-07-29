// ReSharper disable RedundantUsingDirective
using System;

namespace LcdMod.Common.Exceptions
{
    public class ArgumentOutOfRangeException : Exception
    {
        public ArgumentOutOfRangeException(string paramName)
            : base(string.IsNullOrEmpty(paramName) ? "Argument out of range" : paramName + " was out of range")
        {
        }

        public ArgumentOutOfRangeException()
            : this(string.Empty)
        {
        }
    }
}
