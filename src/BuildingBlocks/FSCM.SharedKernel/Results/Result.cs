namespace FSCM.SharedKernel.Results;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new("", "");
    public static Error NotFound(string what, string id) => new("not_found", $"{what} {id} không tồn tại.");
    public static Error Conflict(string message) => new("conflict", message);
    public static Error Validation(string message) => new("validation", message);
}

public class Result
{
    protected Result(bool ok, Error error) { IsSuccess = ok; Error = error; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }
    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error e) => new(false, e);
    public static Result<T> Success<T>(T value) => new(value, true, Error.None);
    public static Result<T> Failure<T>(Error e) => new(default, false, e);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;
    internal Result(T? value, bool ok, Error error) : base(ok, error) => _value = value;
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Result is a failure.");
}
