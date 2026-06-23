using LcdMod.Common.Config.Models;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Persistent per-surface settings used by Doom's cockpit input bridge.
    /// The first eight bytes retain the selected cockpit entity id so settings
    /// written by older versions remain compatible.
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

        public static long GetCockpitEntityId(ScreenConfigGeneral config)
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

        public static void SetCockpitEntityId(ScreenConfigGeneral config, long entityId)
        {
            if (config == null)
                return;

            var data = ReadData(config);
            ulong value = unchecked((ulong)entityId);

            for (int i = 0; i < CockpitDataLength; i++)
                data[i] = (byte)(value >> (i * 8));

            config.SetCustomData(CustomDataKey, data);
        }

        public static int GetKeyboardTurnSensitivity(ScreenConfigGeneral config)
        {
            if (config == null)
                return DefaultKeyboardTurnSensitivity;

            var data = config.GetCustomData(CustomDataKey);
            if (data == null || data.Length <= KeyboardTurnSensitivityIndex)
                return DefaultKeyboardTurnSensitivity;

            return ClampKeyboardTurnSensitivity(data[KeyboardTurnSensitivityIndex]);
        }

        public static void SetKeyboardTurnSensitivity(ScreenConfigGeneral config, int value)
        {
            if (config == null)
                return;

            var data = ReadData(config);
            data[KeyboardTurnSensitivityIndex] =
                (byte)ClampKeyboardTurnSensitivity(value);
            config.SetCustomData(CustomDataKey, data);
        }

        public static bool GetMouseTurningEnabled(ScreenConfigGeneral config)
        {
            if (config == null)
                return false;

            var data = config.GetCustomData(CustomDataKey);
            return data != null &&
                data.Length > FlagsIndex &&
                (data[FlagsIndex] & MouseTurningFlag) != 0;
        }

        public static void SetMouseTurningEnabled(ScreenConfigGeneral config, bool enabled)
        {
            if (config == null)
                return;

            var data = ReadData(config);
            if (enabled)
                data[FlagsIndex] |= MouseTurningFlag;
            else
                data[FlagsIndex] &= unchecked((byte)~MouseTurningFlag);

            config.SetCustomData(CustomDataKey, data);
        }

        static byte[] ReadData(ScreenConfigGeneral config)
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
