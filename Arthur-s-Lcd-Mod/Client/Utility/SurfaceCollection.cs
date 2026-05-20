using System;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.Game.Components;
using Sandbox.ModAPI;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace LcdMod.Client.Utility
{
    public interface ISurfaceTssInstances
    {
        SurfaceScriptBase GetInstance(int index);
    }

    public sealed class TextPanelSurfaceTssInstances : SurfaceTssInstancesBase
    {
        readonly IMyTextPanel _panel;
        IApp _app;

        public TextPanelSurfaceTssInstances(IMyTextPanel panel)
        {
            _panel = panel;
        }

        public SurfaceScriptBase GetActiveInstance()
        {
            return GetInstance(GetActiveRotationIndex());
        }

        public IApp GetApp()
        {
            return _app;
        }

        public TApp GetApp<TApp>() where TApp : class, IApp
        {
            return _app as TApp;
        }

        public bool SetAppIfNullOrDifferent(IApp app)
        {
            if (app == null)
                return false;

            if (_app != null && _app.GetType() == app.GetType())
                return false;

            _app = app;
            return true;
        }

        public void ClearApp()
        {
            _app = null;
        }

        internal override int GetIndex(SurfaceScriptBase instance)
        {
            return instance?.RotationOrSurfaceIndex ?? 0;
        }

        int GetActiveRotationIndex()
        {
            if (_panel == null)
                return 0;

            foreach (var component in _panel.Components)
            {
                var lcdSurfaceComponent = component as IMyLcdSurfaceComponent;
                if (lcdSurfaceComponent == null)
                    continue;

                return lcdSurfaceComponent.SelectedRotationIndex;
            }

            return 0;
        }
    }

    public sealed class TextSurfaceProviderTssInstances : SurfaceTssInstancesBase
    {
        public TextSurfaceProviderTssInstances(IMyTextSurfaceProvider provider)
        {
        }

        internal override int GetIndex(SurfaceScriptBase instance)
        {
            return instance?.RotationOrSurfaceIndex ?? 0;
        }
    }

    public abstract class SurfaceTssInstancesBase : ISurfaceTssInstances
    {
        readonly Dictionary<int, SurfaceScriptBase> _instancesByIndex =
            new Dictionary<int, SurfaceScriptBase>();

        public SurfaceScriptBase GetInstance(int index)
        {
            SurfaceScriptBase instance;
            return _instancesByIndex.TryGetValue(index, out instance) ? instance : null;
        }

        public IEnumerable<SurfaceScriptBase> GetInstances()
        {
            foreach (var entry in _instancesByIndex)
                yield return entry.Value;
        }

        internal abstract int GetIndex(SurfaceScriptBase instance);

        internal void Add(SurfaceScriptBase instance)
        {
            if (instance == null)
                return;

            Remove(instance);

            var index = GetIndex(instance);
            _instancesByIndex[index] = instance;
        }

        internal void Remove(SurfaceScriptBase instance)
        {
            if (instance == null)
                return;

            var emptyIndexes = new List<int>();
            foreach (var entry in _instancesByIndex)
            {
                if (entry.Value == instance)
                    emptyIndexes.Add(entry.Key);
            }

            for (int i = 0; i < emptyIndexes.Count; i++)
                _instancesByIndex.Remove(emptyIndexes[i]);
        }

        internal bool IsEmpty
        {
            get { return _instancesByIndex.Count == 0; }
        }
    }

    /// <summary>
    /// Collection of active surfaces with extensibility hooks on Add.
    /// </summary>
    public sealed class SurfaceCollection : ICollection<SurfaceScriptBase>
    {
        readonly List<SurfaceScriptBase> _items = new List<SurfaceScriptBase>();
        readonly Dictionary<long, SurfaceTssInstancesBase> _instancesByBlock =
            new Dictionary<long, SurfaceTssInstancesBase>();

        public event Action<SurfaceScriptBase> Added;
        public event Action<SurfaceScriptBase> Removed;
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        
        public void Add(SurfaceScriptBase item)
        {
            _items.Add(item);
            AddToSlots(item);
            LcdModSessionComponent.HookSurfaceModules(item);
            Added?.Invoke(item);
        }

        public bool Remove(SurfaceScriptBase item)
        {
            LcdModSessionComponent.UnhookSurfaceModules(item);
            var removed = _items.Remove(item);
            if (removed)
            {
                RemoveFromSlots(item);
                Removed?.Invoke(item);
            }

            return removed;
        }

        public void Clear()
        {
            for (int i = 0; i < _items.Count; i++)
                LcdModSessionComponent.UnhookSurfaceModules(_items[i]);

            _items.Clear();
            _instancesByBlock.Clear();
        }

        public ISurfaceTssInstances GetInstances(IMyTerminalBlock block)
        {
            if (block == null)
                return null;

            SurfaceTssInstancesBase instances;
            return _instancesByBlock.TryGetValue(block.EntityId, out instances) ? instances : null;
        }

        public SurfaceScriptBase GetInstance(IMyTerminalBlock block, int index)
        {
            var instances = GetInstances(block);
            return instances?.GetInstance(index);
        }

        void AddToSlots(SurfaceScriptBase item)
        {
            var block = item?.Block as IMyTerminalBlock;
            if (block == null)
                return;

            SurfaceTssInstancesBase instances;
            if (!_instancesByBlock.TryGetValue(block.EntityId, out instances))
            {
                instances = CreateInstances(block);
                if (instances == null)
                    return;

                _instancesByBlock[block.EntityId] = instances;
            }

            instances.Add(item);
        }

        void RemoveFromSlots(SurfaceScriptBase item)
        {
            var block = item?.Block as IMyTerminalBlock;
            if (block == null)
                return;

            SurfaceTssInstancesBase instances;
            if (!_instancesByBlock.TryGetValue(block.EntityId, out instances))
                return;

            instances.Remove(item);
            if (instances.IsEmpty)
                _instancesByBlock.Remove(block.EntityId);
        }

        static SurfaceTssInstancesBase CreateInstances(IMyTerminalBlock block)
        {
            var panel = block as IMyTextPanel;
            if (panel != null)
                return new TextPanelSurfaceTssInstances(panel);

            var provider = block as IMyTextSurfaceProvider;
            if (provider != null)
                return new TextSurfaceProviderTssInstances(provider);

            return null;
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
