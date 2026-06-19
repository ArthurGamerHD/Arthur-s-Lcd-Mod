using System.Collections.Generic;
using Generated;
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
using static LcdMod.Common.Helpers.Constants;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using CheckboxHideEmpty = LcdMod.Client.Terminal.Controls.Generic.CheckboxHideEmpty;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;

namespace LcdMod.Client.SurfaceScripts
{

#if EXPERIMENTAL
    [MyTextSurfaceScript(ID, TITLE)]
#endif
    public partial class ButtonPad : InteractiveSurfaceScript,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<CheckboxHideEmpty>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>,
        IMultiDisplayMode
    {
        protected override ConfigKind ConfigKind => ConfigKind.ButtonPanel;
        public override IApp App => _app;
        ButtonPadApp _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;

        public override List<Control> InteractiveList => _app.Children as List<Control>;

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

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            var buttonPanelConfig = AppConfig;
            if (buttonPanelConfig == null)
                return;

            base.SafeRun();

            if (_app == null)
                _app = new ButtonPadApp(buttonPanelConfig, this);

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
