namespace CourtBook.Application.Common;

/// <summary>
/// Discriminated union that wraps either a success value or an Error.
/// Use this as the return type of service methods instead of throwing exceptions
/// for expected business failures (not found, conflict, validation, etc.).
/// </summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T Value { get; }
    public Error Error { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
        Error = Error.None;
    }

    private Result(Error error)
    {
        IsSuccess = false;
        Error = error;
        Value = default!;
    }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(Error error) => new(error);

    // Implicit conversions for ergonomics
    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Fail(error);

    /// <summary>
    /// Executes <paramref name="onSuccess"/> if the result is Ok,
    /// otherwise executes <paramref name="onFailure"/>.
    /// </summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
        => IsSuccess ? onSuccess(Value) : onFailure(Error);
}

/// <summary>
/// Non-generic result for operations that return no value on success.
/// </summary>
public sealed class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    private Result(bool success, Error error = default!)
    {
        IsSuccess = success;
        Error = error ?? Error.None;
    }

    public static Result Ok() => new(true);
    public static Result Fail(Error error) => new(false, error);

    public static implicit operator Result(Error error) => Fail(error);

    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure)
        => IsSuccess ? onSuccess() : onFailure(Error);
}
