using MetroDiagram.Core.Models;

namespace MetroDiagram.Core.Loading;

public sealed class MetroLoadResult
{
    public MetroLoadResult(
        MetroExportDocument? document,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        Document = document;
        Warnings = warnings;
        Errors = errors;
    }

    public MetroExportDocument? Document { get; }

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Document is not null && Errors.Count == 0;

    public static MetroLoadResult Failure(string error)
    {
        return new MetroLoadResult(null, [], [error]);
    }
}

