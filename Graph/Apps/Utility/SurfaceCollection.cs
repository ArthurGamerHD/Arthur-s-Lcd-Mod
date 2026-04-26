using System;
using System.Collections.Generic;
using Graph.Apps.Abstract;

namespace Graph.Apps.Utility
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
            Graph.System.LcdModSessionComponent.HookSurfaceModules(item);
            Added?.Invoke(item);
        }

        public bool Remove(SurfaceScriptBase item)
        {
            Graph.System.LcdModSessionComponent.UnhookSurfaceModules(item);
            var removed = _items.Remove(item);
            if (removed)
                Removed?.Invoke(item);

            return removed;
        }

        public void Clear()
        {
            for (int i = 0; i < _items.Count; i++)
                Graph.System.LcdModSessionComponent.UnhookSurfaceModules(_items[i]);

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

        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }
    }
}
