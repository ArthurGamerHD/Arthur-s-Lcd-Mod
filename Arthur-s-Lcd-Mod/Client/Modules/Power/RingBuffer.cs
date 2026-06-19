using System;
using System.Collections.Generic;

namespace LcdMod.Client.Modules.Power
{
    public sealed class RingBuffer<T>
    {
        readonly T[] _items;
        int _head;
        int _count;

        public RingBuffer(int capacity)
        {
            _items = new T[Math.Max(1, capacity)];
        }

        public int Capacity => _items.Length;
        public int Count => _count;

        public void Add(T item)
        {
            _items[_head] = item;
            _head = (_head + 1) % _items.Length;
            if (_count < _items.Length)
                _count++;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            _head = 0;
            _count = 0;
        }

        public List<T> ToListOldestFirst()
        {
            var result = new List<T>(_count);
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head - _count + i + _items.Length) % _items.Length;
                result.Add(_items[idx]);
            }

            return result;
        }
    }
}
