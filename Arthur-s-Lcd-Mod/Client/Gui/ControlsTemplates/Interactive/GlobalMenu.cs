using System.Collections.Generic;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    /// <summary>
    /// Surface-script global menu host. The real visual/control tree is owned by <see cref="Menu"/>.
    /// This wrapper only adapts the existing surface-script API and exposes one root hit-test control.
    /// </summary>
    sealed class GlobalMenu : RectangleControl
    {
        readonly Menu _menu;

        public GlobalMenu(List<GlobalMenuEntry> entries)
            : base(default(RectangleF), CursorType.Default)
        {
            SetClass("ControlBase GlobalMenu");
            _menu = new Menu(entries);
            AddChild(_menu);
            SetVisible(_menu.Visible);
        }

        public void AddInteractiveEntries(List<Control> entries)
        {
            if (!Visible || entries == null || !_menu.HasMenuBounds)
                return;

            entries.Add(this);
        }

        public float GetReservedHeight(
            InteractiveSurfaceScript owner,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            return _menu.GetReservedHeight(owner, scale, fontScale, surface);
        }

        public void Render(
            InteractiveSurfaceScript owner,
            List<MySprite> targetSprites,
            RectangleF viewBox,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            Color textColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            HideEntries();

            if (!Visible || targetSprites == null || _menu == null || !_menu.Visible)
                return;

            IVisualStyleScope styleScope = owner?.App;
            if (styleScope == null)
                return;

            SetDataContext(owner.App);
            SetStyleParent(styleScope);
            SetResources(null);

            _menu.Configure(
                viewBox,
                owner != null ? owner.ViewBox.Width * 0.65f : viewBox.Width * 0.65f,
                surface,
                cursorPosition);

            _menu.Render(targetSprites);
            SetRect(_menu.HasMenuBounds ? _menu.MenuBounds : default(RectangleF));
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            // GlobalMenu is a hit-test host only. Its visuals are drawn by the
            // owner-aware Render(...) overload from InteractiveSurfaceScript.DrawTitle.
            // Rendering it through the generic interactive-entry path would otherwise
            // draw a default ControlBase rectangle behind the menu/flyouts.
        }

        public void HideEntries()
        {
            _menu.HideEntries();
            SetRect(default(RectangleF));
        }
    }
}
