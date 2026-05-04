using System;
using System.Collections.Generic;
using System.IO;
using VRageMath;

namespace Graph.System.ScreenAreas
{
    public sealed class MinimalMwmModel
    {
        public readonly Dictionary<string, MinimalMwmScreenAreaGeometry> AreasByMaterial =
            new Dictionary<string, MinimalMwmScreenAreaGeometry>(StringComparer.OrdinalIgnoreCase);

        public readonly List<MinimalMwmLod> Lods = new List<MinimalMwmLod>();
        
        public int Version;
    }

    public sealed class MinimalMwmLod
    {
        public float Distance;
        public string Path;

        public override string ToString()
        {
            return string.IsNullOrEmpty(Path) ? base.ToString() : Path;
        }
    }

    public sealed class MinimalMwmScreenAreaGeometry
    {
        public int FirstTriangleIndex = -1;
        
        bool _hasBounds;
        bool _hasUvBounds;
        Vector3 _min;
        Vector3 _max;
        Vector2 _uvMin;
        Vector2 _uvMax;

#if DEBUG
        public readonly List<MinimalMwmTriangle> Triangles = new List<MinimalMwmTriangle>();
#endif
        public readonly List<int> TriangleIndices = new List<int>();
        public readonly List<MinimalMwmUvTriangle> UvTriangles = new List<MinimalMwmUvTriangle>();
        public readonly Dictionary<int, Vector2> UvByVertexIndex = new Dictionary<int, Vector2>();
        public string Material;
        public int TriangleCount;
        public double AreaM2;
        public bool HasUvBounds => _hasUvBounds;
        public Vector2 UvMin => _uvMin;
        public Vector2 UvMax => _uvMax;

        public double WidthM => _hasBounds ? Math.Max(Math.Max(_max.X - _min.X, _max.Y - _min.Y), _max.Z - _min.Z) : 0d;

        public double HeightM
        {
            get
            {
                if (!_hasBounds)
                    return 0d;

                var x = _max.X - _min.X;
                var y = _max.Y - _min.Y;
                var z = _max.Z - _min.Z;
                if (x > y)
                    Swap(ref x, ref y);
                if (y > z)
                    Swap(ref y, ref z);
                if (x > y)
                    Swap(ref x, ref y);
                return y;
            }
        }

        public double Aspect => HeightM > 0d ? WidthM / HeightM : 0d;

        public void AddTriangles(Vector3[] vertices, Vector2[] uvs, int[] indices, int triangleOffset)
        {
            if (vertices == null || indices == null)
                return;

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                var localTri = i / 3;
                TriangleIndices.Add(triangleOffset + localTri);

                var i0 = indices[i];
                var i1 = indices[i + 1];
                var i2 = indices[i + 2];

                if (i0 < 0 || i1 < 0 || i2 < 0 ||
                    i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                    continue;

                var a = vertices[i0];
                var b = vertices[i1];
                var c = vertices[i2];

#if DEBUG
                Triangles.Add(new MinimalMwmTriangle(a, b, c));
#endif
                if (uvs != null && i0 < uvs.Length && i1 < uvs.Length && i2 < uvs.Length)
                {
                    var uvA = uvs[i0];
                    var uvB = uvs[i1];
                    var uvC = uvs[i2];
                    UvTriangles.Add(new MinimalMwmUvTriangle(uvA, uvB, uvC));
                    AddVertexUv(i0, uvA);
                    AddVertexUv(i1, uvB);
                    AddVertexUv(i2, uvC);
                    IncludeUv(uvA);
                    IncludeUv(uvB);
                    IncludeUv(uvC);
                }
                else
                {
                    UvTriangles.Add(new MinimalMwmUvTriangle(Vector2.Zero, Vector2.Zero, Vector2.Zero));
                }

                AreaM2 += Vector3.Cross(b - a, c - a).Length() * 0.5d;
                TriangleCount++;
                Include(a);
                Include(b);
                Include(c);
            }
        }

