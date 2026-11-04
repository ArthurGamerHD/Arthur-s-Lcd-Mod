using System.Text;

namespace LcdMod.Common.Ships
{
    // What a scan learns of a ship, for its identification code.
    internal struct ShipTraits
    {
        public bool Station;
        public bool Large;
        public bool JumpDrive;
        public bool SafeZone;
        public bool Wheels;
        // No seat, but a remote control or an AI block flies it.
        public bool Unmanned;
        // The Water mod is in the session, so a ship can float.
        public bool WaterWorld;

        public bool AtmosphericThrust;
        public bool IonThrust;
        public bool HydrogenThrust;
        // Thrusters of a kind the game does not ship (modded).
        public bool ExoticThrust;
        public bool GravityDrive;
        // Its thrust lifts it against an Earth-like gravity (atmospheric and hydrogen), or the Moon's (ion and hydrogen).
        public bool LiftsInAtmosphere;
        public bool LiftsOnMoon;

        public bool Reactor;
        public bool HydrogenEngine;
        public bool CleanEnergy;
        public bool Battery;

        public int Blocks;
        public int ArmorBlocks;
        public int HeavyArmorBlocks;

        // Weapons make up a good share of what it is built to do (IsCombat), not one turret on a freighter.
        public bool Combat;
        public bool Cargo;
        public bool Trade;
        public bool Production;
        public bool Exploration;
        public bool Welder;
        public bool Grinder;
        public bool Drill;
    }

    // The identification code [abc-123]: a, its kind; b, its power; c, its armor; 1, its role; 2, its propulsion;
    // 3, its tools when it is a support craft. Parts are left out as the scheme's notes say.
    internal static class ShipIdCode
    {
        // Armor below this share of the blocks: unarmored. Over this share of the armor in one kind: light or heavy;
        // in between, mixed.
        const float ARMORED_SHARE = 0.05f;
        const float ONE_KIND_SHARE = 0.9f;
        // Combat-oriented: weapons at least this share of the functional blocks, and at least this many.
        const float COMBAT_SHARE = 0.08f;
        const int COMBAT_MINIMUM_WEAPONS = 2;

        // Functional blocks: those with a terminal, leaving out lights (armor, decoration and conveyors have none).
        public static bool IsCombat(int weapons, int functionalBlocks)
        {
            return weapons >= COMBAT_MINIMUM_WEAPONS && weapons >= functionalBlocks * COMBAT_SHARE;
        }

        public static string Compose(ShipTraits ship)
        {
            char kind = Kind(ship);
            char role = Role(ship);
            char armor = Armor(ship);
            var code = new StringBuilder("[");
            code.Append(kind).Append(Power(ship));
            // Note 4: light armor goes without saying.
            if (armor != 'L')
                code.Append(armor);
            code.Append('-').Append(role);
            // Note 3: a rover or a station that is not a support craft ends with its role.
            bool grounded = kind == 'D' || kind == 'E' || IsStation(kind);
            if (!grounded || role == '0')
            {
                // Note 2: a station does not fly.
                code.Append(IsStation(kind) ? '0' : Propulsion(ship));
                // Note 1: only a support craft says what tools it carries.
                if (role == '0')
                    code.Append(Tools(ship));
            }
            return code.Append(']').ToString();
        }

        static bool IsStation(char kind)
        {
            return kind == 'J' || kind == 'K' || kind == 'L' || kind == 'M';
        }

        static bool Flies(ShipTraits ship)
        {
            return ship.LiftsInAtmosphere || ship.LiftsOnMoon;
        }

        public static char Kind(ShipTraits ship)
        {
            if (ship.Station)
                return ship.SafeZone ? (ship.JumpDrive ? 'M' : 'L') : (ship.JumpDrive ? 'J' : 'K');
            if (ship.Unmanned)
                return ship.Large ? (ship.JumpDrive ? 'N' : 'O') : 'P';
            if (ship.Wheels)
            {
                if (Flies(ship))
                    return ship.Large ? (ship.JumpDrive ? 'F' : 'G') : 'H';
                return ship.Large ? 'D' : 'E';
            }
            bool surfaceThrust = (ship.AtmosphericThrust || ship.HydrogenThrust) && !ship.IonThrust && !ship.ExoticThrust;
            if (ship.WaterWorld && surfaceThrust && !ship.LiftsInAtmosphere)
                return ship.Large ? 'R' : 'S';
            bool propelled = ship.AtmosphericThrust || ship.IonThrust || ship.HydrogenThrust || ship.ExoticThrust ||
                             ship.GravityDrive;
            if (!propelled)
                return 'X';
            return ship.Large ? (ship.JumpDrive ? 'A' : 'B') : (ship.JumpDrive ? 'Q' : 'C');
        }

