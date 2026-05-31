using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Services.FileUpload
{
    // ── Response models ────────────────────────────────────────────────

    public class UploadFileResponseModel
    {
        public int status { get; set; }
        public string? message { get; set; }
        public string? messageDetail { get; set; }
        public int total { get; set; }
        public UploadData? data { get; set; }
    }

    public class UploadData
    {
        public UploadSuccess[]? success { get; set; }
        public UploadError[]? error { get; set; }
    }

    public class UploadSuccess
    {
        public string? message { get; set; }
        public string? urlPathFile { get; set; }
        public DateTime dateCreate { get; set; }
        public string? extension { get; set; }
        public int size { get; set; }
        public int type { get; set; }
        public bool IsFileSigned { get; set; }
    }

    public class UploadError
    {
        public string? message { get; set; }
        public string? urlPathFile { get; set; }
        public DateTime dateCreate { get; set; }
        public string? extension { get; set; }
        public int size { get; set; }
        public int type { get; set; }
        public bool IsFileSigned { get; set; }
    }

    // ── Upload V2 (multipart) models ────────────────────────────────────

    public class FileInputUploadModel
    {
        public List<FileUploadItem> lstFileUpload { get; set; } = new();
        public string? urlDirectory { get; set; }
        public bool isUploadCloud { get; set; }
        public bool isRename { get; set; }
        public int wResize { get; set; }
        public bool isResize { get; set; }
        public int maNamHoc { get; set; }
        public string? maSoGD { get; set; }
        public string? maPhongGD { get; set; }
        public string? maTruong { get; set; }
        public string? maPhanMem { get; set; }
    }

    public class FileUploadItem
    {
        public byte[]? fileBytes { get; set; }
        public string? fileName { get; set; }
    }

    public class UploadFileV2ResponseModel
    {
        public List<string> Errors { get; set; } = new();
        public List<DataUploaded>? Data { get; set; }
        public bool IsSuccess => Errors.Count <= 0;
    }

    public class DataUploaded
    {
        public int index { get; set; }
        public string? fileServerMessage { get; set; }
        public int fileServerCode { get; set; }
        public string? fileServerUrlPath { get; set; }
        public string? fileNameRequest { get; set; }
        public object? fileCloudUrlPath { get; set; }
        public int fileCloudCode { get; set; }
        public object? fileCloudMessage { get; set; }
        public DateTime dateCreate { get; set; }
        public string? extension { get; set; }
        public int size { get; set; }
        public string? type { get; set; }
    }

    // ── Service ─────────────────────────────────────────────────────────

    /// <summary>
    /// Upload file service — upload signed files lên external HTTP server.
    /// Mapping từ Core.ApiUploadFile cũ.
    /// </summary>
    public class ApiUploadFileService
    {
        private readonly HttpClient _httpClient;
        private const string DefaultApiKey = "3EC79C17-63ED-4166-BD58-04397B94312C";

        public ApiUploadFileService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _httpClient.DefaultRequestHeaders.Add("ApiKey", DefaultApiKey);
        }

        /// <summary>
        /// Upload danh sách file dạng base64 lên server.
        /// </summary>
        public async Task<UploadFileResponseModel?> UploadListFileBase64Async(
            string urlApi, ListFileBase64UploadModel input, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(input);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(urlApi, content, ct);
            var resultJson = await response.Content.ReadAsStringAsync(ct);

            return JsonSerializer.Deserialize<UploadFileResponseModel>(resultJson);
        }

        /// <summary>
        /// Upload files dạng multipart/form-data (V2).
        /// </summary>
        public async Task<UploadFileV2ResponseModel?> UploadFilesAsync(
            string urlApi, FileInputUploadModel model, CancellationToken ct = default)
        {
            using var formData = new MultipartFormDataContent();

            foreach (var file in model.lstFileUpload)
            {
                if (file.fileBytes != null)
                {
                    var fileContent = new ByteArrayContent(file.fileBytes);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    formData.Add(fileContent, "lstFile", file.fileName ?? "file.bin");
                }
            }

            formData.Add(new StringContent(model.urlDirectory ?? ""), "urlDirectory");
            formData.Add(new StringContent(model.isUploadCloud.ToString()), "isUploadCloud");
            formData.Add(new StringContent(model.isRename.ToString()), "isRename");
            formData.Add(new StringContent(model.wResize.ToString()), "wResize");
            formData.Add(new StringContent(model.isResize.ToString()), "isResize");
            formData.Add(new StringContent(model.maPhanMem ?? ""), "maPhanMem");
            formData.Add(new StringContent(model.maNamHoc.ToString()), "maNamHoc");
            formData.Add(new StringContent(model.maSoGD ?? ""), "maSoGD");
            formData.Add(new StringContent(model.maTruong ?? ""), "maTruong");
            if (!string.IsNullOrEmpty(model.maPhongGD))
                formData.Add(new StringContent(model.maPhongGD), "maPhongGD");

            var response = await _httpClient.PostAsync(urlApi, formData, ct);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<UploadFileV2ResponseModel>(content);
            }

            return new UploadFileV2ResponseModel
            {
                Errors = new List<string> { $"Lỗi upload file: {response.StatusCode}" }
            };
        }

        /// <summary>
        /// Xóa danh sách file trên server.
        /// </summary>
        public async Task<bool> DeleteListFileAsync(string uri, List<string> lstFile, CancellationToken ct = default)
        {
            var urlApi = uri.TrimEnd('/') + "/upload/deleteFile";
            var json = JsonSerializer.Serialize(lstFile);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(urlApi, content, ct);
            return response.IsSuccessStatusCode;
        }
    }

    /// <summary>
    /// Model cho upload base64 (V1).
    /// </summary>
    public class ListFileBase64UploadModel
    {
        public List<FileBase64UploadItem>? ListFile { get; set; }
        public string? UrlDirectory { get; set; }
        public bool IsRemaneFile { get; set; }
        public bool IsCompress { get; set; }
    }

    public class FileBase64UploadItem
    {
        public string? base64Data { get; set; }
        public string? fileName { get; set; }
    }
}
