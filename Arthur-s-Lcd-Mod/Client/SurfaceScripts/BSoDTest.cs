#if DEBUG
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(Apps.BSoDTestApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class BSoDTestSurfaceScript : SurfaceScriptBase
    {
        public const string ID = "BSoDTest";
        public const string TITLE = "LcdMod BSoD Test";

        readonly List<MySprite> _sprites = new List<MySprite>();
        protected override string DefaultTitle => TITLE;
        public override IApp App => null;

        public BSoDTestSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public override void SafeRun()
        {
            throw new InvalidOperationException("Intentional LcdMod BSoD test exception.");
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();
            return _sprites;
        }
    }
}
#endif
