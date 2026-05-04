using Sandbox.Game.Entities;
using VRage.Audio;

namespace Graph.Apps.Utility
{
    public interface ISoundCapable
    {
        MyEntity3DSoundEmitter SoundEmitter { get; set; }
        void PlaySounds(MySoundPair id, bool force2d = false);
    }
}