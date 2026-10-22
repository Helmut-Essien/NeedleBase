using System.Net;
using NeedleBase.Application.Contracts.Errors;

namespace NeedleBase.Web.Services;

/// <summary>Outcome of one Api call: a value, a ProblemDetails failure, or "couldn't reach the Api".</summary>
public sealed class ApiResult<T>
{
    public const string OfflineMessage = "Can't reach NeedleBase right now. Check your connection and try again.";

    private ApiResult(T? value, ApiProblem? problem, bool unreachable)
    {
        Value = value;
        Problem = problem;
        Unreachable = unreachable;
    }

    public T? Value { get; }

    public ApiProblem? Problem { get; }

    /// <summary>Network failure or timeout (no response). Pages show the offline state.</summary>
    public bool Unreachable { get; }

    public bool IsSuccess => Problem is null && !Unreachable;

    public HttpStatusCode? Status => Problem is null ? null : (HttpStatusCode)Problem.Status;

    public string? Code => Problem?.Code;

    /// <summary>Plain-language message for a page banner (never a raw exception or status code).</summary>
    public string Message => Unreachable
        ? OfflineMessage
        : Problem?.Detail ?? Problem?.FirstValidationError ?? Problem?.Title ?? "Something went wrong. Try again.";

    public static ApiResult<T> Ok(T value) => new(value, null, false);

    public static ApiResult<T> Fail(ApiProblem problem) => new(default, problem, false);

    public static ApiResult<T> Offline() => new(default, null, true);
}
