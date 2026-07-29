using System;

namespace LcdMod.Client.Modules.Cartography
{
    public enum CartographyProjection
    {
        CubemapFaces
    }

    public enum CartographyLayer
    {
        SurfaceFarColor
    }

    public sealed class CartographyRequest
    {
        public long PlanetEntityId;
        public string PlanetGeneratorSubtype;
        public double PlanetRadiusMeters;
        public CartographyProjection Projection = CartographyProjection.CubemapFaces;
        public CartographyLayer Layer = CartographyLayer.SurfaceFarColor;

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
