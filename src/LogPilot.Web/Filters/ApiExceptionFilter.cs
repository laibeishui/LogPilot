using LogPilot.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LogPilot.Web.Filters;

/// <summary>
/// 将 AppException 映射为统一 JSON 错误响应。
/// </summary>
public sealed class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not AppException appException)
        {
            return;
        }

        var requestId = context.HttpContext.TraceIdentifier;
        context.Result = new ObjectResult(new ApiError
        {
            Code = appException.Code,
            Message = appException.Message,
            RequestId = requestId
        })
        {
            StatusCode = appException.StatusCode
        };
        context.ExceptionHandled = true;
    }
}
