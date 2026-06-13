using System.Collections.Generic;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Apps.Abstract
{
    public interface IApp
    {
        void Update();
        void LayoutChanged();
        List<MySprite> GetSprites();
        IReadOnlyList<Control> Children { get; }
        bool HasVisibleItems();
        void OnMouseScroll(int delta, ref bool handled);
    }

    public interface IThemedApp : IApp, IVisualStyleScope
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
