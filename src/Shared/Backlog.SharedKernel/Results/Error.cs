namespace Backlog.SharedKernel.Results;

/// <summary>
/// Why an operation did not succeed. An error is data, not an exception: it
/// travels back through <see cref="Result"/> so a caller has to look at it.
/// </summary>
/// <param name="Code">Stable, machine-readable identifier, e.g. <c>entry.not_found</c>.</param>
/// <param name="Message">Human-readable explanation, safe to show to the person using the app.</param>
/// <param name="Type">What kind of failure this is, so a host can map it to UI or HTTP.</param>
public readonly record struct Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    /// <summary>The absence of an error. Never returned by a failed result.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Input the caller supplied is not acceptable.</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>The thing being addressed does not exist.</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>The request is understood but conflicts with current state.</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>Something went wrong that the caller cannot correct.</summary>
    public static Error Unexpected(string code, string message) => new(code, message, ErrorType.Unexpected);

    public override string ToString() => string.IsNullOrEmpty(Code) ? Message : $"{Code}: {Message}";
}

/// <summary>Coarse failure classification, kept small on purpose.</summary>
public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unexpected
}
