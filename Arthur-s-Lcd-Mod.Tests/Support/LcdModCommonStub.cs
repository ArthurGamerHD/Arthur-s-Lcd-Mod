// ReSharper disable All
namespace LcdMod.Common
{
}

namespace Sandbox.ModAPI
{
    public interface IMyTerminalBlock : VRage.Game.ModAPI.IMyCubeBlock
    {
    }

    // ReSharper disable once InconsistentNaming
    public static class MyAPIGateway
    {
        public static MyUtilities Utilities { get; set; } = new MyUtilities();
        public static MySession Session { get; set; } = new MySession();
    }

    public sealed class MySession
    {
        public List<VRage.Game.MyObjectBuilder_Checkpoint.ModItem> Mods { get; set; } =
            new List<VRage.Game.MyObjectBuilder_Checkpoint.ModItem>();
    }

    public sealed class MyUtilities
    {
        public string GameContentRoot { get; set; } = string.Empty;

        public BinaryWriter WriteBinaryFileInGlobalStorage(string name)
        {
            return new BinaryWriter(new MemoryStream(), System.Text.Encoding.UTF8, leaveOpen: false);
        }

        public bool FileExistsInGameContent(string name)
        {
            return File.Exists(ResolveGameContentPath(name));
        }

        public bool FileExistsInModLocation(
            string name,
            VRage.Game.MyObjectBuilder_Checkpoint.ModItem mod)
        {
            return false;
        }

        public BinaryReader ReadBinaryFileInGameContent(string name)
        {
            return new BinaryReader(File.OpenRead(ResolveGameContentPath(name)));
        }

        public BinaryReader ReadBinaryFileInModLocation(
            string name,
            VRage.Game.MyObjectBuilder_Checkpoint.ModItem mod)
        {
            throw new FileNotFoundException(name);
        }

        string ResolveGameContentPath(string name)
        {
            if (Path.IsPathRooted(name))
                return name;

            return string.IsNullOrEmpty(GameContentRoot)
                ? name
                : Path.Combine(GameContentRoot, name);
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
    [ProtoBuf.ProtoContract]
    public struct Color
    {
        [ProtoBuf.ProtoMember(1)]
        public uint PackedValue;
        
        public Color(byte r, byte g, byte b, byte a)
        {
            PackedValue = (uint)(r | g << 8 | b << 16 | a << 24);
        }

        public Color(byte r, byte g, byte b)
        {
            PackedValue = (uint)(r | g << 8 | b << 16 | byte.MaxValue << 24);
        }
    }
}

namespace VRage.Game
{
    public sealed class MyObjectBuilder_Checkpoint
    {
        public sealed class ModItem
        {
        }
    }

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
        public static void LogError(Exception exception, object source)
        {
        }
    }

    public static class FactionHelperCommon
    {
        public static VRageMath.Color DefaultColor => default;

        public static VRageMath.Color GetAccent(Sandbox.ModAPI.IMyTerminalBlock block)
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
