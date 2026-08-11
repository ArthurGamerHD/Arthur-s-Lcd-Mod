using System;

namespace LcdMod.Client.Modules.Cartography
{
    public enum CartographyProjection
    {
        CubemapFaces
    }

    public enum CartographyLayer
    {
        Satellite = 0,

        // Compatibility alias for callers using the original layer name.
        SurfaceFarColor = Satellite,

        Terrain = 1,
        Materials = 2,
        Biomes = 3
    }

    public sealed class CartographyRequest
    {
        public long PlanetEntityId;
        public string PlanetGeneratorSubtype;
        public double PlanetRadiusMeters;
        public CartographyProjection Projection = CartographyProjection.CubemapFaces;
        public CartographyLayer Layer = CartographyLayer.Satellite;

        /// <summary>
        /// Zero keeps the source face resolution. A positive value renders each
        /// square cubemap face directly at this maximum side length.
        /// </summary>
        public int MaximumFaceSide;

        /// <summary>
        /// Builds an immutable, mipmapped cubemap of native VRageMath.Color texels.
        /// This is intended for live LCD apps that render full-color sprite masks.
        /// </summary>
        public bool ReturnColorCubemap;

        /// <summary>
        /// Captures verbose material-color fallback diagnostics. This is intended
        /// for the debug cartography command and is disabled for normal requests.
        /// </summary>
        public bool IncludeDiagnostics;
    }

    public sealed class CartographyResult
    {
        public bool Success;
        public bool Cancelled;
        public string Error;
        public long PlanetEntityId;
        public string PlanetGeneratorSubtype;
        public int FaceWidth;
        public int FaceHeight;
        public PlanetColorCubemap ColorCubemap;
        public PlanetColorCubemap WaterOverlayCubemap;

        /// <summary>
        /// Optional diagnostic text produced when IncludeDiagnostics was requested.
        /// </summary>
        public string DiagnosticReport;
    }

    public sealed class CartographyColorCubemapCachedEvent
    {
        public long PlanetEntityId;
        public string PlanetGeneratorSubtype;
        public CartographyProjection Projection;
        public CartographyLayer Layer;
        public int MaximumFaceSide;
        public PlanetColorCubemap ColorCubemap;
        public PlanetColorCubemap WaterOverlayCubemap;
    }

    public sealed class CartographyPlanetInvalidatedEvent
    {
        public long PlanetEntityId;
        public string PlanetGeneratorSubtype;
    }

    public sealed class CartographyTicket
    {
        readonly Action _cancel;

        internal CartographyTicket(long id, Action cancel)
        {
            Id = id;
            _cancel = cancel;
        }

        public long Id { get; private set; }

        public void Cancel()
        {
            if (_cancel != null)
                _cancel();
        }
    }

    internal sealed class CartographyCancellation
    {
        volatile bool _cancelled;

        public bool IsCancelled
        {
            get { return _cancelled; }
        }

        public void Cancel()
        {
            _cancelled = true;
        }

        public void ThrowIfCancelled()
        {
            if (_cancelled)
                throw new CartographyCancelledException();
        }
    }

    internal sealed class CartographyCancelledException : Exception
    {
    }
}
