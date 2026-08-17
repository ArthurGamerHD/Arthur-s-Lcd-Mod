using System;
using System.Collections.Generic;
#if EXPERIMENTAL
using LcdMod.Client.Diagnostics;
#endif
using LcdMod.Common.Helpers;
using LcdMod.Common.Mvvm;
using VRage.Game.ModAPI;
using GridLinkTypeEnum = VRage.Game.ModAPI.GridLinkTypeEnum;

namespace LcdMod.Client.GridData
{
    /// <summary>
    ///     Keeps references to the live typed block lists belonging to a grid-link scope.
    ///     It never copies block membership; source references are resolved only on initial bind
    ///     and after link-topology changes.
    /// </summary>
    public sealed class LinkedTypedBlockSourceSet<T> : IDisposable
    {
        readonly Func<TypedBlockCollection, IObservableList<T>> _selectBlocks;
        readonly List<IObservableList<T>> _sources = new List<IObservableList<T>>();
        readonly List<GridLogic> _sourceLogics = new List<GridLogic>();
        readonly HashSet<IObservableList<T>> _seenSources = new HashSet<IObservableList<T>>();
        readonly List<IMyCubeGrid> _linkedGrids = new List<IMyCubeGrid>();
        readonly HashSet<long> _linkedGridIds = new HashSet<long>();

        GridLogic _root;
        GridLinkTypeEnum _linkType;
        bool _hasScope;
        bool _rebindScheduled;
        bool _isDisposed;

        public LinkedTypedBlockSourceSet(Func<TypedBlockCollection, IObservableList<T>> selectBlocks)
        {
            if (selectBlocks == null)
                throw new ArgumentNullException(nameof(selectBlocks));

            _selectBlocks = selectBlocks;
        }

        public IReadOnlyList<IObservableList<T>> Sources => _sources;
        public event Action Changed;

        public void Bind(GridLogic root, GridLinkTypeEnum linkType)
        {
            if (_isDisposed)
                return;
            if (_hasScope && ReferenceEquals(_root, root) && _linkType == linkType)
                return;

            UnbindRoot();
            _root = root;
            _linkType = linkType;
            _hasScope = root != null;

            if (_root == null)
            {
                ClearSources();
                RaiseChanged();
                return;
            }

            _root.WatchLinkType(_linkType);
            _root.LinkChanged += OnLinkChanged;
            TryRebindSources();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Unbind();
            _isDisposed = true;
            Changed = null;
        }

        public void Unbind()
        {
            if (_isDisposed)
                return;

            UnbindRoot();
            ClearSources();
        }

        void OnLinkChanged(object sender, GridLinkChangedArgs args)
        {
            if (args != null && args.LinkType == _linkType)
                ScheduleRebind();
        }

        void ScheduleRebind()
        {
            if (_isDisposed || _root == null || _rebindScheduled)
                return;

            _rebindScheduled = true;
            LcdModClientComponent.RunNextFrame.Add(RunScheduledRebind);
        }

        void RunScheduledRebind()
        {
            _rebindScheduled = false;
            if (_isDisposed || _root == null)
                return;

            TryRebindSources();
        }

        void TryRebindSources()
        {
            try
            {
#if EXPERIMENTAL
                using (RuntimeProfiler.Measure(
                           "blocks.topology",
                           "rebind_linked_typed_sources",
                           typeof(T).Name,
                           _root != null ? _root.TargetGrid : 0L))
#endif
                {
                    RebindSources();
                }
            }
            catch (Exception e)
            {
                LogHelper.LogOnce(
                    "LinkedTypedBlockSourceSet.Rebind." + typeof(T).FullName + "." + e.GetType().FullName,
                    "Linked typed block sources rebind failed and will be retried: " + e);
                ScheduleRebind();
            }
        }

        void RebindSources()
        {
            ClearSources();
            _root.GetLinkedGrids(_linkType, _linkedGrids);
            if (_linkedGrids.Count == 0 && _root.Grid != null)
                _linkedGrids.Add(_root.Grid);

            for (int i = 0; i < _linkedGrids.Count; i++)
            {
                var grid = _linkedGrids[i];
                if (grid == null || grid.Closed || grid.MarkedForClose || !_linkedGridIds.Add(grid.EntityId))
                    continue;

                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grid);
                if (logic == null)
                    continue;

                logic.RequestCapability(GridCapability.Blocks);
                var retained = false;
                try
                {
                    var source = _selectBlocks(logic.Blocks);
                    if (source != null && _seenSources.Add(source))
                    {
                        retained = true;
                        _sourceLogics.Add(logic);
                        _sources.Add(source);
                        source.ItemAdded += OnSourceItemAdded;
                        source.ItemRemoved += OnSourceItemRemoved;
                    }
                }
                finally
                {
                    if (!retained)
                        logic.Release(GridCapability.Blocks);
                }
            }

            _seenSources.Clear();
            _linkedGrids.Clear();
            _linkedGridIds.Clear();
            RaiseChanged();
        }

        void UnbindRoot()
        {
            if (_root != null)
                _root.LinkChanged -= OnLinkChanged;

            _root = null;
            _hasScope = false;
        }

        void ClearSources()
        {
            foreach (var source in _sources)
            {
                source.ItemAdded -= OnSourceItemAdded;
                source.ItemRemoved -= OnSourceItemRemoved;
            }
            foreach (var logic in _sourceLogics)
                logic.Release(GridCapability.Blocks);
            _sources.Clear();
            _sourceLogics.Clear();
            _seenSources.Clear();
            _linkedGrids.Clear();
            _linkedGridIds.Clear();
        }

        void OnSourceItemAdded(IObservableCollection<T> sender, T item)
        {
            RaiseChanged();
        }

        void OnSourceItemRemoved(IObservableCollection<T> sender, T item)
        {
            RaiseChanged();
        }

        void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null)
                handler();
        }
    }
}
