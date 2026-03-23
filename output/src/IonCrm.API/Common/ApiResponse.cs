namespace IonCrm.API.Common;

/// <summary>
/// Standard API response wrapper used by all endpoints.
/// Every response is wrapped in this type for consistency.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
public class ApiResponse<T>
{
    /// <summary>Gets or sets a value indicating whether the request succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets the response payload (null on failure).</summary>
    public T? Data { get; set; }

    /// <summary>Gets or sets a human-readable message.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets the list of error messages (empty on success).</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>Gets or sets the HTTP status code mirrored in the body.</summary>
    public int StatusCode { get; set; }

    /// <summary>Creates a successful 200 response.</summary>
    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true, Data = data, StatusCode = 200, Message = message
    };

    /// <summary>Creates a successful 201 response.</summary>
    public static ApiResponse<T> Created(T data, string? message = null) => new()
    {
        Success = true, Data = data, StatusCode = 201, Message = message
    };

    /// <summary>Creates a failure response.</summary>
    public static ApiResponse<T> Fail(string error, int statusCode = 400) => new()
    {
        Success = false, Errors = new List<string> { error }, StatusCode = statusCode
    };

    /// <summary>Creates a failure response with multiple errors.</summary>
    public static ApiResponse<T> Fail(IEnumerable<string> errors, int statusCode = 400) => new()
    {
        Success = false, Errors = errors.ToList(), StatusCode = statusCode
    };
}
