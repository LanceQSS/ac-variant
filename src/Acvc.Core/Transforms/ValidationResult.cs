namespace Acvc.Core.Transforms;

public enum ValidationSeverity
{
    Warning,
    Failure,
}

/// <summary>
/// One validation finding. <see cref="Value"/> is the offending number and
/// <see cref="Limit"/> the threshold it was judged against, so reports never say
/// just "mass out of range" without the numbers.
/// </summary>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Rule,
    double Value,
    double Limit,
    string Message);

/// <summary>Outcome of the post-transform validation pass.</summary>
public sealed class ValidationResult
{
    public ValidationResult(IReadOnlyList<ValidationIssue> issues) => Issues = issues;

    public IReadOnlyList<ValidationIssue> Issues { get; }
    public IEnumerable<ValidationIssue> Failures => Issues.Where(i => i.Severity == ValidationSeverity.Failure);
    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == ValidationSeverity.Warning);
    public bool HasFailures => Failures.Any();
}
