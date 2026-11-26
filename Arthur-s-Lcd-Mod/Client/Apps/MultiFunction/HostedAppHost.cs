using System.Collections.Generic;
using System.Text;
using Acrux.Animation;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Models;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Apps.MultiFunction
{
    /// <summary>
    /// The host of an app running inside a multi-function display: the display's surface, block and grid, with the
    /// app's own config and the area of its page or window.
    /// </summary>
    internal sealed class HostedAppHost : IAppHost
    {
        readonly SurfaceScriptBase _script;
        AppInstanceConfig _config;
        readonly string _title;
        readonly string _icon;
        RectangleF _viewBox;

        public HostedAppHost(SurfaceScriptBase script, AppInstanceConfig config, MultiFunctionAppInfo info)
        {
            _script = script;
            _config = config;
            _title = info.Name;
            _icon = info.Icon;
            _viewBox = script.ViewBox;
        }

        public SurfaceScriptBase Script => _script;

        /// <summary>The window the hosted app shows its dialogs above: its own page or window.</summary>
        public Acrux.Controls.Window DialogOwner { get; set; }

        public string Icon => _icon;
        public AnimationController Animations => _script.Animations;
        public IMyCubeBlock Block => _script.Block;
        public IMyTextSurface Surface => _script.Surface;
        public RectangleF ViewBox => _viewBox;
        public Color ForegroundColor => _script.ForegroundColor;
        public Color BackgroundColor => _script.BackgroundColor;

        // The page bar or the window's caption already names the app.
        public bool TitleVisible => false;
        public string Title => _title;
        public string TitleIcon => _script.TitleIcon;
        public IComponentContainer Config => _config;
        public int SurfaceIndex => _script.SurfaceIndex;
        public float ConfiguredScale => _script.ConfiguredScale;
        public GridLogic GridLogic => _script.GridLogic;
        public ScreenProviderConfig ProviderConfig => _script.ProviderConfig;

        // A config sync replaces every config object of the surface with a copy.
        internal void SetConfig(AppInstanceConfig config)
        {
            _config = config;
        }

        // Returns whether the area changed.
        public bool SetViewBox(RectangleF viewBox)
        {
            if (Equal(_viewBox, viewBox))
                return false;
            _viewBox = viewBox;
            return true;
        }

        static bool Equal(RectangleF left, RectangleF right)
        {
            return System.Math.Abs(left.X - right.X) < .5f && System.Math.Abs(left.Y - right.Y) < .5f &&
                   System.Math.Abs(left.Width - right.Width) < .5f && System.Math.Abs(left.Height - right.Height) < .5f;
        }

        public void AddBackground(List<MySprite> frame, Color? color = null) => _script.AddBackground(frame, color);
        public void DrawTitle(List<MySprite> frame) { }
        public void DrawMessage(List<MySprite> sprites, string message, string icon, Color color, float scale = 1f) =>
            _script.DrawMessage(sprites, message, icon, color, scale);
        public void DrawLoading(List<MySprite> sprites, float scale = 1f) => _script.DrawLoading(sprites, scale);
        public void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1f) =>
            _script.TrimText(ref sb, availableWidth, fontSize);
        public void RenderSprites() => _script.RenderSprites();
        public void RequestFrames(int frames = 10) => _script.RequestFrames(frames);

        public bool TryGetReferenceWorldMatrix(int referenceModeValue, out MatrixD world, bool useBlockWorldForCockpitAuto = false) =>
            _script.TryGetReferenceWorldMatrix(referenceModeValue, out world, useBlockWorldForCockpitAuto);
    }
}
