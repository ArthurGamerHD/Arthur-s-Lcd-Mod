using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Client.Utility;
#if EXPERIMENTAL
using Sandbox.Game.GameSystems.TextSurfaceScripts;
#endif
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using CheckboxHideEmpty = LcdMod.Client.Terminal.Controls.Generic.CheckboxHideEmpty;
using ComboboxButtonStyle = LcdMod.Client.Terminal.Controls.Generic.ComboboxButtonStyle;
using SliderButtonCount = LcdMod.Client.Terminal.Controls.Generic.SliderButtonCount;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{

    [LcdSurface(typeof(ButtonPadApp))]
#if EXPERIMENTAL
    [MyTextSurfaceScript(ID, TITLE)]
#endif
    public partial class ButtonPad : InteractiveSurfaceScript,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<SliderButtonCount>,
        IUsesTerminalControl<ComboboxButtonStyle>,
        IUsesTerminalControl<CheckboxHideEmpty>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>,
        IMultiDisplayMode
    {
        public override IApp App => _app;
        ButtonPadApp _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;

        public override List<Control> InteractiveList => _app.VisualChildren as List<Control>;

        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public const string ID = MOD_PREFIX + "ButtonPadApp";
        public const string TITLE = MOD_PREFIX + "ButtonPad";

        protected override string DefaultTitle => TITLE;

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

        public ButtonPad(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();

            if (_app != null)
                _app.LayoutChanged();
        }

        protected override IApp DetachGridBoundApp()
        {
            var app = _app;
            _app = null;
            return app;
        }

        public override void SafeRun()
        {

            base.SafeRun();

            if (_app == null)
                _app = new ButtonPadApp(this);

            _app.Update();

            RenderSprites();
        }


        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();

            AddBackground(sprites);
            DrawTitle(sprites);
            DrawFooter(sprites);
            sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
