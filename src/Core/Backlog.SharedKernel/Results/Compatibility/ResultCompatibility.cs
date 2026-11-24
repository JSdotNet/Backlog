namespace Backlog.SharedKernel.Results.Compatibility;

/// <summary>
/// TEMPORARY. The members the class-based <see cref="Result"/> had, rebuilt
/// over the union, so code outside <c>src/Core</c> and <c>src/Modules</c>
/// keeps compiling while it waits for its own migration to exhaustive
/// <c>switch</c> matching (plan app-defaults-rollout, step 14, deletes this
/// folder). Only those projects import the namespace — see
/// <c>Directory.Build.props</c> — so a module cannot reach back for it.
/// </summary>
public static class ResultCompatibility
{
    extension(Result result)
    {
        public bool IsSuccess => result switch
        {
            Ok => true,
            Error => false,
        };

        public bool IsFailure => !result.IsSuccess;

        /// <summary><see cref="Error.None"/> when the result succeeded.</summary>
        public Error Error => result switch
        {
            Ok => Error.None,
            Error error => error,
        };
    }

    extension<TValue>(Result<TValue> result)
    {
        public bool IsSuccess => result switch
        {
            Ok<TValue> => true,
            Error => false,
        };

        public bool IsFailure => !result.IsSuccess;

        /// <summary><see cref="Error.None"/> when the result succeeded.</summary>
        public Error Error => result switch
        {
            Ok<TValue> => Error.None,
            Error error => error,
        };

        /// <summary>The produced value. Reading it on a failed result is a bug, so it throws.</summary>
        public TValue Unwrap() => result switch
        {
            Ok<TValue> ok => ok.Value,
            Error => throw new InvalidOperationException("A failed result has no value; check IsSuccess first."),
        };

        /// <summary>Hands out the value only when there is one, without throwing.</summary>
        public bool TryGetValue(out TValue value)
        {
            if (result is Ok<TValue> ok)
            {
                value = ok.Value;
                return true;
            }

            value = default!;
            return false;
        }

        /// <summary>The result as the non-generic <see cref="Result"/> the class hierarchy gave for free.</summary>
        public Result AsResult() => result switch
        {
            Ok<TValue> => Result.Success(),
            Error error => error,
        };
    }
}
