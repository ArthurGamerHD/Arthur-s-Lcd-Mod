using System;
using VRageMath;

namespace LcdMod.Client.Apps.Maps
{
    /// <summary>Time-based angular momentum measured from the actual dragged camera basis.</summary>
    internal sealed class MapOrbitInertia
    {
        // Match the legacy orbit camera's 0.9 decay per 60 Hz frame.
        static readonly double Decay = -Math.Log(.9d) * 60d;
        const float STOP_SPEED = .00008f * 60f;
        const float MAXIMUM_SPEED = .35f * 60f;
        const double MAXIMUM_SAMPLE_AGE = .08d;
        Quaternion _previous;
        Vector3 _axis;
        float _speed;
        double _sampleTime;
        double _stepTime;
        bool _dragging;
        bool _spinning;

        public void Begin(double time, Vector3 view, Vector3 right, Vector3 up)
        {
            Stop();
            _dragging = true;
            _previous = Orientation(view, right, up);
            _sampleTime = time;
        }

        public void Record(double time, Vector3 view, Vector3 right, Vector3 up)
        {
            if (!_dragging)
                return;
            Quaternion next = Orientation(view, right, up);
            Quaternion delta = next * Quaternion.Conjugate(_previous);
            delta.Normalize();
            if (delta.W < 0f)
                delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
            _axis = new Vector3(delta.X, delta.Y, delta.Z);
            float sine = _axis.Normalize();
            double elapsed = Math.Max(1d / 60d, time - _sampleTime);
            _speed = sine <= 1e-7f ? 0f : Math.Min(MAXIMUM_SPEED,
                (float)(2d * Math.Atan2(sine, delta.W) / elapsed));
            _previous = next;
            _sampleTime = time;
        }

        public void Release(double time)
        {
            _spinning = _dragging && time >= _sampleTime &&
                time - _sampleTime <= MAXIMUM_SAMPLE_AGE && _speed > STOP_SPEED;
            _dragging = false;
            _stepTime = time;
        }

        public bool Advance(double time, ref Vector3 view, ref Vector3 right, ref Vector3 up)
        {
            if (!_spinning || time <= _stepTime)
                return false;
            double elapsed = time - _stepTime;
            double decay = Math.Exp(-Decay * elapsed);
            float angle = (float)(_speed * (1d - decay) / Decay);
            Quaternion rotation = Quaternion.CreateFromAxisAngle(_axis, angle);
            view = Vector3.Transform(view, rotation);
            right = Vector3.Transform(right, rotation);
            up = Vector3.Transform(up, rotation);
            _speed *= (float)decay;
            _stepTime = time;
            if (_speed <= STOP_SPEED)
                _spinning = false;
            return true;
        }

        public void Stop()
        {
            _dragging = false;
            _spinning = false;
            _speed = 0f;
        }

        static Quaternion Orientation(Vector3 view, Vector3 right, Vector3 up)
        {
            Matrix basis = Matrix.Identity;
            basis.Right = right;
            basis.Up = up;
            basis.Backward = view;
            return Quaternion.CreateFromRotationMatrix(basis);
        }
    }
}
