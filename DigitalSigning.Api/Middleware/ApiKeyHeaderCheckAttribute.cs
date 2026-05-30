using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace DigitalSigning.Api.Middleware
{
    /// <summary>
    /// API Key validation filter — kiểm tra header ApiKey trước khi cho phép request.
    /// Danh sách key được cấu hình trong appsettings.json (AppSettings:ApiKeys).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ApiKeyHeaderCheckAttribute : TypeFilterAttribute
    {
        public ApiKeyHeaderCheckAttribute() : base(typeof(ApiKeyHeaderCheckFilter))
        {
        }

        private class ApiKeyHeaderCheckFilter : IActionFilter
        {
            private readonly string[] _allowedKeys;

            public ApiKeyHeaderCheckFilter(IConfiguration configuration)
            {
                var keys = configuration.GetValue<string>("AppSettings:ApiKeys")
                    ?? configuration.GetValue<string>("ApiKeys")
                    ?? "3EC79C17-63ED-4166-BD58-04397B94312C;030097002468";
                _allowedKeys = keys.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            public void OnActionExecuting(ActionExecutingContext context)
            {
                var requestKey = context.HttpContext.Request.Headers["ApiKey"].FirstOrDefault();

                if (string.IsNullOrEmpty(requestKey))
                {
                    context.Result = new JsonResult(new
                    {
                        status = 403,
                        message = "Lỗi token không được để trống",
                        error_code = "Authorized"
                    })
                    {
                        StatusCode = 403
                    };
                    return;
                }

                if (!_allowedKeys.Contains(requestKey))
                {
                    context.Result = new JsonResult(new
                    {
                        status = 403,
                        message = "Lỗi nhập sai mã token",
                        error_code = "Authorized"
                    })
                    {
                        StatusCode = 403
                    };
                    return;
                }
            }

            public void OnActionExecuted(ActionExecutedContext context)
            {
            }
        }
    }
}
