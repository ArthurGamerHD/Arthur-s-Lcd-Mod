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
        public Vector3 Position;
        public Color IconColor;
        public string IconTexture;
        public Color ElevationColor;
        public bool TargetLock;
        public float TargetLockPercent;
        public RadarMarker Marker;
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
