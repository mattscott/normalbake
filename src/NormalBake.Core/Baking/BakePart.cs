namespace NormalBake.Core;

/// <summary>
/// One named piece of a multi-part bake: a low-poly and the high-poly it should be projected against.
/// Parts share one output atlas but each is baked in isolation, so geometry from one part never bleeds
/// into another. Pair them from named meshes with <see cref="NormalBaker.MatchByName"/>.
/// </summary>
public sealed record BakePart(string Name, Mesh High, UvMesh Low);