        // The scheme names no letter for every mix (reactor, engine and clean energy without batteries; engine and
        // clean energy; no power at all): those read X.
        public static char Power(ShipTraits ship)
        {
            bool re = ship.Reactor, he = ship.HydrogenEngine, el = ship.CleanEnergy, ba = ship.Battery;
            if (re && he && el && ba) return 'A';
            if (re && he && !el && ba) return 'B';
            if (re && he && !el && !ba) return 'C';
            if (re && !he && el && ba) return 'D';
            if (re && !he && el && !ba) return 'E';
            if (re && !he && !el && ba) return 'F';
            if (!re && he && el && ba) return 'G';
            if (!re && he && !el && ba) return 'H';
            if (!re && !he && el && ba) return 'I';
            if (re && !he && !el && !ba) return 'J';
            if (!re && he && !el && !ba) return 'K';
            if (!re && !he && el && !ba) return 'L';
            if (!re && !he && !el && ba) return 'M';
            return 'X';
        }

        public static char Armor(ShipTraits ship)
        {
            if (ship.ArmorBlocks <= 0 || ship.ArmorBlocks < ship.Blocks * ARMORED_SHARE)
                return 'X';
            int light = ship.ArmorBlocks - ship.HeavyArmorBlocks;
            if (light > ship.ArmorBlocks * ONE_KIND_SHARE)
                return 'L';
            return ship.HeavyArmorBlocks > ship.ArmorBlocks * ONE_KIND_SHARE ? 'P' : 'H';
        }

        // Combat is decided first: a warship carries a few welders or grinders for repairs, while a work ship has no
        // weapons, or too few to count. Only then do tools make a support craft; one with no role at all is one too
        // (as "other").
        public static char Role(ShipTraits ship)
        {
            bool cargo = ship.Cargo || ship.Trade, exploration = ship.Exploration;
            if (ship.Combat)
            {
                if (cargo && exploration) return '5';
                if (cargo) return '1';
                if (exploration) return '9';
                return '6';
            }
            if (ship.Welder || ship.Grinder || ship.Drill)
                return '0';
            if (ship.Trade && exploration) return '4';
            if (ship.Trade) return '3';
            if (cargo && exploration) return '2';
            if (cargo || ship.Production) return '7';
            if (exploration) return '8';
            return '0';
        }

        // Only the kinds of thrust that actually lift it count: one that cannot fly in any gravity reads 0.
        public static char Propulsion(ShipTraits ship)
        {
            bool thrusters = ship.AtmosphericThrust || ship.IonThrust || ship.HydrogenThrust;
            if (!thrusters && ship.ExoticThrust)
                return '9';
            if (!thrusters && ship.GravityDrive)
                return '8';
            bool atmospheric = ship.AtmosphericThrust && ship.LiftsInAtmosphere;
            bool ion = ship.IonThrust && ship.LiftsOnMoon;
            bool hydrogen = ship.HydrogenThrust && (ship.LiftsInAtmosphere || ship.LiftsOnMoon);
            int digit = (atmospheric ? 1 : 0) + (ion ? 2 : 0) + (hydrogen ? 4 : 0);
            return (char)('0' + digit);
        }

        public static char Tools(ShipTraits ship)
        {
            bool weld = ship.Welder, grind = ship.Grinder, mine = ship.Drill;
            if (weld && grind && mine) return '9';
            if (grind && mine) return '8';
            if (weld && mine) return '7';
            if (weld && grind) return '6';
            if (mine) return '3';
            if (grind) return '2';
            if (weld) return '1';
            bool powered = ship.Reactor || ship.HydrogenEngine || ship.CleanEnergy || ship.Battery;
            return powered ? '4' : '5';
        }
    }
}
