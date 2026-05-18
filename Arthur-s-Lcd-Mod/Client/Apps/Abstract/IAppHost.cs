using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Grid;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Models;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using ScreenConfigGeneral = LcdMod.Common.Config.Models.ScreenConfigGeneral;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.SurfaceScripts.Abstract
{
    public interface IAppHost
    {
        IMyCubeBlock Block { get; }
        IMyTextSurface Surface { get; }
        RectangleF ViewBox { get; }
        float Scale { get; set; }
        Color ForegroundColor { get; }
        Color BackgroundColor { get; }
        bool TitleVisible { get; }
        string Title { get; }
        ScreenConfigGeneral Config { get; }
        GridLogic GridLogic { get; }
        ScreenProviderConfig ProviderConfig { get; }
        void AddBackground(List<MySprite> frame, Color? color = null);
        void DrawTitle(List<MySprite> frame);
        void DrawMessage(List<MySprite> sprites, string message, string icon, Color color, float scale = 1f);
        void DrawLoading(List<MySprite> sprites, float scale = 1f);
        void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1f);
        void RenderSprites();
        bool TryGetReferenceWorldMatrix(int referenceModeValue, out MatrixD world, bool useBlockWorldForCockpitAuto = false);
    }
}
