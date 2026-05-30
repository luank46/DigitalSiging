using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DigitalSigning.Api.Middleware
{
    /// <summary>
    /// API Key validation filter — kiểm tra header ApiKey trước khi cho phép request.
    /// Mapping từ ApiKeyHeaderCheckAttribute cũ.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ApiKeyHeaderCheckAttribute : TypeFilterAttribute
    {
        /// <summary>
        /// Cho phép tùy chỉnh danh sách API key khi dùng attribute.
        /// </summary>
        public ApiKeyHeaderCheckAttribute(params string[] allowedKeys)
            : base(typeof(ApiKeyHeaderCheckFilter))
        {
            Arguments = new object[] { allowedKeys.Length > 0 ? allowedKeys : DefaultAllowedKeys };
        }

        private static readonly string[] DefaultAllowedKeys =
        {
            "3EC79C17-63ED-4166-BD58-04397B94312C",
            "030097002468"
        };

        private class ApiKeyHeaderCheckFilter : IActionFilter
        {
            private readonly string[] _allowedKeys;

            public ApiKeyHeaderCheckFilter(string[] allowedKeys)
            {
                _allowedKeys = allowedKeys;
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
