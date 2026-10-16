using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.PlanetaryMap;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Helpers;
using Cartography;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps
{
    /// <summary>The nearest planet drawn by the cartography map renderer backported from the standalone Minimap.</summary>
    [LcdApp(29, Name = "PlanetaryMap")]
    [ConfigComponent(APP, typeof(PlanetaryMapConfigComponent), PropertyName = "PlanetaryMapComponent")]
    public sealed partial class PlanetaryMapApp : App, IApp, IPlanetaryMapHost
    {
        readonly PlanetaryMapViewModel _viewModel;
        bool _syncConfigNextRun;
        bool _closed;

        public PlanetaryMapApp(IAppHost host)
            : base(host)
        {
            _viewModel = new PlanetaryMapViewModel(this);
            _viewModel.ApplyConfig(true);
            SetBackgroundContent(new PlanetaryMapSceneView { DataContext = _viewModel });
            SetContent(new PlanetaryMapView { DataContext = _viewModel });
        }

        public event Action SurfaceGpsCreated
        {
            add { _viewModel.SurfaceGpsCreated += value; }
            remove { _viewModel.SurfaceGpsCreated -= value; }
        }

        public event Action CameraOrbitChanged
        {
            add { _viewModel.CameraOrbitChanged += value; }
            remove { _viewModel.CameraOrbitChanged -= value; }
        }

        public bool CanCreateSurfaceGps => _viewModel.CanCreateSurfaceGps;
        public bool CanOrbitCamera => _viewModel.CanCreateSurfaceGps;
        public CursorType RequestedCursorType => CursorType.Default;
        internal override float WindowRenderScaling => CardScale;

        public override void Update()
        {
            _viewModel.Tick();
            if (!_syncConfigNextRun)
                return;
            _syncConfigNextRun = false;
            if (Host.Block != null && Host.ProviderConfig != null)
                ConfigManager.Sync(Host.Block, Host.ProviderConfig);
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public override void Close()
        {
            _closed = true;
            _viewModel.Dispose();
            base.Close();
        }

        CartographyModule Maps.IMapHost.Cartography =>
            LcdModSessionComponent.Client != null ? LcdModSessionComponent.Client.Cartography : null;

        MatrixD? Maps.IMapHost.ReferenceWorldMatrix
        {
            get
            {
                MatrixD world;
                if (Host.TryGetReferenceWorldMatrix((int)ReferenceMode.Controller, out world))
                    return world;
                if (Host.Block != null && Host.Block.CubeGrid != null)
                    return Host.Block.CubeGrid.WorldMatrix;
                return Host.Block != null ? Host.Block.WorldMatrix : (MatrixD?)null;
            }
        }

        void Maps.IMapHost.PlayClick()
        {
            Play(AudioHelper.HudClick);
        }

        void Maps.IMapHost.PlayGps()
        {
            Play(AudioHelper.HudGps3);
        }

        void Play(Sandbox.Game.Entities.MySoundPair sound)
        {
            var interactive = Host as InteractiveSurfaceScript;
            if (interactive != null)
                interactive.PlaySounds(sound, true);
        }

        void Maps.IMapHost.RequestRender()
        {
            if (!_closed)
                Host.RenderSprites();
        }

        PlanetaryMapConfigComponent IPlanetaryMapHost.Config => PlanetaryMapComponent;
        IMyCubeBlock IPlanetaryMapHost.Block => Host.Block;
        Color IPlanetaryMapHost.HeaderColor => GetHeaderColor();
        Color IPlanetaryMapHost.WarningColor => ColorComponent.ResolveWarningColor();
        Color IPlanetaryMapHost.ErrorColor => ColorComponent.ResolveErrorColor();
        Color IPlanetaryMapHost.ForegroundColor => Host.ForegroundColor;

        void IPlanetaryMapHost.SyncConfig()
        {
            _syncConfigNextRun = true;
        }
    }
}
