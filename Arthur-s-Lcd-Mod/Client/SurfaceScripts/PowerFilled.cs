using System.Collections.Generic;
using LcdMod.Common.Config.Components;
using Generated;
using IAutoScroll = LcdMod.Client.Terminal.Controls.Generic.IAutoScroll;
using LcdMod.Client.Gui;
using LcdMod.Client.Helpers;
using LcdMod.Client.Apps;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using VRage.ModAPI;
using LcdMod.Client.Apps.Abstract;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using ComboboxPowerLinkType = LcdMod.Client.Terminal.Controls.Generic.ComboboxPowerLinkType;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(PowerFilledApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class PowerFilledSurfaceScript : InteractiveSurfaceScript,
        IAutoScroll,
        IMultiDisplayMode,
        IUsesTerminalControl<ComboboxPowerLinkType>
    {
        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.CompactAndCard;
        }

        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public const string ID = "BatteryGraph";
        public const string TITLE = MOD_PREFIX + "PowerFilled";

        protected override string DefaultTitle => TITLE;

        public override IApp App => _app;
        PowerFilledApp _app;

        public PowerFilledSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
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
            base.SafeRun();

            if (_app == null)
                _app = new PowerFilledApp(this);

            EnsureRetainedAppView(_app);
            _app.Update();
            RenderSprites();
        }

        protected override bool RendersInParallel => true;


        public override List<MySprite> GetSprites()
        {
            var sprites = BeginFrameSprites();
            AddBackground(sprites);
            DrawRetainedAppView(sprites);
            if (!_app.HasVisibleItems())
                DrawMessage(sprites, LocHelper.Empty, "Warning", ColorComponent.ResolveWarningColor(), GeneralComponent.GetScale());
            return sprites;
        }
    }
}
