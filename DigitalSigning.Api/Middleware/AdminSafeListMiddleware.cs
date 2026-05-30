using System.Net;

namespace DigitalSigning.Api.Middleware
{
    /// <summary>
    /// Middleware kiểm tra IP an toàn — chỉ cho phép các IP trong danh sách.
    /// Mapping từ AdminSafeListMiddleware cũ.
    /// </summary>
    public class AdminSafeListMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AdminSafeListMiddleware> _logger;
        private readonly HashSet<IPAddress> _safeIps;

        public AdminSafeListMiddleware(
            RequestDelegate next,
            ILogger<AdminSafeListMiddleware> logger,
            string safelist)
        {
            _next = next;
            _logger = logger;
            _safeIps = safelist
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(IPAddress.Parse)
                .ToHashSet();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Method != HttpMethod.Get.Method)
            {
                var remoteIp = context.Connection.RemoteIpAddress;
                _logger.LogDebug("Request from Remote IP address: {RemoteIp}", remoteIp);

                if (remoteIp == null || !_safeIps.Contains(remoteIp))
                {
                    _logger.LogWarning(
                        "Forbidden Request from Remote IP address: {RemoteIp}", remoteIp);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            await _next.Invoke(context);
        }
    }

    public static class AdminSafeListExtensions
    {
        public static IApplicationBuilder UseAdminSafeList(
            this IApplicationBuilder builder, string safelist)
        {
            return builder.UseMiddleware<AdminSafeListMiddleware>(safelist);
        }
    }
}
