using System;

namespace SentriPet
{
    /// <summary>Exponential smoothing toward a target.</summary>
    class Anim
    {
        public double Value, Target;
        public Anim(double v) { Value = Target = v; }
        public void Step(double dt, double speed)
        {
            Value += (Target - Value) * (1 - Math.Exp(-speed * dt));
            // settle exactly, so unchanged values stop invalidating the visuals every frame
            if (Math.Abs(Target - Value) < 0.02) Value = Target;
        }
    }

    /// <summary>Damped spring (for needles that overshoot a little).</summary>
    class Spring
    {
        public double X, V, Target;
        public Spring(double x) { X = Target = x; }
        public void Step(double dt, double k, double damping)
        {
            int n = (int)Math.Ceiling(dt / 0.008);
            double h = dt / Math.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                double a = k * (Target - X) - damping * V;
                V += a * h;
                X += V * h;
            }
        }
    }
}
