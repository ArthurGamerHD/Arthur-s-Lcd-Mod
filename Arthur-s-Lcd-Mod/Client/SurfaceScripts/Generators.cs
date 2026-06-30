using System.Collections.Generic;
using Generated;
using IAutoScroll = LcdMod.Client.Terminal.Controls.Generic.IAutoScroll;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Client.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.GeneratorsApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class GeneratorsSurfaceScript : InteractiveSurfaceScript, IAutoScroll, IMultiDisplayMode,
        IUsesTerminalControl<ComboboxLinkType>
    {
        public const string ID = "GeneratorsGraph";
        public const string TITLE = "RadialMenuGroupTitle_Power";
        GeneratorsApp _app;

        public override IApp App => _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override List<Control> InteractiveList => _app.VisualChildren as List<Control>;
        protected override bool RendersInteractiveEntriesInGetSprites => true;
        protected override string DefaultTitle => TITLE;

        public GeneratorsSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
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
                _app = new GeneratorsApp(this);

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            DrawTitle(sprites);
            if (_app != null)
                sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
