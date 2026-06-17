using System.Numerics;

namespace NormalBake.Core;

/// <summary>A ray: an origin, a (not necessarily normalized) direction, and a maximum distance.</summary>
public readonly record struct Ray(Vector3 Origin, Vector3 Direction, float TMax)
{
    /// <summary>Creates a ray with an unbounded maximum distance.</summary>
    public Ray(Vector3 origin, Vector3 direction)
        : this(origin, direction, float.PositiveInfinity)
    {
    }
}

/// <summary>
/// A ray-triangle intersection: distance <see cref="T"/> along the ray, the hit triangle index, and the
/// barycentric weights of the second/third vertices (<see cref="U"/>/<see cref="V"/>); the first
/// vertex's weight is <see cref="W"/> = 1 − U − V. Interpolate an attribute as
/// <c>W·a0 + U·a1 + V·a2</c>.
/// </summary>
public readonly record struct Hit(float T, int Triangle, float U, float V)
{
    /// <summary>Barycentric weight of the first triangle vertex (1 − U − V).</summary>
    public float W => 1f - U - V;
}

/// <summary>An axis-aligned bounding box. An <see cref="Empty"/> box has inverted bounds (area 0).</summary>
public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    /// <summary>An inverted, empty box that grows correctly under <see cref="Include"/>/<see cref="Union"/>.</summary>
    public static Aabb Empty => new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    /// <summary>The box extent (Max − Min).</summary>
    public Vector3 Extent => Max - Min;

    /// <summary>The box centre.</summary>
    public Vector3 Centroid => (Min + Max) * 0.5f;

    /// <summary>Half the total surface area's components — the SAH metric (0 if inverted/degenerate).</summary>
    public float SurfaceArea
    {
        get
        {
            Vector3 e = Max - Min;
            if (e.X < 0f || e.Y < 0f || e.Z < 0f)
            {
                return 0f;
            }

            return 2f * ((e.X * e.Y) + (e.Y * e.Z) + (e.Z * e.X));
        }
    }

    /// <summary>Returns this box grown to include a point.</summary>
    public Aabb Include(Vector3 p) => new(Vector3.Min(Min, p), Vector3.Max(Max, p));

    /// <summary>Returns the union of two boxes.</summary>
    public Aabb Union(Aabb other) => new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));
}

/// <summary>A ray tracer over a fixed high-poly: closest-hit (normal pass) and any-hit (occlusion).</summary>
public interface IRayTracer
{
    /// <summary>Finds the closest hit within <c>ray.TMax</c>. Returns false if the ray misses.</summary>
    bool ClosestHit(in Ray ray, out Hit hit);

    /// <summary>Returns true if any triangle is hit within <c>ray.TMax</c> (occlusion test).</summary>
    bool AnyHit(in Ray ray);
}
