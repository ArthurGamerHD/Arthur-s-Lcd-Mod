using System.Collections.Generic;
using Generated;
using IAutoScroll = LcdMod.Client.Terminal.Controls.Generic.IAutoScroll;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Client.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.ModAPI;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;
using SwitchSubGrid = LcdMod.Client.Terminal.Controls.Generic.SwitchSubGrid;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.CargoFilledApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class CargoFilledSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<SwitchSubGrid>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>,
        IAutoScroll,
        IMultiDisplayMode
    {
        public const string ID = "ContainerCharts";
        public const string TITLE = "DisplayName_CargoFilledEntityComponent";
        protected override string DefaultTitle => TITLE;

        public override IApp App => _app;

        CargoFilledApp _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
public override List<Control> InteractiveList => _app.VisualChildren as List<Control>;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public CargoFilledSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

        public override void SafeRun()
        {
            base.SafeRun();

            if (_app == null)
                _app = new CargoFilledApp(this);

            UpdateViewBox();
            _app.Update();

            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (_app == null || !_app.HasEntries)
            {
                AddEmptySprites(sprites);
                return sprites;
            }

            AddBackground(sprites);
            DrawTitle(sprites);
            sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
