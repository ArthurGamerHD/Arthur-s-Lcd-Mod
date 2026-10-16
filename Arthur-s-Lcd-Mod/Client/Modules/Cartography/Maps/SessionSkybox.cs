using System;
using System.IO;
using Adk.Image;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Cartography.Maps
{
    /// <summary>One async texture cache per cartography session, shared by all map surfaces.</summary>
    internal sealed class SessionSkybox
    {
        string _path;
        bool _pending;
        bool _closed;
        bool _sessionCaptured;
        string _customSkybox;
        public SkyboxCubemap Cubemap { get; private set; }
        public Matrix WorldToCube { get; private set; } = Matrix.Identity;
        public event Action Changed;

        public void Update()
        {
            if (_closed || MyAPIGateway.Session == null || MyAPIGateway.Parallel == null ||
                MyAPIGateway.Utilities == null || MyDefinitionManager.Static == null)
                return;
            var environment = MyDefinitionManager.Static.EnvironmentDefinition;
            if (environment == null) return;
            // MyCommon uploads BackgroundOrientation without transposing it. HLSL's
            // column-major interpretation therefore applies its transpose to the ray.
            Matrix orientation = Matrix.Transpose(Matrix.CreateFromQuaternion(environment.EnvironmentOrientation.ToQuaternion()));
            if (orientation != WorldToCube)
            {
                WorldToCube = orientation;
                Changed?.Invoke();
            }
            if (!_sessionCaptured)
            {
                // The public ModAPI exposes CustomSkybox through the checkpoint.
                // Capture it once; constructing a checkpoint every frame is expensive.
                _customSkybox = MyAPIGateway.Session.GetCheckpoint(null).CustomSkybox;
                _sessionCaptured = true;
            }
            string path = string.IsNullOrEmpty(_customSkybox) ? environment.EnvironmentTexture : _customSkybox;
            if (_pending || string.Equals(path, _path, StringComparison.Ordinal)) return;
            _path = path;
            Cubemap = null;
            Changed?.Invoke();
            var mods = MyAPIGateway.Session.Mods.ToArray();
            SkyboxCubemap result = null;
            Exception failure = null;
            _pending = true;
            try
            {
                MyAPIGateway.Parallel.Start(() =>
                {
                    try { result = Load(path, mods); }
                    catch (Exception error) { failure = error; }
                }, () =>
                {
                    _pending = false;
                    if (_closed) return;
                    if (failure != null)
                        MyLog.Default.WriteLineAndConsole("LCD map skybox '" + path + "': " + failure.Message);
                    Cubemap = result;
                    Changed?.Invoke();
                });
            }
            catch
            {
                _pending = false;
                _path = null;
                throw;
            }
        }

        static SkyboxCubemap Load(string path, MyObjectBuilder_Checkpoint.ModItem[] mods)
        {
            string relative, failure;
            if (!ContentTexturePath.TryNormalize(path, out relative, out failure))
                throw new InvalidOperationException(failure);
            // Expanded definition paths identify their owning mod. Do not let a
            // different mod's identically named texture replace that explicit path.
            string normalized = path.Replace('\\', '/');
            bool expanded = !normalized.TrimStart('.','/').StartsWith("Textures/", StringComparison.OrdinalIgnoreCase);
            for (int i = mods.Length - 1; i >= 0; i--)
            {
                string root = mods[i].GetPath().Replace('\\', '/').TrimEnd('/') + "/";
                if (expanded && !normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInModLocation(relative, mods[i]))
                        return SkyboxCubemap.Load(reader.BaseStream);
                }
                catch (FileNotFoundException) { }
            }
            using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInGameContent(relative))
                return SkyboxCubemap.Load(reader.BaseStream);
        }

        public void Clear()
        {
            _closed = true;
            Cubemap = null;
            Changed = null;
        }

        public static Vector3 CubeDirection(Vector3 worldRay, Matrix worldToCube)
        {
            Vector3 ray = Vector3.TransformNormal(worldRay, worldToCube);
            // EnvAmbient.hlsli: ApplySkyboxOrientation; VSkybox is the negative ray.
            ray.Z = -ray.Z;
            return ray;
        }
    }
}
