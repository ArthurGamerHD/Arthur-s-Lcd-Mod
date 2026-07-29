using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Common.Helpers;
using LcdMod.Common.Imaging;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Client.Modules.Cartography
{
    public sealed class CartographyModule
    {
        sealed class PendingJob
        {
            public long Id;
            public CartographyRequest Request;
            public PlanetDefinitionSnapshot Planet;
            public FarColorCatalogSnapshot FarColors;
            public CartographyCancellation Cancellation;
            public Action<CartographyResult> Completed;
            public CartographyResult Result;
            public Exception WorkerError;
        }

        struct ColorCubemapCacheKey : IEquatable<ColorCubemapCacheKey>
        {
            public long PlanetEntityId;
            public string PlanetGeneratorSubtype;
            public CartographyProjection Projection;
            public CartographyLayer Layer;
            public int MaximumFaceSide;

            public bool Equals(ColorCubemapCacheKey other)
            {
                return PlanetEntityId == other.PlanetEntityId &&
                       Projection == other.Projection &&
                       Layer == other.Layer &&
                       MaximumFaceSide == other.MaximumFaceSide &&
                       string.Equals(
                           PlanetGeneratorSubtype,
                           other.PlanetGeneratorSubtype,
                           StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return obj is ColorCubemapCacheKey &&
                       Equals((ColorCubemapCacheKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = PlanetEntityId.GetHashCode();
                    hash = hash * 397 ^ (int)Projection;
                    hash = hash * 397 ^ (int)Layer;
                    hash = hash * 397 ^ MaximumFaceSide;
                    hash = hash * 397 ^ (PlanetGeneratorSubtype == null
                        ? 0
                        : StringComparer.OrdinalIgnoreCase.GetHashCode(
                            PlanetGeneratorSubtype));
                    return hash;
                }
            }
        }

        readonly object _sync = new object();
        readonly Queue<PendingJob> _queue = new Queue<PendingJob>();
        readonly Dictionary<ColorCubemapCacheKey, PlanetColorCubemap> _colorCubemapCache =
            new Dictionary<ColorCubemapCacheKey, PlanetColorCubemap>();
        PendingJob _running;
        long _nextId;
        bool _unloaded;

        public bool TryGetCachedColorCubemap(
            CartographyRequest request,
            out PlanetColorCubemap cubemap)
        {
            cubemap = null;
            if (request == null || !request.ReturnColorCubemap)
                return false;

            ColorCubemapCacheKey key = CreateColorCubemapCacheKey(request);
            lock (_sync)
            {
                if (_colorCubemapCache.TryGetValue(key, out cubemap))
                    return true;

                // A higher lazily-generated level includes all lower display mips,
                // so it can service a less detailed request without rebuilding it.
                bool found = false;
                int bestRank = int.MaxValue;
                foreach (var pair in _colorCubemapCache)
                {
                    if (!SameColorCubemapIdentity(pair.Key, key) ||
                        !pair.Value.SatisfiesFaceSide(request.MaximumFaceSide))
                    {
                        continue;
                    }

                    int rank = pair.Value.DetailRank;
                    if (found && rank >= bestRank)
                        continue;

                    cubemap = pair.Value;
                    bestRank = rank;
                    found = true;
                }

                return found;
            }
        }

        public CartographyTicket RequestMap(
            CartographyRequest request,
            Action<CartographyResult> completed)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            // Copy caller-owned options and resolve engine-owned entities/definitions
            // on the game thread. The worker receives only module-owned data.
            CartographyRequest workRequest = CopyRequest(request);
            PlanetDefinitionSnapshot planet = CartographySnapshotBuilder.BuildPlanet(workRequest);
            FarColorCatalogSnapshot farColors = CartographySnapshotBuilder.BuildFarColors(planet);

            PendingJob job;
            lock (_sync)
            {
                if (_unloaded)
                    throw new InvalidOperationException("Cartography module is unloaded.");

                job = new PendingJob
                {
                    Id = ++_nextId,
                    Request = workRequest,
                    Planet = planet,
                    FarColors = farColors,
                    Cancellation = new CartographyCancellation(),
                    Completed = completed
                };

                _queue.Enqueue(job);
            }

            TryStartNext();
            return new CartographyTicket(job.Id, delegate { Cancel(job.Id); });
        }


        static ColorCubemapCacheKey CreateColorCubemapCacheKey(
            CartographyRequest request)
        {
            return new ColorCubemapCacheKey
            {
                PlanetEntityId = request.PlanetEntityId,
                PlanetGeneratorSubtype = request.PlanetEntityId != 0L
                    ? null
                    : request.PlanetGeneratorSubtype ?? string.Empty,
                Projection = request.Projection,
                Layer = request.Layer,
                MaximumFaceSide = request.MaximumFaceSide
            };
        }


        static bool SameColorCubemapIdentity(
            ColorCubemapCacheKey left,
            ColorCubemapCacheKey right)
        {
            return left.PlanetEntityId == right.PlanetEntityId &&
                   left.Projection == right.Projection &&
                   left.Layer == right.Layer &&
                   string.Equals(
                       left.PlanetGeneratorSubtype,
                       right.PlanetGeneratorSubtype,
                       StringComparison.OrdinalIgnoreCase);
        }

        void StoreColorCubemap(
            ColorCubemapCacheKey key,
            PlanetColorCubemap cubemap)
        {
            var remove = new List<ColorCubemapCacheKey>();

            foreach (var pair in _colorCubemapCache)
            {
                if (!SameColorCubemapIdentity(pair.Key, key))
                    continue;

                // Keep an already cached level when it contains this level and all
                // of its lower display mips. Otherwise, discard levels superseded
                // by the new higher-resolution cubemap.
                if (pair.Value.SatisfiesFaceSide(cubemap.RequestedMaximumFaceSide))
                    return;

                if (cubemap.SatisfiesFaceSide(pair.Value.RequestedMaximumFaceSide))
                    remove.Add(pair.Key);
            }

            for (int i = 0; i < remove.Count; i++)
                _colorCubemapCache.Remove(remove[i]);

            _colorCubemapCache[key] = cubemap;
        }

        static CartographyRequest CopyRequest(CartographyRequest source)
        {
            return new CartographyRequest
            {
                PlanetEntityId = source.PlanetEntityId,
                PlanetGeneratorSubtype = source.PlanetGeneratorSubtype,
                PlanetRadiusMeters = source.PlanetRadiusMeters,
                Projection = source.Projection,
                Layer = source.Layer,
                MaximumFaceSide = source.MaximumFaceSide,
                ReturnColorCubemap = source.ReturnColorCubemap
            };
        }

        public bool Cancel(long ticketId)
        {
            lock (_sync)
            {
                if (_running != null && _running.Id == ticketId)
                {
                    _running.Cancellation.Cancel();
                    return true;
                }

                foreach (PendingJob pending in _queue)
                {
                    if (pending.Id != ticketId)
                        continue;

                    pending.Cancellation.Cancel();
                    return true;
                }
            }

            return false;
        }

        public void Clear()
        {
            lock (_sync)
            {
                _unloaded = true;
                if (_running != null)
                    _running.Cancellation.Cancel();

                while (_queue.Count > 0)
                    _queue.Dequeue().Cancellation.Cancel();

                _colorCubemapCache.Clear();
            }
        }

        void TryStartNext()
        {
            PendingJob job = null;
            lock (_sync)
            {
                if (_unloaded || _running != null)
                    return;

                while (_queue.Count > 0)
                {
                    PendingJob candidate = _queue.Dequeue();
                    if (candidate.Cancellation.IsCancelled)
                        continue;

                    job = candidate;
                    _running = candidate;
                    break;
                }
            }

            if (job == null)
                return;

            MyAPIGateway.Parallel.Start(
                delegate { Execute(job); },
                delegate { Complete(job); });
        }

        static void Execute(PendingJob job)
        {
            try
            {
                job.Cancellation.ThrowIfCancelled();
                job.FarColors.ResolveTextureFallbacks(job.Cancellation);
                job.Cancellation.ThrowIfCancelled();
                PlanetMapSource source = PlanetMapSource.Load(job.Planet, job.Cancellation);
                job.Cancellation.ThrowIfCancelled();

                PaintedPlanetFaces painted = PlanetSurfacePainter.Render(
                    source,
                    job.Planet,
                    job.FarColors,
                    job.Request,
                    job.Cancellation);

                job.Cancellation.ThrowIfCancelled();
                PlanetColorCubemap colorCubemap = job.Request.ReturnColorCubemap
                    ? PlanetColorCubemapBuilder.Build(
                        painted,
                        job.Request.MaximumFaceSide,
                        true,
                        job.Cancellation)
                    : null;

                job.Cancellation.ThrowIfCancelled();
                job.Result = BuildResult(job, painted, colorCubemap);
            }
            catch (CartographyCancelledException)
            {
                job.Result = new CartographyResult
                {
                    Success = false,
                    Cancelled = true,
                    PlanetGeneratorSubtype = job.Planet.GeneratorSubtype
                };
            }
            catch (Exception error)
            {
                job.WorkerError = error;
                job.Result = new CartographyResult
                {
                    Success = false,
                    Error = error.Message,
                    PlanetGeneratorSubtype = job.Planet != null
                        ? job.Planet.GeneratorSubtype
                        : job.Request.PlanetGeneratorSubtype
                };
            }
        }

        void Complete(PendingJob job)
        {
            Action<CartographyResult> callback = null;
            bool allowCallback;
            lock (_sync)
            {
                if (ReferenceEquals(_running, job))
                    _running = null;

                if (!_unloaded &&
                    job.Result != null &&
                    job.Result.Success &&
                    job.Result.ColorCubemap != null &&
                    job.Request.ReturnColorCubemap)
                {
                    StoreColorCubemap(
                        CreateColorCubemapCacheKey(job.Request),
                        job.Result.ColorCubemap);
                }

                allowCallback = !_unloaded;
                if (allowCallback)
                    callback = job.Completed;
            }

            if (allowCallback && job.WorkerError != null)
                ErrorHandlerHelper.LogError(job.WorkerError, typeof(CartographyModule));

            if (allowCallback && callback != null)
            {
                try
                {
                    callback(job.Result);
                }
                catch (Exception error)
                {
                    ErrorHandlerHelper.LogError(error, typeof(CartographyModule));
                }
            }

            TryStartNext();
        }

        static CartographyResult BuildResult(
            PendingJob job,
            PaintedPlanetFaces painted,
            PlanetColorCubemap colorCubemap)
        {
            int faceWidth = colorCubemap != null
                ? colorCubemap.BaseResolution
                : 0;
            int faceHeight = faceWidth;
            if (faceWidth == 0 && painted.Faces.Count > 0)
            {
                RawRgbaBitmap first = painted.Faces.Values.First();
                faceWidth = first.Width;
                faceHeight = first.Height;
            }

            return new CartographyResult
            {
                Success = true,
                PlanetEntityId = job.Request.PlanetEntityId,
                PlanetGeneratorSubtype = job.Planet.GeneratorSubtype,
                FaceWidth = faceWidth,
                FaceHeight = faceHeight,
                ColorCubemap = colorCubemap
            };
        }
    }
}
