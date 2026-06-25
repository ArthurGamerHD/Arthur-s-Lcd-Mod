using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.DockingAlignmentApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class DockingAlignment : SurfaceScriptBase,
        IReferenceBlockSelection,
        IMultiDisplayMode
    {
        public const string ID = "DockingAlignment";
        public const string TITLE = MOD_PREFIX + "DockingAlignment";

        static readonly List<MyTerminalControlComboBoxItem> DockingDisplayModes =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = 0,
                    Value = MyStringId.GetOrCompute(MOD_PREFIX + "DockingAlignment_DisplayMode_Default")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = 1,
                    Value = MyStringId.GetOrCompute(MOD_PREFIX + "DockingAlignment_DisplayMode_LcdReference")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = 2,
                    Value = MyStringId.GetOrCompute(MOD_PREFIX + "DockingAlignment_DisplayMode_ControllerReference")
                }
            };

        DockingAlignmentApp _app;
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

        public bool TryGetReferenceBlockCandidates(List<IMyTerminalBlock> blocks) => false;

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        public override void SafeRun()
        {
            if (_app == null)
            {
                _app = new DockingAlignmentApp(this);
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
