using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps.Abstract
{
    public interface IApp
    {
        void Update();
        void LayoutChanged();
        List<MySprite> GetSprites();
    }

    public interface IThemedApp : IApp
    {
        IReadOnlyDictionary<string, Color> Theme { get; }
        ControlRenderContext CreateControlRenderContext(
            IMyTextSurface surface,
            float scale,
            float fontScale,
            Vector2 cursorPosition);
        Color GetThemeColor(string role);
    }
}
