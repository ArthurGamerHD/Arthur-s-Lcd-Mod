using Sandbox.Game.Entities;

namespace LcdMod.Client.Utility
{
    public interface ISoundCapable
    {
        MyEntity3DSoundEmitter SoundEmitter { get; set; }
        void PlaySounds(MySoundPair id, bool force2D = false);
    }
}