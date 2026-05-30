using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DigitalSigning.Core.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DigitalSigning.Api.Controllers
{
    /// <summary>
    /// GCC HSM Proxy Controller — chuyển tiếp request đến GCC HSM API.
    /// Mapping từ GccController cũ. Cho phép client gọi trực tiếp các endpoint GCC.
    /// </summary>
    [ApiController]
    public class GccProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ProviderSettings _settings;

        public GccProxyController(
            IHttpClientFactory httpClientFactory,
            IOptions<ProviderSettings> settings)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
        }

        private string GccBase => _settings.GccBaseUrl.TrimEnd('/');

        [HttpPost("auth/login")]
        public async Task<IActionResult> Login([FromBody] object data)
        {
            return await ProxyPostAsync($"{GccBase}/auth/login", data);
        }

        [HttpPost("/credentials/list")]
        public async Task<IActionResult> CredentialsList([FromBody] object data)
        {
            return await ProxyPostAsync($"{GccBase}/credentials/list", data);
        }

        [HttpPost("/credentials/info")]
        public async Task<IActionResult> CredentialsInfo([FromBody] object data)
        {
            return await ProxyPostAsync($"{GccBase}/credentials/info", data);
        }

        [HttpPost("/credentials/authorize")]
        public async Task<IActionResult> CredentialsAuthorize([FromBody] object data)
        {
            return await ProxyPostAsync($"{GccBase}/credentials/authorize", data);
        }

        [HttpPost("/signatures/signHash")]
        public async Task<IActionResult> SignHash([FromBody] object data)
        {
            return await ProxyPostAsync($"{GccBase}/signatures/signHash", data);
        }

        private async Task<IActionResult> ProxyPostAsync(string url, object data)
        {
            var json = JsonSerializer.Serialize(data);
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            var authHeader = HttpContext.Request.Headers.Authorization.ToString();
            if (AuthenticationHeaderValue.TryParse(authHeader, out var parsedHeader))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", parsedHeader.Parameter);
            }

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            return StatusCode((int)response.StatusCode, responseContent);
        }
    }
}
