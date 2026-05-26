using System;
using System.Collections.Generic;

namespace MetroDiagram.Core.Geometry
{
    public readonly struct PathGeometryPoint
    {
        public PathGeometryPoint(double x, double z)
        {
            X = x;
            Z = z;
        }

        public double X { get; }

        public double Z { get; }
    }

    public static class PathGeometrySampler
    {
        public static IReadOnlyList<PathGeometryPoint> SampleCubicBezier(
            PathGeometryPoint p0,
            PathGeometryPoint p1,
            PathGeometryPoint p2,
            PathGeometryPoint p3,
            int intervals = 4)
        {
            intervals = Math.Max(1, intervals);
            List<PathGeometryPoint> points = new List<PathGeometryPoint>(intervals + 1);
            for (int i = 0; i <= intervals; i++)
            {
                points.Add(EvaluateCubicBezier(p0, p1, p2, p3, (double)i / intervals));
            }

            return points;
        }

        public static PathGeometryPoint EvaluateCubicBezier(
            PathGeometryPoint p0,
            PathGeometryPoint p1,
            PathGeometryPoint p2,
            PathGeometryPoint p3,
            double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            double oneMinusT = 1 - t;
            double a = oneMinusT * oneMinusT * oneMinusT;
            double b = 3 * oneMinusT * oneMinusT * t;
            double c = 3 * oneMinusT * t * t;
            double d = t * t * t;
            return new PathGeometryPoint(
                a * p0.X + b * p1.X + c * p2.X + d * p3.X,
                a * p0.Z + b * p1.Z + c * p2.Z + d * p3.Z);
        }
    }
}
