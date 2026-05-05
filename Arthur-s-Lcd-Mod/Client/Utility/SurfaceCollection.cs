using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;

namespace LcdMod.Client.Utility
{
    /// <summary>
    /// Collection of active surfaces with extensibility hooks on Add.
    /// </summary>
    public sealed class SurfaceCollection : ICollection<SurfaceScriptBase>
    {
        readonly List<SurfaceScriptBase> _items = new List<SurfaceScriptBase>();

        public event Action<SurfaceScriptBase> Added;
        public event Action<SurfaceScriptBase> Removed;
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        
        public void Add(SurfaceScriptBase item)
        {
            _items.Add(item);
            LcdModSessionComponent.HookSurfaceModules(item);
            Added?.Invoke(item);
        }

        public bool Remove(SurfaceScriptBase item)
        {
            LcdModSessionComponent.UnhookSurfaceModules(item);
            var removed = _items.Remove(item);
            if (removed)
                Removed?.Invoke(item);

            return removed;
        }

        public void Clear()
        {
            for (int i = 0; i < _items.Count; i++)
                LcdModSessionComponent.UnhookSurfaceModules(_items[i]);

            _items.Clear();
        }

        public bool Contains(SurfaceScriptBase item)
        {
            return _items.Contains(item);
        }

        public void CopyTo(SurfaceScriptBase[] array, int arrayIndex)
        {
            _items.CopyTo(array, arrayIndex);
        }

        public IEnumerator<SurfaceScriptBase> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }
    }
}
