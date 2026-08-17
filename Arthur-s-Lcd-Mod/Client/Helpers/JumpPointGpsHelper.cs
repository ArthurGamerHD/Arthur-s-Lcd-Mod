using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Helpers
{
    /// <summary>
    /// Calculates safe planetary jump points and manages their short-lived local GPS markers.
    /// The caller supplies the grid context; no grid service state is required.
    /// </summary>
    internal sealed class JumpPointGpsHelper
    {
        static readonly TimeSpan GpsTtl = TimeSpan.FromSeconds(60);

        readonly Dictionary<long, Vector3D> _pointsByPlanet = new Dictionary<long, Vector3D>();
        readonly Dictionary<long, GpsEntry> _gpsByPlanet = new Dictionary<long, GpsEntry>();
        long _cacheFrame = -1;

        public bool TryGetJumpPoint(
            IMyCubeGrid grid,
            long planetId,
            string planetName,
            Vector3D planetCenter,
            double planetRadiusMeters,
            double gravityRangeMeters,
            out Vector3D jumpPoint,
            bool publish = true)
        {
            jumpPoint = Vector3D.Zero;
            if (grid == null || grid.Closed || grid.MarkedForClose)
                return false;

            var frame = MyAPIGateway.Session != null
                ? MyAPIGateway.Session.GameplayFrameCounter
                : -1;
            if (_cacheFrame != frame)
            {
                _pointsByPlanet.Clear();
                _cacheFrame = frame;
            }

            if (!_pointsByPlanet.TryGetValue(planetId, out jumpPoint))
            {
                jumpPoint = CalculateJumpPoint(
                    grid.GetPosition(),
                    planetCenter,
                    planetRadiusMeters,
                    gravityRangeMeters);
                _pointsByPlanet[planetId] = jumpPoint;
            }

            if (publish)
                Publish(grid.CustomName, planetId, planetName, jumpPoint, frame);
            return true;
        }

        public void Clear()
        {
            _pointsByPlanet.Clear();
            _gpsByPlanet.Clear();
            _cacheFrame = -1;
        }

        static Vector3D CalculateJumpPoint(
            Vector3D referencePosition,
            Vector3D planetCenter,
            double planetRadiusMeters,
            double gravityRangeMeters)
        {
            var direction = referencePosition - planetCenter;
            if (direction.LengthSquared() <= 1e-6)
                direction = Vector3D.Forward;
            else
                direction.Normalize();

            var offsetMeters = Math.Max(0d, planetRadiusMeters + gravityRangeMeters + 10d);
            return planetCenter + direction * offsetMeters;
        }

        void Publish(
            string gridName,
            long planetId,
            string planetName,
            Vector3D jumpPoint,
            long frame)
        {
            if (frame < 0 || MyAPIGateway.Session == null || MyAPIGateway.Session.GPS == null)
                return;

            GpsEntry entry;
            _gpsByPlanet.TryGetValue(planetId, out entry);
            if (frame - entry.LastPublishedFrame < 60)
                return;

            var gps = entry.Gps;
            var discardAt = MyAPIGateway.Session.ElapsedPlayTime + GpsTtl;
            if (gps == null ||
                (gps.DiscardAt.HasValue && gps.DiscardAt.Value <= MyAPIGateway.Session.ElapsedPlayTime))
            {
                gps = MyAPIGateway.Session.GPS.Create(
                    BuildGpsName(gridName, planetName),
                    string.Empty,
                    jumpPoint,
                    true,
                    true);
                if (gps == null)
                    return;

                gps.DiscardAt = discardAt;
                MyAPIGateway.Session.GPS.AddLocalGps(gps);
                entry.Gps = gps;
            }
            else
            {
                gps.Coords = jumpPoint;
                gps.DiscardAt = discardAt;
            }

            entry.LastPublishedFrame = frame;
            _gpsByPlanet[planetId] = entry;
        }

        static string BuildGpsName(string gridName, string planetName)
        {
            return "JumpPoint_" + NormalizeNameToken(gridName) + "_" + NormalizeNameToken(planetName);
        }

        static string NormalizeNameToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var chars = new List<char>(value.Length);
            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character) || character == '_' || character == '-')
                    chars.Add(character);
                else if (char.IsWhiteSpace(character))
                    chars.Add('_');
            }

            var token = FormatingHelper.TrimName(new string(chars.ToArray()));
            return token.Length == 0 ? "Unknown" : token;
        }

        struct GpsEntry
        {
            public IMyGps Gps;
            public long LastPublishedFrame;
        }
    }
}
