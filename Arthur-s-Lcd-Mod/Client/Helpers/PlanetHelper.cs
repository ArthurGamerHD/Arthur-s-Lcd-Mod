using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Client.Helpers
{
    public static class PlanetHelper
    {
        public struct PlanetTextureStyle
        {
            public Color BaseColor;
            public Color? PolarCapColor;
            public Color? EquatorColor;
        }

        public static readonly PlanetTextureStyle DefaultPlanetTexture = new PlanetTextureStyle
        {
            BaseColor = new Color(70, 185, 90),
            PolarCapColor = null,
            EquatorColor = null
        };

        public static readonly Dictionary<string, PlanetTextureStyle> PlanetTexturesByName =
            new Dictionary<string, PlanetTextureStyle>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Mars",
                    new PlanetTextureStyle
                        { BaseColor = new Color(181, 94, 56), PolarCapColor = Color.White, EquatorColor = null }
                },
                {
                    "Titan",
                    new PlanetTextureStyle
                        { BaseColor = new Color(201, 110, 52), PolarCapColor = null, EquatorColor = null }
                },
                {
                    "Alien",
                    new PlanetTextureStyle
                        { BaseColor = new Color(140, 255, 60), PolarCapColor = Color.Cyan, EquatorColor = Color.Wheat }
                },
                {
                    "Europa",
                    new PlanetTextureStyle
                        { BaseColor = new Color(120, 190, 255), PolarCapColor = null, EquatorColor = null }
                },
                {
                    "Triton",
                    new PlanetTextureStyle
                        { BaseColor = new Color(235, 245, 255), PolarCapColor = null, EquatorColor = null }
                },
                {
                    "Pertam",
                    new PlanetTextureStyle
                        { BaseColor = new Color(214, 182, 120), PolarCapColor = null, EquatorColor = null }
                },
                {
                    "Pertan",
                    new PlanetTextureStyle
                        { BaseColor = new Color(214, 182, 120), PolarCapColor = null, EquatorColor = null }
                },
                {
                    "EarthLike",
                    new PlanetTextureStyle
                        { BaseColor = new Color(86, 168, 92), PolarCapColor = Color.White, EquatorColor = Color.Wheat }
                },
                {
                    "Moon",
                    new PlanetTextureStyle
                        { BaseColor = new Color(148, 148, 148), PolarCapColor = null, EquatorColor = null }
                }
            };

        public static readonly Dictionary<long, MyPlanet> PlanetsById = new Dictionary<long, MyPlanet>();
        public static readonly Dictionary<long, string> PlanetNamesById = new Dictionary<long, string>();
        public static readonly Dictionary<long, string> PlanetGeneratorNamesById = new Dictionary<long, string>();

        static readonly HashSet<IMyEntity> PlanetEntities = new HashSet<IMyEntity>();

        public static void OnEntityAdded(IMyEntity entity)
        {
            if (entity is MyPlanet)
                RefreshPlanets();
        }

        public static void RefreshPlanets()
        {
            PlanetsById.Clear();
            PlanetNamesById.Clear();
            PlanetGeneratorNamesById.Clear();
            PlanetEntities.Clear();

            if (MyAPIGateway.Entities == null)
                return;

            MyAPIGateway.Entities.GetEntities(PlanetEntities, e => e is MyPlanet);
            foreach (var entity in PlanetEntities)
            {
                var planet = entity as MyPlanet;
                if (planet == null)
                    continue;

                PlanetsById[planet.EntityId] = planet;
                PlanetNamesById[planet.EntityId] = planet.Name ?? string.Empty;
                PlanetGeneratorNamesById[planet.EntityId] = GetPlanetGeneratorName(planet);
            }
        }

        public static void Clear()
        {
            PlanetsById.Clear();
            PlanetNamesById.Clear();
            PlanetGeneratorNamesById.Clear();
            PlanetEntities.Clear();
        }

        public static string GetPlanetGeneratorName(MyPlanet planet)
        {
            if (planet == null)
                return string.Empty;

            var generator = planet.Generator;
            if (generator == null)
                return string.Empty;

            var subtype = generator.Id.SubtypeName;
            return string.IsNullOrWhiteSpace(subtype) ? string.Empty : subtype;
        }

        public static PlanetTextureStyle ResolvePlanetTexture(string planetName)
        {
            if (string.IsNullOrWhiteSpace(planetName))
                return DefaultPlanetTexture;

            PlanetTextureStyle texture;
            if (PlanetTexturesByName.TryGetValue(planetName, out texture))
                return texture;

            return new PlanetTextureStyle
            {
                BaseColor = GetDeterministicPlanetColor(planetName),
                PolarCapColor = null,
                EquatorColor = null
            };
        }

        public static Color GetDeterministicPlanetColor(string planetName)
        {
            if (string.IsNullOrWhiteSpace(planetName))
                return DefaultPlanetTexture.BaseColor;

            var normalized = planetName.Trim().ToLowerInvariant();
            uint hash = 2166136261u; // FNV-1a 32-bit offset basis
            for (int i = 0; i < normalized.Length; i++)
            {
                hash ^= normalized[i];
                hash *= 16777619u; // FNV prime
            }

            float hue = (hash % 360u) / 360f;
            float saturation = 0.55f + (((hash >> 9) & 0xFFu) / 255f) * 0.25f; // 0.55..0.80
            float value = 0.60f + (((hash >> 17) & 0xFFu) / 255f) * 0.25f; // 0.60..0.85

            return HsvToColor(hue, saturation, value);
        }

        static Color HsvToColor(float h, float s, float v)
        {
            h = MathHelper.Clamp(h, 0f, 1f);
            s = MathHelper.Clamp(s, 0f, 1f);
            v = MathHelper.Clamp(v, 0f, 1f);

            if (s <= 0.0001f)
            {
                byte gray = (byte)MathHelper.Clamp((int)(v * 255f), 0, 255);
                return new Color(gray, gray, gray);
            }

            float hh = h * 6f;
            int sector = (int)Math.Floor(hh);
            float f = hh - sector;
            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));

            float r;
            float g;
            float b;

            switch (sector % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return new Color(
                (byte)MathHelper.Clamp((int)(r * 255f), 0, 255),
                (byte)MathHelper.Clamp((int)(g * 255f), 0, 255),
                (byte)MathHelper.Clamp((int)(b * 255f), 0, 255));
        }

    }
}
