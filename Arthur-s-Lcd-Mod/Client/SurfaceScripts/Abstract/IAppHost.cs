using LcdMod.Client.Grid;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Models;
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
        void RenderSprites();
        bool TryGetReferenceWorldMatrix(int referenceModeValue, out MatrixD world, bool useBlockWorldForCockpitAuto = false);
    }
}
