namespace NormalBake.Core;

/// <summary>How serious a UV-contract finding is.</summary>
public enum UvValidationSeverity
{
    /// <summary>A condition that degrades quality but does not block baking.</summary>
    Warning,

    /// <summary>A contract violation that makes a correct unique bake impossible.</summary>
    Error,
}

/// <summary>A single UV-contract finding with a stable code and an actionable message.</summary>
public sealed class UvValidationIssue
{
    /// <summary>Creates a finding.</summary>
    public UvValidationIssue(UvValidationSeverity severity, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);
        Severity = severity;
        Code = code;
        Message = message;
    }

    /// <summary>Severity of the finding.</summary>
    public UvValidationSeverity Severity { get; }

    /// <summary>A stable identifier (e.g. <c>UV.Overlap</c>) for programmatic handling.</summary>
    public string Code { get; }

    /// <summary>A human-readable, actionable description.</summary>
    public string Message { get; }

    /// <summary>Renders the finding as <c>[Severity] Code: message</c>.</summary>
    public override string ToString() => $"[{Severity}] {Code}: {Message}";
}

/// <summary>The outcome of validating a low-poly's UV layout against the bake contract.</summary>
public sealed class UvValidationResult
{
    /// <summary>Creates a result from a set of findings.</summary>
    public UvValidationResult(IReadOnlyList<UvValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues;
    }

    /// <summary>All findings, in the order they were produced.</summary>
    public IReadOnlyList<UvValidationIssue> Issues { get; }

    /// <summary>True when no finding is an <see cref="UvValidationSeverity.Error"/>.</summary>
    public bool IsValid
    {
        get
        {
            foreach (UvValidationIssue issue in Issues)
            {
                if (issue.Severity == UvValidationSeverity.Error)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// Knobs for UV-contract validation. The texel-based checks (overlap, gutter) are evaluated on a raster
/// grid; <see cref="PaddingTexels"/> defaults to the resolution-scaled recommendation (≈ res/128, i.e.
/// 8 px at 1K, 16 px at 2K), and the required inter-island gutter is twice that.
/// </summary>
public sealed class UvValidationOptions
{
    /// <summary>Output texture resolution the UVs target; the overlap/gutter raster scales with it.</summary>
    public int Resolution { get; init; } = 1024;

    /// <summary>
    /// Edge-padding width in texels at <see cref="Resolution"/>; islands must be ≥ 2× this apart. When
    /// null, defaults to <c>max(1, Resolution / 128)</c> (the polycount padding table).
    /// </summary>
    public int? PaddingTexels { get; init; }

    /// <summary>When true, triangles lying entirely outside the 0–1 tile are an error rather than a warning.</summary>
    public bool RequireInsideTile { get; init; }
}

/// <summary>Thrown by <see cref="UvContract.ValidateOrThrow"/> when the UV layout violates the contract.</summary>
public sealed class UvContractException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public UvContractException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public UvContractException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public UvContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
