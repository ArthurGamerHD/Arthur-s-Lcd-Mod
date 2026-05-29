using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Common.Config.Models;
using VRage.Game.GUI.TextPanel;

namespace LcdMod.Client.Apps
{
    public sealed class ButtonPadApp : AppBase, IAppInteractive
    {
        ScreenConfigInteractive Config;
        
        public ButtonPadApp(ScreenConfigInteractive config, IAppHost host) : base(config, host)
        {
            Config = config;
        }

        public override void Update()
        {
        }

        public override List<MySprite> GetSprites()
        {
            return  new List<MySprite>();
        }

        public List<ControlBase> InteractiveList { get; }

        public bool HasVisibleItems()
        {
            return true;
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
        }
    }
}
