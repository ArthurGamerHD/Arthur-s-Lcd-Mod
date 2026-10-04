using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Modules.Cartography.Maps
{
    public sealed class PlanetSnapshot
    {
        public MyPlanet Planet;
        public long EntityId;
        public string Name;
        public Vector3D Position;
        public float Radius;
        public float MaximumRadius;
        public bool HasAtmosphere;
    }

    public sealed class MapData
    {
        readonly List<PlanetSnapshot> _planets = new List<PlanetSnapshot>();
        readonly HashSet<IMyEntity> _entities = new HashSet<IMyEntity>();

        public IReadOnlyList<PlanetSnapshot> Planets => _planets;
        public Vector3D PlayerPosition { get; private set; }
        public MatrixD PlayerWorldMatrix { get; private set; }
        public bool HasPlayerWorldMatrix { get; private set; }

        public void RefreshPlanets()
        {
            _planets.Clear();
            _entities.Clear();
            if (MyAPIGateway.Entities == null)
                return;

            MyAPIGateway.Entities.GetEntities(_entities, IsPlanet);
            foreach (IMyEntity entity in _entities)
            {
                MyPlanet planet = entity as MyPlanet;
                if (planet == null || planet.MarkedForClose)
                    continue;
                _planets.Add(new PlanetSnapshot
                {
                    Planet = planet,
                    EntityId = planet.EntityId,
                    Name = string.IsNullOrEmpty(planet.Name) ? "Planet" : planet.Name,
                    Position = planet.PositionComp.GetPosition(),
                    Radius = planet.AverageRadius,
                    MaximumRadius = (float)Math.Max(planet.AverageRadius, planet.MaximumRadius),
                    HasAtmosphere = planet.HasAtmosphere
                });
            }
            UpdatePlayerPosition();
        }

        /// <summary>Overrides the player as the map reference, e.g. with the block an LCD belongs to.</summary>
        public Func<MatrixD?> ReferenceProvider { get; set; }

        public void UpdatePlayerPosition()
        {
            HasPlayerWorldMatrix = false;
            MatrixD? reference = ReferenceProvider == null ? null : ReferenceProvider();
            if (reference.HasValue)
            {
                PlayerWorldMatrix = reference.Value;
                PlayerPosition = reference.Value.Translation;
                HasPlayerWorldMatrix = true;
                return;
            }

            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null)
                return;

            MatrixD referenceWorld;
            if (TryGetCurrentPlayerWorldMatrix(out referenceWorld))
            {
                PlayerWorldMatrix = referenceWorld;
                PlayerPosition = referenceWorld.Translation;
                HasPlayerWorldMatrix = true;
                return;
            }

            PlayerPosition = MyAPIGateway.Session.Player.GetPosition();
        }

        /// <summary>
        /// Gets the live player/ship reference orientation without waiting for a
        /// map-data refresh. HUD elements can use this on every draw frame while
        /// the expensive map texture continues to update at a lower rate.
        /// </summary>
        public static bool TryGetCurrentPlayerWorldMatrix(out MatrixD world)
        {
            world = MatrixD.Identity;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null)
                return false;

            var controller = MyAPIGateway.Session.Player.Controller;
            var controlled = controller == null ? null : controller.ControlledEntity;
            IMyEntity controlledEntity = controlled == null ? null : controlled.Entity;
            return TryGetStableReferenceWorldMatrix(controlledEntity, out world);
        }

        /// <summary>
        /// Resolves the same stable ship reference used by the LCD planetary map.
        /// A controlled camera/turret can rotate through a gimbal pole and flip its
        /// local basis, so using ControlledEntity.WorldMatrix directly makes map
        /// up/down invert after the camera is recentered. Prefer the grid's main
        /// cockpit/remote controller, then the grid itself. Non-grid entities such
        /// as a character keep their own world matrix.
        /// </summary>
        static bool TryGetStableReferenceWorldMatrix(IMyEntity controlledEntity, out MatrixD world)
        {
            world = MatrixD.Identity;
            if (controlledEntity == null)
                return false;

            var cubeBlock = controlledEntity as IMyCubeBlock;
            var grid = cubeBlock == null ? null : cubeBlock.CubeGrid as MyCubeGrid;
            if (grid != null)
            {
                if (grid.MainCockpit != null)
                {
                    world = grid.MainCockpit.WorldMatrix;
                    return true;
                }

                if (grid.MainRemoteControl != null)
                {
                    world = grid.MainRemoteControl.WorldMatrix;
                    return true;
                }

                world = grid.WorldMatrix;
                return true;
            }

            world = controlledEntity.WorldMatrix;
            return true;
        }


        public PlanetSnapshot FindPlanet(long entityId)
        {
            if (entityId == 0L)
                return null;

            for (int i = 0; i < _planets.Count; i++)
            {
                if (_planets[i].EntityId == entityId)
                    return _planets[i];
            }
            return null;
        }

        public PlanetSnapshot FindPlayerPlanet(double maximumSurfaceDistanceMeters = 10000d)
        {
            PlanetSnapshot best = null;
            double bestSurfaceDistance = double.MaxValue;
            for (int i = 0; i < _planets.Count; i++)
            {
                PlanetSnapshot planet = _planets[i];
                double radius = Math.Max(planet.Radius, planet.MaximumRadius);
                double surfaceDistance = Math.Max(0d, Vector3D.Distance(PlayerPosition, planet.Position) - radius);
                if (surfaceDistance > maximumSurfaceDistanceMeters || surfaceDistance >= bestSurfaceDistance)
                    continue;
                bestSurfaceDistance = surfaceDistance;
                best = planet;
            }
            return best;
        }

        public PlanetSnapshot FindClosestPlanet()
        {
            PlanetSnapshot closest = null;
            double closestDistance = double.MaxValue;
            for (int i = 0; i < _planets.Count; i++)
            {
                PlanetSnapshot planet = _planets[i];
                double distance = Vector3D.Distance(PlayerPosition, planet.Position) - planet.Radius;
                if (distance >= closestDistance)
                    continue;
                closestDistance = distance;
                closest = planet;
            }
            return closest;
        }

        static bool IsPlanet(IMyEntity entity)
        {
            return entity is MyPlanet;
        }
    }
}
