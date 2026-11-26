using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.StarMap;
using LcdMod.Client.Config;
using LcdMod.Client.GridData;
using Acrux.Core.Input;
using LcdMod.Client.Gui;
using LcdMod.Client.Helpers;
using Acrux.Cartography;
using Acrux.Cartography.Maps;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps
{
    /// <summary>
    /// The Star Map backported from the standalone Minimap, as an orbit map. Its first-person HUD is now External
    /// Sensors: a screen still set to it is moved there.
    /// </summary>
    [LcdApp(13)]
    [MultiFunction(SurfaceScripts.StarMapSurfaceScript.TITLE, "Map3D")]
    [ConfigComponent(APP, typeof(StarMapConfigComponent), PropertyName = "StarMapComponent")]
    public partial class StarMapApp : App, IApp, IStarMapHost
    {
        public const string ID = MOD_PREFIX + "StarMapSurface";
        public const string TITLE = MOD_PREFIX + "StarMapSurface";

        readonly StarMapViewModel _viewModel;
        readonly LinkedTypedBlockSourceSet<IMyJumpDrive> _jumpDriveSources =
            new LinkedTypedBlockSourceSet<IMyJumpDrive>(blocks => blocks.JumpDrives);
        readonly JumpPointGpsHelper _jumpPointGps = new JumpPointGpsHelper();
        bool _syncConfigNextRun;
        bool _closed;
        bool _migrating;

        public StarMapApp(IAppHost host)
            : base(host)
        {
            _viewModel = new StarMapViewModel(this);
            _viewModel.ApplyConfig(true);
            SetBackgroundContent(new StarMapSceneView { DataContext = _viewModel });
            SetContent(new StarMapView { DataContext = _viewModel });
        }

        public event Action PlanetDetailsOpened
        {
            add { _viewModel.PlanetDetailsOpened += value; }
            remove { _viewModel.PlanetDetailsOpened -= value; }
        }

        public event Action StaticCameraOrbitChanged
        {
            add { _viewModel.StaticCameraOrbitChanged += value; }
            remove { _viewModel.StaticCameraOrbitChanged -= value; }
        }

        internal override float WindowRenderScaling => CardScale;

        public override void Update()
        {
            MigrateHud();
            _viewModel.Tick();
            if (!_syncConfigNextRun)
                return;
            _syncConfigNextRun = false;
            if (Host.Block != null && Host.ProviderConfig != null)
                ConfigManager.Sync(Host.Block, Host.ProviderConfig);
        }

        // The HUD mode moved to External Sensors, which keeps this screen's settings (its display mode one is first
        // person there). Only a player allowed to change the screen switches it; the game syncs the new script.
        void MigrateHud()
        {
            if (_migrating || GeneralComponent.DisplayMode != (int)StarMapDisplayMode.Hud)
                return;
            var block = Host.Block as IMyTerminalBlock;
            if (block == null || !block.HasLocalPlayerAccess())
                return;
            _migrating = true;
            var surface = Host.Surface;
            Action switchScript = () =>
            {
                if (surface != null && !block.MarkedForClose)
                    surface.Script = SurfaceScripts.ExternalSensorsSurfaceScript.ID;
            };
            lock (LcdModClientComponent.RunNextFrame)
                LcdModClientComponent.RunNextFrame.Add(switchScript);
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public override void Close()
        {
            _closed = true;
            _jumpDriveSources.Dispose();
            _jumpPointGps.Clear();
            _viewModel.Dispose();
            base.Close();
        }

        MatrixD ReferenceMatrix
        {
            get
            {
                MatrixD world;
                if (Host.Block == null)
                    return MatrixD.Identity;
                if (!Host.TryGetReferenceWorldMatrix(InteractionComponent.ReferenceMode, out world))
                    return Host.Block.WorldMatrix;
                Vector3D forward = world.Forward;
                Vector3D right = world.Right;
                Vector3D up = world.Up;
                if (forward.Normalize() <= 1e-6 || right.Normalize() <= 1e-6 || up.Normalize() <= 1e-6)
                    return Host.Block.WorldMatrix;
                world.Forward = forward;
                world.Right = right;
                world.Up = up;
                return world;
            }
        }

        CartographyModule Maps.IMapHost.Cartography =>
            LcdModSessionComponent.Client != null ? LcdModSessionComponent.Client.Cartography : null;

        MatrixD? Maps.IMapHost.ReferenceWorldMatrix => Host.Block == null ? (MatrixD?)null : ReferenceMatrix;

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
            var interactive = InteractiveSurface;
            if (interactive != null)
                interactive.PlaySounds(sound, true);
        }

        void Maps.IMapHost.RequestRender()
        {
            if (!_closed)
                Host.RenderSprites();
        }

        void Maps.IMapHost.RequestFrames()
        {
            if (!_closed)
                Host.RequestFrames();
        }

        StarMapConfigComponent IStarMapHost.Config => StarMapComponent;
        IMyCubeBlock IStarMapHost.Block => Host.Block;
        string IStarMapHost.Title => Host.Title;
        Color IMarkerColors.HeaderColor => GetHeaderColor();
        Color IMarkerColors.WarningColor => ColorComponent.ResolveWarningColor();
        Color IMarkerColors.ErrorColor => ColorComponent.ResolveErrorColor();
        Color IStarMapHost.ForegroundColor => Host.ForegroundColor;

        bool IStarMapHost.HasJumpDrives
        {
            get
            {
                _jumpDriveSources.Bind(Host.GridLogic, GridLinkTypeEnum.Physical);
                var sources = _jumpDriveSources.Sources;
                for (int i = 0; i < sources.Count; i++)
                    if (sources[i].Count > 0)
                        return true;
                return false;
            }
        }

        bool IStarMapHost.TryGetJumpPoint(PlanetSnapshot planet, double gravityRange, out Vector3D jumpPoint)
        {
            return _jumpPointGps.TryGetJumpPoint(
                Host.Block != null ? Host.Block.CubeGrid : null,
                planet.EntityId,
                planet.Name,
                planet.Position,
                planet.Radius,
                gravityRange,
                out jumpPoint,
                false);
        }

        void IStarMapHost.SyncConfig()
        {
            _syncConfigNextRun = true;
        }
    }
}
