using System;
using System.Collections.Generic;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;

namespace Cartography
{
    /// <summary>
    /// The mod-specific services the shared cartography files depend on.
    /// Each mod that builds them provides its own copy of this class.
    /// </summary>
    internal static class CartographyHost
    {
        public const long CommunicationId = Constants.WORKSHOP_ID;

        public static Dictionary<long, MyPlanet> PlanetsById => PlanetHelper.PlanetsById;

        public static void LogError(Exception error, Type source)
        {
            ErrorHandlerHelper.LogError(error, source);
        }
    }
}
