using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.Models.Antenna;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Client.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.ModAPI;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class AntennaSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>,
        IMultiDisplayMode
    {
        protected override ConfigKind ConfigKind => ConfigKind.WithBlocks;
        AntennaApp _app;

        public const string ID = "AntennaGraph";
        public const string TITLE = "Antenna";

        protected override string DefaultTitle => TITLE;

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

        public AntennaSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _app = null;
        }

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            if (_app == null)
                _app = new AntennaApp(AppConfig, this);
            else if (!ReferenceEquals(_app.Config, AppConfig))
                _app.SetConfig(AppConfig);

            _app.Update(GridLogic);

            if (!_app.HasEntries)
            {
                Empty();
                return;
            }

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                DrawFooter(sprites);
                sprites.AddRange(_app.GetSprites());
                frame.AddRange(sprites);
            }
        }
    }
}
