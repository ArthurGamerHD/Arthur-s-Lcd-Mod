using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LcdMod.Client.Modules.Power
{
    public sealed class ReferenceIdentityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceIdentityComparer<T> Instance = new ReferenceIdentityComparer<T>();

        public bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
