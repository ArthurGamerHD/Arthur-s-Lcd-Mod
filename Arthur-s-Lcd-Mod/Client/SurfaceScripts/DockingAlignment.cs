using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class DockingAlignment : SurfaceScriptBase,
        IReferenceBlockSelection,
        IMultiDisplayMode
    {
        public const string ID = "DockingAlignment";
        public const string TITLE = "LcdMod_DockingAlignment";

        static readonly List<MyTerminalControlComboBoxItem> DockingDisplayModes =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = 0,
                    Value = MyStringId.GetOrCompute("LcdMod_DockingAlignment_DisplayMode_Default")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = 1,
                    Value = MyStringId.GetOrCompute("LcdMod_DockingAlignment_DisplayMode_LcdReference")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = 2,
                    Value = MyStringId.GetOrCompute("LcdMod_DockingAlignment_DisplayMode_ControllerReference")
                }
            };

        DockingAlignmentApp _app;

        protected override ConfigKind ConfigKind => ConfigKind.Docking;
        public override IApp App => _app;
        protected override string DefaultTitle => TITLE;

        public DockingAlignment(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DockingDisplayModes;
        }

        public bool IsReferenceBlockCandidate(IMyTerminalBlock block)
        {
            return block is IMyShipConnector || block is IMyShipMergeBlock;
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        public override void SafeRun()
        {
            var appConfig = AppConfig as ScreenConfigDocking;
            if (appConfig == null)
                return;

            if (_app == null)
            {
                _app = new DockingAlignmentApp(appConfig, this);
                _app.LayoutChanged();
            }

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            return _app != null ? _app.GetSprites() : new List<MySprite>();
        }
    }
}
