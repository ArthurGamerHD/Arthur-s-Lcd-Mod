using LcdMod.Common.Config.Components;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Persistent per-surface settings for Doom's native input screen.
    /// The first eight bytes retain the older cockpit setting for compatibility.
    /// </summary>
    public static class DoomInputSettings
    {
        public const int MinKeyboardTurnSensitivity = 25;
        public const int MaxKeyboardTurnSensitivity = 200;
        public const int DefaultKeyboardTurnSensitivity = 100;

        const string CustomDataKey = "ManagedDoom.InputCockpit";
        const int CockpitDataLength = 8;
        const int KeyboardTurnSensitivityIndex = 8;
        const int FlagsIndex = 9;
        const int DataLength = 10;
        const byte MouseTurningFlag = 1;
        const byte MouseTurningConfiguredFlag = 2;

        public static long GetCockpitEntityId(GeneralConfigComponent config)
        {
            if (config == null)
                return 0L;

            var data = config.GetCustomData(CustomDataKey);
            if (data == null || data.Length < CockpitDataLength)
                return 0L;

            ulong value = 0UL;
            for (int i = 0; i < CockpitDataLength; i++)
                value |= (ulong)data[i] << (i * 8);

            return unchecked((long)value);
        }

        public static void SetCockpitEntityId(GeneralConfigComponent config, long entityId)
        {
            if (config == null)
                return;

            var data = ReadData(config);
            ulong value = unchecked((ulong)entityId);

            for (int i = 0; i < CockpitDataLength; i++)
                data[i] = (byte)(value >> (i * 8));

            config.SetCustomData(CustomDataKey, data);
        }

        public static int GetKeyboardTurnSensitivity(GeneralConfigComponent config)
        {
            if (config == null)
                return DefaultKeyboardTurnSensitivity;

            var data = config.GetCustomData(CustomDataKey);
            if (data == null || data.Length <= KeyboardTurnSensitivityIndex)
                return DefaultKeyboardTurnSensitivity;

            return ClampKeyboardTurnSensitivity(data[KeyboardTurnSensitivityIndex]);
        }

        public static void SetKeyboardTurnSensitivity(GeneralConfigComponent config, int value)
        {
            if (config == null)
                return;

            var data = ReadData(config);
            data[KeyboardTurnSensitivityIndex] =
                (byte)ClampKeyboardTurnSensitivity(value);
            config.SetCustomData(CustomDataKey, data);
        }

        public static bool GetMouseTurningEnabled(GeneralConfigComponent config)
        {
            if (config == null)
                return true;

            var data = config.GetCustomData(CustomDataKey);
            // Older configs did not record an explicit off choice. Mouse turning
            // is on by default for the native input screen.
            return data == null || data.Length <= FlagsIndex ||
                   (data[FlagsIndex] & MouseTurningConfiguredFlag) == 0 ||
                   (data[FlagsIndex] & MouseTurningFlag) != 0;
        }

        public static void SetMouseTurningEnabled(GeneralConfigComponent config, bool enabled)
        {
            if (config == null)
                return;

            var data = ReadData(config);
            data[FlagsIndex] |= MouseTurningConfiguredFlag;
            if (enabled)
                data[FlagsIndex] |= MouseTurningFlag;
            else
                data[FlagsIndex] &= unchecked((byte)~MouseTurningFlag);

            config.SetCustomData(CustomDataKey, data);
        }

        static byte[] ReadData(GeneralConfigComponent config)
        {
            var oldData = config.GetCustomData(CustomDataKey);
            var data = new byte[DataLength];

            if (oldData != null)
            {
                int count = oldData.Length < DataLength
                    ? oldData.Length
                    : DataLength;

                for (int i = 0; i < count; i++)
                    data[i] = oldData[i];
            }

            if (oldData == null || oldData.Length <= KeyboardTurnSensitivityIndex)
            {
                data[KeyboardTurnSensitivityIndex] =
                    (byte)DefaultKeyboardTurnSensitivity;
            }

            return data;
        }

        static int ClampKeyboardTurnSensitivity(int value)
        {
            if (value < MinKeyboardTurnSensitivity)
                return MinKeyboardTurnSensitivity;
            if (value > MaxKeyboardTurnSensitivity)
                return MaxKeyboardTurnSensitivity;
            return value;
        }
    }
}
