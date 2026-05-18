using System.Collections.Generic;
using LcdMod.Common.Config.Models;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps.Abstract
{
    public abstract class AppBase : IApp
    {
        protected AppBase(ScreenConfigGeneral config, SurfaceScriptBase host)
        {
            AppConfig = config;
            Host = host;
        }

        protected SurfaceScriptBase Host { get; private set; }
        protected ScreenConfigGeneral AppConfig { get; private set; }

        public abstract void Update();

        public virtual void LayoutChanged()
        {
        }

        public abstract List<MySprite> GetSprites();

        public virtual void UpdateViewBox(RectangleF viewBox)
        {
        }
    }
}
