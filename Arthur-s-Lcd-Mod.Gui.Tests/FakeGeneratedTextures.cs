using System.Reflection;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

/// <summary>Stands in for MyAPIGateway.GeneratedTextures and keeps the pixels written to each texture.</summary>
sealed class FakeGeneratedTextures : IDisposable
{
    readonly IMyGeneratedTextures _previous;
    public static readonly Dictionary<string, (Vector2I Size, byte[] Pixels)> Textures = new();

    public FakeGeneratedTextures()
    {
        _previous = MyAPIGateway.GeneratedTextures;
        Textures.Clear();
        MyAPIGateway.GeneratedTextures = DispatchProxy.Create<IMyGeneratedTextures, Factory>();
    }

    public void Dispose()
    {
        MyAPIGateway.GeneratedTextures = _previous;
        Textures.Clear();
    }

    public class Factory : DispatchProxy
    {
        int _created;

        protected override object Invoke(MethodInfo method, object[] args)
        {
            switch (method.Name)
            {
                case "get_IsSupported": return true;
                case "get_MaxTextureSide": return 4096;
                case "Create":
                    var texture = DispatchProxy.Create<IMyGeneratedTexture, Texture>();
                    var size = (Vector2I)args[1];
                    ((Texture)(object)texture).Init(args[0] + "#" + _created++, size);
                    return texture;
                default: throw new NotSupportedException(method.Name);
            }
        }
    }

    public class Texture : DispatchProxy
    {
        string _name;
        Vector2I _size;
        bool _valid;

        public void Init(string name, Vector2I size)
        {
            _name = name;
            _size = size;
            _valid = true;
            Textures[name] = (size, new byte[size.X * size.Y * 4]);
        }

        protected override object Invoke(MethodInfo method, object[] args)
        {
            switch (method.Name)
            {
                case "get_Name": return _valid ? _name : string.Empty;
                case "get_Size": return _size;
                case "get_IsValid": return _valid;
                case "get_ByteCount": return _size.X * _size.Y * 4;
                case "BeginUpdate": return _valid ? new byte[_size.X * _size.Y * 4] : null;
                case "EndUpdate":
                    Textures[_name] = (_size, (byte[])args[0]);
                    return null;
                case "Dispose":
                    _valid = false;
                    return null;
                default: throw new NotSupportedException(method.Name);
            }
        }
    }
}
