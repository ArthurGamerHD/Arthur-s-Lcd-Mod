using System.Collections.Generic;
using System.Text;
using Acrux.Animation;
using LcdMod.Client.GridData;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Models;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Apps.Abstract
{
    public interface IAppHost
    {
        AnimationController Animations { get; }
        IMyCubeBlock Block { get; }
        IMyTextSurface Surface { get; }
        RectangleF ViewBox { get; }
        Color ForegroundColor { get; }
        Color BackgroundColor { get; }
        bool TitleVisible { get; }
        string Title { get; }
        string TitleIcon { get; }
        IComponentContainer Config { get; }
        int SurfaceIndex { get; }
        float ConfiguredScale { get; }
        GridLogic GridLogic { get; }
        ScreenProviderConfig ProviderConfig { get; }
        void AddBackground(List<MySprite> frame, Color? color = null);
        void DrawTitle(List<MySprite> frame);
        void DrawMessage(List<MySprite> sprites, string message, string icon, Color color, float scale = 1f);
        void DrawLoading(List<MySprite> sprites, float scale = 1f);
        void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1f);
        void RenderSprites();

        /// <summary>
        /// Runs the screen's update on every game tick for the next <paramref name="frames"/> ticks instead of
        /// the script's own cadence, so a screen with a scene still settling (a moving 3D camera, a frame in
        /// flight) animates at the game's rate. Requests extend, never shorten, the current burst.
        /// </summary>
        void RequestFrames(int frames = 10);
        bool TryGetReferenceWorldMatrix(int referenceModeValue, out MatrixD world, bool useBlockWorldForCockpitAuto = false);
    }
}
