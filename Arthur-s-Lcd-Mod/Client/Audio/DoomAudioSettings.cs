using LcdMod.Common.Config.Models;

namespace LcdMod.Client.Audio
{
    /// <summary>
    /// Persistent per-surface volume settings shared by the Doom app and its
    /// terminal controls. Values use ManagedDoom's native 0..15 volume range.
    /// </summary>
    public static class DoomAudioSettings
    {
        public const int MaxVolume = 15;
        public const int DefaultSfxVolume = 15;
        public const int DefaultMusicVolume = 8;

        const string CustomDataKey = "ManagedDoom.AudioVolumes";
        const int SfxIndex = 0;
        const int MusicIndex = 1;
        const int DataLength = 2;

        public static int GetSfxVolume(ScreenConfigGeneral config)
        {
            return GetVolume(config, SfxIndex, DefaultSfxVolume);
        }

        public static int GetMusicVolume(ScreenConfigGeneral config)
        {
            return GetVolume(config, MusicIndex, DefaultMusicVolume);
        }

        public static void SetSfxVolume(ScreenConfigGeneral config, int value)
        {
            SetVolume(config, SfxIndex, value);
        }

        public static void SetMusicVolume(ScreenConfigGeneral config, int value)
        {
            SetVolume(config, MusicIndex, value);
        }

        static int GetVolume(ScreenConfigGeneral config, int index, int defaultValue)
        {
            if (config == null)
                return defaultValue;

            var data = config.GetCustomData(CustomDataKey);
            if (data == null || data.Length < DataLength)
                return defaultValue;

            return Clamp(data[index]);
        }

        static void SetVolume(ScreenConfigGeneral config, int index, int value)
        {
            if (config == null)
                return;

            var oldData = config.GetCustomData(CustomDataKey);
            var data = new byte[DataLength];
            data[SfxIndex] = (byte)GetVolume(config, SfxIndex, DefaultSfxVolume);
            data[MusicIndex] = (byte)GetVolume(config, MusicIndex, DefaultMusicVolume);

            // Do not retain a caller-owned array from CustomData. Config sync
            // may serialize on another path while the terminal is being used.
            if (oldData != null && oldData.Length >= DataLength)
            {
                data[SfxIndex] = (byte)Clamp(oldData[SfxIndex]);
                data[MusicIndex] = (byte)Clamp(oldData[MusicIndex]);
            }

            data[index] = (byte)Clamp(value);
            config.SetCustomData(CustomDataKey, data);
        }

        static int Clamp(int value)
        {
            if (value < 0)
                return 0;
            if (value > MaxVolume)
                return MaxVolume;
            return value;
        }
    }
}
