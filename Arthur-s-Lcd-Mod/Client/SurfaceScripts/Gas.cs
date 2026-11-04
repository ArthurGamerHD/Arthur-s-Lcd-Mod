using System.Collections.Generic;
using Generated;
using IAutoScroll = LcdMod.Client.Terminal.Controls.Generic.IAutoScroll;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
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
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(GasApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class GasSurfaceScript : SurfaceScriptBase, IAutoScroll, IMultiDisplayMode, IUsesTerminalControl<ComboboxLinkType>
    {
        public const string ID = "GasGraph";
        public const string TITLE = MOD_PREFIX + "GasFilled";
        protected override string DefaultTitle => TITLE;
        
        public override IApp App => _app;
        GasApp _app;

        public GasSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        public override void SafeRun()
        {
            if (_app == null)
                _app = new GasApp(this);

            EnsureRetainedAppView(_app);
            UpdateViewBox();
            _app.Update();

            RenderSprites();
        }


        public override List<MySprite> GetSprites()
        {
            var sprites = BeginFrameSprites();
            if (_app == null || !_app.HasEntries)
            {
                AddEmptySprites(sprites);
                return sprites;
            }

            AddBackground(sprites);
            DrawRetainedAppView(sprites);
            return sprites;
        }
    }
}
