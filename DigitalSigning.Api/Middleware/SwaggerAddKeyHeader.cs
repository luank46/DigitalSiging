using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DigitalSigning.Api.Middleware
{
    /// <summary>
    /// Operation filter thêm header ApiKey vào Swagger UI.
    /// Mapping từ SwaggerAddKeyHeader cũ.
    /// </summary>
    public class SwaggerAddKeyHeader : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            operation.Parameters ??= new List<OpenApiParameter>();
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "ApiKey",
                In = ParameterLocation.Header,
                Required = true,
                AllowEmptyValue = false,
            });
        }
    }
}
