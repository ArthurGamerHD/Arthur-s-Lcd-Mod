using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Client.Helpers
{
    // The missiles in flight inside a sphere. The game's missile index is asked first; seeking ammo (homing missiles,
    // seeking torpedoes) also lives in the general entity index, so a missile only that one returns is added from it.
    internal static class MissileScanner
    {
        static readonly List<MyEntity> Indexed = new List<MyEntity>();
        static readonly HashSet<long> Found = new HashSet<long>();

        public static void Collect(BoundingSphereD sphere, List<IMyMissile> result)
        {
            result.Clear();
            Found.Clear();
            if (MyAPIGateway.Missiles != null)
            {
                Indexed.Clear();
                MyAPIGateway.Missiles.GetAllMissilesInSphere(ref sphere, Indexed);
                for (int i = 0; i < Indexed.Count; i++)
                    Add(Indexed[i] as IMyMissile, result);
                Indexed.Clear();
            }
            List<IMyEntity> entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);
            if (entities == null)
                return;
            for (int i = 0; i < entities.Count; i++)
                Add(entities[i] as IMyMissile, result);
            entities.Clear();
        }

        static void Add(IMyMissile missile, List<IMyMissile> result)
        {
            if (missile != null && !missile.MarkedForClose && Found.Add(missile.EntityId))
                result.Add(missile);
        }
    }
}
