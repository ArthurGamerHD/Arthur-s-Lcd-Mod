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
    [LcdSurface(typeof(GeneratorsApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class GeneratorsSurfaceScript : InteractiveSurfaceScript, IAutoScroll, IMultiDisplayMode,
        IUsesTerminalControl<ComboboxPowerLinkType>
    {
        public const string ID = "GeneratorsGraph";
        public const string TITLE = "RadialMenuGroupTitle_Power";
        GeneratorsApp _app;

        public override IApp App => _app;
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

            EnsureRetainedAppView(_app);
            _app.Update();
            RenderSprites();
        }


        public override List<MySprite> GetSprites()
        {
            var sprites = BeginFrameSprites();
            AddBackground(sprites);
            if (_app != null)
                DrawRetainedAppView(sprites);
            return sprites;
        }
    }
}
