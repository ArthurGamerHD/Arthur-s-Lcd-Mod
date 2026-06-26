// ReSharper disable EmptyNamespace
namespace LcdMod.Common
{
}

namespace Sandbox.ModAPI
{
    public interface IMyTerminalBlock : VRage.Game.ModAPI.IMyCubeBlock
    {
    }

    public static class MyAPIGateway
    {
        public static MyUtilities Utilities { get; set; } = new MyUtilities();
    }

    public sealed class MyUtilities
    {
        public System.IO.BinaryWriter WriteBinaryFileInGlobalStorage(string name)
        {
            return new System.IO.BinaryWriter(new System.IO.MemoryStream(), System.Text.Encoding.UTF8, leaveOpen: false);
        }
    }
}

namespace VRage.Game.ModAPI
{
    public interface IMyCubeGrid
    {
        long EntityId { get; }
    }

    public interface IMyCubeBlock
    {
        IMyCubeGrid CubeGrid { get; }
    }

    public enum GridLinkTypeEnum
    {
        Logical,
        Physical,
        NoContactDamage,
        Mechanical,
        Electrical
    }
}

namespace VRageMath
{
    public struct Color
    {
        public uint PackedValue;
        
        public Color(byte r, byte g, byte b, byte a)
        {
        }
        public Color(byte r, byte g, byte b)
        {
        }
    }
}

namespace VRage.Game
{
    public struct MyDefinitionId
    {
        readonly string _value;

        MyDefinitionId(string value)
        {
            _value = value;
        }

        public static MyDefinitionId Parse(string value)
        {
            return new MyDefinitionId(value);
        }

        public override string ToString()
        {
            return _value ?? string.Empty;
        }
    }
}

namespace VRageMath
{
    public static class MathHelper
    {
        public static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }

        public static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}

namespace LcdMod.Common.Helpers
{
    public static class ErrorHandlerHelper
    {
        public static void LogError(System.Exception exception, object source)
        {
        }
    }

    public static class FactionHelperCommon
    {
        public static VRageMath.Color DefaultColor => default;

        public static VRageMath.Color GetIconColor(Sandbox.ModAPI.IMyTerminalBlock block)
        {
            return default;
        }
    }

    public sealed class FillSettings
    {
        public int UraniumLargeGridSmallReactor { get; set; }
        public int UraniumLargeGridLargeReactor { get; set; }
        public int UraniumSmallGridSmallReactor { get; set; }
        public int UraniumSmallGridLargeReactor { get; set; }
        public int AmmoDefaultPerWeapon { get; set; }
        public string[] WeaponOverrideKeys { get; set; }
        public int[] WeaponOverrideCounts { get; set; }
    }
}
