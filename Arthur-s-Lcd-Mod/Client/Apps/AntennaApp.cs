using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.Antenna;
using LcdMod.Client.Gui.UserControls.Antenna;
using LcdMod.Client.Terminal.Controls;
using VRage.Game.GUI.TextPanel;

using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;

namespace LcdMod.Client.Apps
{
    [LcdApp(1)]
    [MultiFunction(SurfaceScripts.AntennaSurfaceScript.TITLE, "BeaconBroadcast")]
    [ConfigComponent(Constants.BLOCKS, typeof(BlockSelectionConfigComponent), PropertyName = "BlockSelectionComponent")]
    public sealed partial class AntennaApp : App, IApp, IAntennaHost
    {
        readonly List<AntennaEntry> _entries = new List<AntennaEntry>();
        readonly Dictionary<long, AntennaEntry> _entryModels = new Dictionary<long, AntennaEntry>();
        readonly HashSet<long> _activeEntryIds = new HashSet<long>();
        readonly List<long> _entriesToRemove = new List<long>();
        readonly List<AntennaCollector> _collectors = new List<AntennaCollector>();
        readonly List<MySprite> _emptySprites = new List<MySprite>();
        readonly AntennaViewModel _viewModel;
        internal IReadOnlyList<AntennaEntry> Antennas => _entries;
        internal bool IsGridMode => GeneralComponent.DisplayMode == (int)DisplayMode.Grid;
        IReadOnlyList<AntennaEntry> IAntennaHost.Antennas => Antennas;
        bool IAntennaHost.IsGridMode => IsGridMode;
        internal override float WindowRenderScaling => GeneralComponent.GetScale() > 0f ? GeneralComponent.GetScale() : 1f;
        public bool HasEntries => _entries.Count > 0;
        public AntennaApp(IAppHost script) : base(script)
        {
            _viewModel = new AntennaViewModel(this);
            SetContent(new AntennaView { DataContext = _viewModel });
        }

        public override void Update()
        {
            if (_collectors.Count == 0)
                BuildCollectors();

            _entries.Clear();
            _activeEntryIds.Clear();
            var gridLogic = Host.GridLogic;
            if (gridLogic == null)
            {
                RemoveInactiveEntryModels();
                _viewModel.Refresh();
                return;
            }

            for (int i = 0; i < _collectors.Count; i++)
                _collectors[i].Collect(gridLogic, _entries, _entryModels, _activeEntryIds);

            RemoveInactiveEntryModels();
            _entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            ApplyEntryLayout();
            _viewModel.Refresh();
        }

        public override List<MySprite> GetSprites()
        {
            return _emptySprites;
        }

        public override void Close()
        {
            for (int i = 0; i < _collectors.Count; i++)
                _collectors[i].Dispose();

            _collectors.Clear();
            base.Close();
        }

        void BuildCollectors()
        {
            _collectors.Add(new LaserAntennaCollector(Host, () => BlockSelectionComponent, () => ColorComponent));
            _collectors.Add(new RadioAntennaCollector(Host, () => BlockSelectionComponent, () => ColorComponent));
            _collectors.Add(new BeaconCollector(Host, () => BlockSelectionComponent, () => ColorComponent));
        }

        void RemoveInactiveEntryModels()
        {
            _entriesToRemove.Clear();
            foreach (var kv in _entryModels)
            {
                if (!_activeEntryIds.Contains(kv.Key))
                    _entriesToRemove.Add(kv.Key);
            }

            for (int i = 0; i < _entriesToRemove.Count; i++)
                _entryModels.Remove(_entriesToRemove[i]);
        }

        void ApplyEntryLayout()
        {
            bool grid = IsGridMode;
            bool lines = GeneralComponent.DrawLines;
            bool drawAsCard = grid && !lines;
            var header = GetHeaderColor();
            var error = ColorComponent.ResolveErrorColor();
            var foreground = Host.Surface.ScriptForegroundColor;
            for (int i = 0; i < _entries.Count; i++)
                _entries[i].ApplyLayout(drawAsCard, !grid && lines, grid && lines, header, error, foreground);
        }

        public override bool HasVisibleItems() => HasEntries;
    }
}
