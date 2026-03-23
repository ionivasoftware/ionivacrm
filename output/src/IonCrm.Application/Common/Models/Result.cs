namespace IonCrm.Application.Common.Models;

/// <summary>
/// Represents the outcome of an application operation that returns a value.
/// Use this for business-logic errors instead of throwing exceptions.
/// Exceptions are reserved for truly exceptional/unexpected conditions.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
public class Result<T>
{
    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
        Errors = Array.Empty<string>();
    }

    private Result(IEnumerable<string> errors)
    {
        IsSuccess = false;
        Errors = errors.ToList().AsReadOnly();
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the result value. Only valid when <see cref="IsSuccess"/> is true.</summary>
    public T? Value { get; }

    /// <summary>Gets the error messages. Only populated when <see cref="IsFailure"/> is true.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Gets the first error message, or null if the operation succeeded.</summary>
    public string? FirstError => Errors.Count > 0 ? Errors[0] : null;

    /// <summary>Creates a successful result carrying the given value.</summary>
    public static Result<T> Success(T value) => new(value);

    /// <summary>Creates a failed result with a single error message.</summary>
    public static Result<T> Failure(string error) => new(new[] { error });

    /// <summary>Creates a failed result with multiple error messages.</summary>
    public static Result<T> Failure(IEnumerable<string> errors) => new(errors);
}

/// <summary>
/// Represents the outcome of an application operation that returns no value.
/// </summary>
public class Result
{
    private Result() { IsSuccess = true; Errors = Array.Empty<string>(); }

    private Result(IEnumerable<string> errors)
    {
        IsSuccess = false;
        Errors = errors.ToList().AsReadOnly();
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the error messages. Only populated when <see cref="IsFailure"/> is true.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Gets the first error message, or null if the operation succeeded.</summary>
    public string? FirstError => Errors.Count > 0 ? Errors[0] : null;

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new();

    /// <summary>Creates a failed result with a single error message.</summary>
    public static Result Failure(string error) => new(new[] { error });

    /// <summary>Creates a failed result with multiple error messages.</summary>
    public static Result Failure(IEnumerable<string> errors) => new(errors);
}
