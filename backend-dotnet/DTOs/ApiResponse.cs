namespace Opstrax.Api.DTOs;

public sealed record ApiResponse<T>(bool Success, T? Data, string Message, IReadOnlyList<string> Errors)
{
    public static ApiResponse<T> Ok(T data, string message = "") => new(true, data, message, []);
    public static ApiResponse<T> Fail(string message, params string[] errors) => new(false, default, message, errors);
}
