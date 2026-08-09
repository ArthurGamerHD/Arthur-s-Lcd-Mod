using System.Collections.Generic;

namespace LcdMod.Client.Modules.Defense
{
    public sealed class DefenseSnapshot
    {
        public static readonly DefenseSnapshot Empty = new DefenseSnapshot(0L, new List<ShieldInfo>());

        public DefenseSnapshot(long gameplayFrame, List<ShieldInfo> shields)
        {
            GameplayFrame = gameplayFrame;
            Shields = shields ?? new List<ShieldInfo>();
        }

        public long GameplayFrame { get; private set; }
        public List<ShieldInfo> Shields { get; private set; }
        public ShieldInfo Primary => Shields.Count > 0 ? Shields[0] : null;

        internal void SetGameplayFrame(long gameplayFrame)
        {
            GameplayFrame = gameplayFrame;
        }
    }
}
