using System.Collections.Generic;
using VRageMath;

namespace LcdMod.Client.Apps.Radar
{
    internal enum RadarMarker
    {
        Square,
        Circle,
        Triangle,
        OutOfRange
    }

    /// <summary>A contact in radar space: X right, Y backward, Z up, normalized to the range (1 = range edge).</summary>
    internal struct RadarTarget
    {
        public long EntityId;
        public Vector3 Position;
        public Color IconColor;
        public string IconTexture;
        public Color ElevationColor;
        public bool TargetLock;
        public float TargetLockPercent;
        // What the game's lock key would lock now (nothing is locked): bracketed, in the radar's line color.
        public bool LockCandidate;
        public RadarMarker Marker;
    }

    // A missile in flight, in the same normalized radar space: a triangle pointing where it flies, colored by who
    // launched it, with its recent trail behind and the line it flies on ahead.
    internal sealed class RadarMissile
    {
        public Vector3 Position;
        public Vector3 Heading;
        public Vector3 Ahead;
        public readonly List<Vector3> Trail = new List<Vector3>();
        public Color Color;
    }

    internal interface IRadarCamera
    {
        bool Orbit(Vector2 deltaRadians);
        bool Pan(Vector2 deltaMeters);
        bool Scroll(int delta);
        void ResetView();
    }

    internal sealed class RadarScene
    {
        public const float DEFAULT_PITCH = 0.6108652f;

        public readonly List<RadarTarget> Targets = new List<RadarTarget>();
        public readonly List<RadarMissile> Missiles = new List<RadarMissile>();

        public IRadarCamera Camera;
        public float WorldRange = 1f;
        public float ZoomScale = 1f;
        public float Yaw;
        public float Pitch = DEFAULT_PITCH;
        public Vector2 PanMeters;
        public Color LineColor = Color.White;
        public float[] RingDistances = new float[0];
    }
}
