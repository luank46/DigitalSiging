using DigitalSigning.Api.Middleware;
using DigitalSigning.Core.Helpers;
using DigitalSigning.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigitalSigning.Api.Controllers
{
    /// <summary>
    /// Cache management admin endpoints.
    /// Mapping từ CacheManageController cũ.
    /// </summary>
    [ApiController]
    [ApiKeyHeaderCheck]
    [Route("[controller]")]
    public class CacheManageController : ControllerBase
    {
        private readonly IAsyncCacheService _asyncCacheService;

        public CacheManageController(IAsyncCacheService asyncCacheService)
        {
            _asyncCacheService = asyncCacheService;
        }

        /// <summary>
        /// Xóa cache Redis theo key hoặc clear tất cả.
        /// </summary>
        [HttpPost("Clear")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<ResponseHelper.LegacyResponseData>> Clear(string? cacheKey)
        {
            if (!string.IsNullOrEmpty(cacheKey))
            {
                await _asyncCacheService.RemoveAsync(cacheKey);
            }
            else
            {
                await _asyncCacheService.ClearAsync();
            }

            return Ok(ResponseHelper.Success(Guid.NewGuid(), "Thành công"));
        }

        /// <summary>
        /// Xóa cache theo prefix.
        /// </summary>
        [HttpPost("ClearByPrefix")]
        public async Task<ActionResult<ResponseHelper.LegacyResponseData>> ClearByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return BadRequest(ResponseHelper.ValidationError("Thiếu prefix"));

            await _asyncCacheService.RemoveByPrefixAsync(prefix);
            return Ok(ResponseHelper.Success(null, $"Đã xóa cache với prefix '{prefix}'"));
        }
    }
}
