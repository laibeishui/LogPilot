namespace LogPilot.Application.Common;

/// <summary>
/// 可映射到稳定 HTTP 状态码的业务异常。
/// </summary>
public sealed class AppException : Exception
{
    public AppException(string code, string message, int statusCode = 400)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}
