using System.Collections.Generic;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Models;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps.Abstract
{
    public abstract class AppBase : IApp
    {
        protected AppBase(ScreenConfigGeneral config, IAppHost host)
        {
            AppConfig = config;
            Host = host;
        }

        protected IAppHost Host { get; private set; }
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
