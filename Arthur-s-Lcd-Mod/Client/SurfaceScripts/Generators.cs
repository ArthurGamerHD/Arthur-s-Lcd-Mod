using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class GeneratorsSurfaceScript : SurfaceScriptBase, IMultiDisplayMode
    {
        protected override ConfigKind ConfigKind => ConfigKind.Power;
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
            if (AppConfig == null)
                return;

            if (_app == null)
                _app = new GeneratorsApp(AppConfig, this);

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
