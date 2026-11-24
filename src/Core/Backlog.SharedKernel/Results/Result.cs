namespace Backlog.SharedKernel.Results;

/// <summary>
/// The outcome of an operation that can fail for a reason the caller is
/// expected to handle: <see cref="Ok"/> or an <see cref="Results.Error"/>.
/// Exceptions stay for genuinely exceptional situations; everything a feature
/// slice can reasonably anticipate comes back as a result.
/// </summary>
/// <remarks>
/// A C# union (app-defaults ADR 0005): read it with an exhaustive
/// <c>switch</c> over its cases, so the compiler proves every outcome is
/// handled — CS8509 is an error.
/// <code>
/// return result switch
/// {
///     Ok => ...,
///     Error error => ...,
/// };
/// </code>
/// </remarks>
public union Result(Ok, Error)
{
    public static Result Success() => Ok.Instance;

    public static Result Failure(Error error) => Failed(error);

    public static Result<TValue> Success<TValue>(TValue value) => new Ok<TValue>(value);

    public static Result<TValue> Failure<TValue>(Error error) => Failed(error);

    private static Error Failed(Error error) => error == Error.None
        ? throw new ArgumentException("A failed result must carry an error.", nameof(error))
        : error;
}

/// <summary>
/// A <see cref="Result"/> that carries a value when it succeeds:
/// <see cref="Ok{TValue}"/> or an <see cref="Results.Error"/>.
/// </summary>
/// <remarks>
/// <code>
/// return result switch
/// {
///     Ok&lt;Entry&gt; ok => ok.Value,
///     Error error => ...,
/// };
/// </code>
/// </remarks>
public union Result<TValue>(Ok<TValue>, Error)
{
    public static implicit operator Result<TValue>(TValue value) => new Ok<TValue>(value);
}

/// <summary>The success case of a <see cref="Result"/>: the operation did what it was asked.</summary>
public sealed record Ok
{
    private Ok()
    {
    }

    /// <summary>The one success; it carries nothing.</summary>
    public static Ok Instance { get; } = new();
}

/// <summary>The success case of a <see cref="Result{TValue}"/>, holding the value produced.</summary>
public sealed record Ok<TValue>(TValue Value);