        void Include(Vector3 value)
        {
            if (!_hasBounds)
            {
                _min = value;
                _max = value;
                _hasBounds = true;
                return;
            }

            _min = Vector3.Min(_min, value);
            _max = Vector3.Max(_max, value);
        }

        void IncludeUv(Vector2 value)
        {
            if (!_hasUvBounds)
            {
                _uvMin = value;
                _uvMax = value;
                _hasUvBounds = true;
                return;
            }

            _uvMin = Vector2.Min(_uvMin, value);
            _uvMax = Vector2.Max(_uvMax, value);
        }

        void AddVertexUv(int vertexIndex, Vector2 uv)
        {
            if (!UvByVertexIndex.ContainsKey(vertexIndex))
                UvByVertexIndex[vertexIndex] = uv;
        }

        static void Swap(ref float a, ref float b)
        {
            var t = a;
            a = b;
            b = t;
        }
    }

    public struct MinimalMwmUvTriangle
    {
        public readonly Vector2 A;
        public readonly Vector2 B;
        public readonly Vector2 C;

        public MinimalMwmUvTriangle(Vector2 a, Vector2 b, Vector2 c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    public struct MinimalMwmTriangle
    {
        public readonly Vector3 A;
        public readonly Vector3 B;
        public readonly Vector3 C;

        public MinimalMwmTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    public static class MinimalMwmReader
    {
        const int INDEXED_TAG_VERSION = 1066002;

        public static bool TryReadScreenArea(BinaryReader reader, string materialName, out MinimalMwmScreenAreaGeometry geometry)
        {
            geometry = null;
            if (string.IsNullOrWhiteSpace(materialName))
                return false;

            MinimalMwmModel model;
            if (!TryRead(reader, materialName, out model))
                return false;

            return model.AreasByMaterial.TryGetValue(materialName, out geometry) &&
                   geometry != null &&
                   geometry.TriangleCount > 0;
        }

        public static bool TryRead(BinaryReader reader, out MinimalMwmModel model)
        {
            return TryRead(reader, null, out model);
        }

        public static bool TryReadLods(BinaryReader reader, out List<MinimalMwmLod> lods)
        {
            lods = null;

            MinimalMwmModel model;
            if (!TryRead(reader, out model) || model == null || model.Lods.Count == 0)
                return false;

            lods = new List<MinimalMwmLod>(model.Lods);
            return true;
        }

        public static bool TryReadLodPaths(BinaryReader reader, out List<string> lodPaths)
        {
            lodPaths = null;

            List<MinimalMwmLod> lods;
            if (!TryReadLods(reader, out lods))
                return false;

            lodPaths = new List<string>(lods.Count);
            for (int i = 0; i < lods.Count; i++)
            {
                if (lods[i] != null && !string.IsNullOrWhiteSpace(lods[i].Path))
                    lodPaths.Add(lods[i].Path);
            }

            return lodPaths.Count > 0;
        }

        static bool TryRead(BinaryReader reader, string targetMaterial, out MinimalMwmModel model)
        {
            model = null;
            if (reader == null || reader.BaseStream == null || !reader.BaseStream.CanSeek)
                return false;

            var result = new MinimalMwmModel();
            try
            {
                var debugTag = reader.ReadString();
                if (!string.Equals(debugTag, "Debug", StringComparison.Ordinal))
                    return false;

                var debugLines = ReadStringArray(reader);
                result.Version = ReadVersion(debugLines);
                if (result.Version < INDEXED_TAG_VERSION)
                    return false;

                var tags = ReadIndexDictionary(reader);

                int lodsOffset;
                if (tags.TryGetValue("LODs", out lodsOffset))
                    TryReadLodsTag(reader, lodsOffset, result.Lods);
                
                int verticesOffset;
                int meshPartsOffset;
                int texCoordsOffset;
                int patternScaleOffset;
                if (!tags.TryGetValue("Vertices", out verticesOffset) ||
                    !tags.TryGetValue("MeshParts", out meshPartsOffset))
                {
                    model = result;
                    return result.Lods.Count > 0;
                }

                Vector3[] vertices;
                if (!TryReadVerticesTag(reader, verticesOffset, out vertices))
                {
                    model = result;
                    return result.Lods.Count > 0;
                }

                Vector2[] uvs = null;
                if (tags.TryGetValue("TexCoords0", out texCoordsOffset))
                {
                    float patternScale = 1f;
                    if (tags.TryGetValue("PatternScale", out patternScaleOffset))
                        TryReadPatternScaleTag(reader, patternScaleOffset, out patternScale);
                    TryReadTexCoordsTag(reader, texCoordsOffset, patternScale, Vector2.Zero, out uvs);
                }

                ReadMeshPartsTag(reader, meshPartsOffset, result.Version, vertices, uvs, result, targetMaterial);
                model = result;
                return true;
            }
            catch
            {
                model = null;
                return false;
            }
        }

        static int ReadVersion(string[] debugLines)
        {
            const string prefix = "Version:";
            if (debugLines == null)
                return 0;

            for (int i = 0; i < debugLines.Length; i++)
            {
                var line = debugLines[i];
                if (line == null || !line.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                int version;
                if (int.TryParse(line.Substring(prefix.Length), out version))
                    return version;
            }

            return 0;
        }

        static string[] ReadStringArray(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var result = new string[count];
            for (int i = 0; i < count; i++)
                result[i] = reader.ReadString();
            return result;
        }

        static Dictionary<string, int> ReadIndexDictionary(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var result = new Dictionary<string, int>(count, StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
                result[reader.ReadString()] = reader.ReadInt32();
            return result;
        }

        static bool TryReadVerticesTag(BinaryReader reader, int offset, out Vector3[] vertices)
        {
            vertices = null;
            if (!SeekTag(reader, offset, "Vertices"))
                return false;

            var count = reader.ReadInt32();
            vertices = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                var packed = reader.ReadUInt64();
                vertices[i] = new Vector3(
                    HalfToFloat((ushort)(packed & 0xffff)),
                    HalfToFloat((ushort)((packed >> 16) & 0xffff)),
                    HalfToFloat((ushort)((packed >> 32) & 0xffff)));
            }

            return true;
        }

        static bool TryReadTexCoordsTag(
            BinaryReader reader,
            int offset,
            float patternScale,
            Vector2 offsetUv,
            out Vector2[] uvs)
        {
            uvs = null;
            if (!SeekTag(reader, offset, "TexCoords0"))
                return false;

            if (Math.Abs(patternScale) < 1e-6f)
                patternScale = 1f;

            var count = reader.ReadInt32();
            uvs = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                var packed = reader.ReadUInt32();
                var vector = new Vector2(
                    HalfToFloat((ushort)(packed & 0xffff)),
                    HalfToFloat((ushort)((packed >> 16) & 0xffff))) / patternScale + offsetUv;
                uvs[i] = new Vector2(vector.X, 0f - vector.Y);
            }

            return true;
        }

        static bool TryReadLodsTag(BinaryReader reader, int offset, List<MinimalMwmLod> lods)
        {
            if (lods == null || !SeekTag(reader, offset, "LODs"))
                return false;

            var count = reader.ReadInt32();
            if (count < 0)
                return false;

            for (int i = 0; i < count; i++)
            {
                var distance = reader.ReadSingle();
                var path = reader.ReadString();
                SkipOptionalNullTerminator(reader);

                if (!string.IsNullOrWhiteSpace(path))
                {
                    lods.Add(new MinimalMwmLod
                    {
                        Distance = distance,
                        Path = path
                    });
                }
            }

            return true;
        }

        static void SkipOptionalNullTerminator(BinaryReader reader)
        {
            if (reader == null || reader.BaseStream == null || !reader.BaseStream.CanSeek)
                return;

            var stream = reader.BaseStream;
            if (stream.Position >= stream.Length)
                return;

            var position = stream.Position;
            if (reader.ReadByte() != 0)
                stream.Position = position;
        }

        static bool TryReadPatternScaleTag(BinaryReader reader, int offset, out float patternScale)
        {
            patternScale = 1f;
            if (!SeekTag(reader, offset, "PatternScale"))
                return false;

            patternScale = reader.ReadSingle();
            if (Math.Abs(patternScale) < 1e-6f)
                patternScale = 1f;
            return true;
        }

        static bool ReadMeshPartsTag(
            BinaryReader reader,
            int offset,
            int version,
            Vector3[] vertices,
            Vector2[] uvs,
            MinimalMwmModel model,
            string targetMaterial)
        {
            if (!SeekTag(reader, offset, "MeshParts"))
                return false;

            var count = reader.ReadInt32();
            var globalTriangleOffset = 0;

            for (int i = 0; i < count; i++)
            {
                reader.ReadInt32();
                if (version < 1052001)
                    reader.ReadInt32();

                var indices = ReadIntArray(reader);
                var partTriangleCount = indices != null ? indices.Length / 3 : 0;

                string material = null;
                if (reader.ReadBoolean())
                    material = ReadMaterialDescriptor(reader, version);

                if (!string.IsNullOrWhiteSpace(material) &&
                    (string.IsNullOrWhiteSpace(targetMaterial) ||
                     string.Equals(material, targetMaterial, StringComparison.OrdinalIgnoreCase)))
                {
                    MinimalMwmScreenAreaGeometry area;
                    if (!model.AreasByMaterial.TryGetValue(material, out area))
                    {
                        area = new MinimalMwmScreenAreaGeometry
                        {
                            Material = material,
                            FirstTriangleIndex = globalTriangleOffset
                        };
                        model.AreasByMaterial[material] = area;
                    }

                    area.AddTriangles(vertices, uvs, indices, globalTriangleOffset);
                }


                globalTriangleOffset += partTriangleCount;
            }

            return true;
        }

        static int[] ReadIntArray(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var result = new int[count];
            for (int i = 0; i < count; i++)
                result[i] = reader.ReadInt32();
            return result;
        }

        static string ReadMaterialDescriptor(BinaryReader reader, int version)
        {
            var materialName = reader.ReadString();
            if (version < 1052002)
            {
                reader.ReadString();
                reader.ReadString();
            }
            else
            {
                SkipStringDictionary(reader);
            }

            if (version >= 1068001)
                SkipStringDictionary(reader);

            if (version < 1157001)
            {
                for (int i = 0; i < 7; i++)
                    reader.ReadSingle();
            }

            var technique = version < 1052001 ? reader.ReadInt32().ToString() : reader.ReadString();
            if (technique == "GLASS")
            {
                if (version >= 1043001)
                {
                    reader.ReadString();
                    reader.ReadString();
                    reader.ReadBoolean();
                }
                else
                {
                    for (int i = 0; i < 4; i++)
                        reader.ReadSingle();
                }
            }

            return materialName;
        }

        static void SkipStringDictionary(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                reader.ReadString();
                reader.ReadString();
            }
        }

        static bool SeekTag(BinaryReader reader, int offset, string expectedTag)
        {
            reader.BaseStream.Position = offset;
            var tag = reader.ReadString();
            return string.Equals(tag, expectedTag, StringComparison.Ordinal);
        }

        static float HalfToFloat(ushort value)
        {
            var sign = (value & 0x8000) == 0 ? 1f : -1f;
            var exponent = (value >> 10) & 0x1f;
            var mantissa = value & 0x03ff;

            if (exponent == 0)
            {
                if (mantissa == 0)
                    return sign * 0f;

                return sign * (float)(Math.Pow(2d, -14d) * (mantissa / 1024d));
            }

            if (exponent == 31)
                return sign > 0f ? float.PositiveInfinity : float.NegativeInfinity;

            return sign * (float)(Math.Pow(2d, exponent - 15d) * (1d + mantissa / 1024d));
        }
    }
}
