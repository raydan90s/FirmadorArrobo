using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Yachasoft.Sri.FacturacionElectronica.Services
{
    // ✅ INTERFAZ CON AMBAS VERSIONES
    public interface IFrappeFileUploader
    {
        // Métodos originales (usan credenciales del config) - Para compatibilidad
        Task<FrappeUploadResult> UploadFileAsync(string filePath, string fileName,
            string folder = "Home/Attachments", string doctype = null, string docname = null);

        Task<FrappeUploadResult> UploadFileStreamAsync(Stream fileStream, string fileName,
            string folder = "Home/Attachments", string doctype = null, string docname = null);

        // Métodos con credenciales dinámicas - Para tu controlador específico
        Task<FrappeUploadResult> UploadFileAsync(string filePath, string fileName,
            string apiKey, string apiSecret,
            string folder = "Home/Attachments", string doctype = null, string docname = null);

        Task<FrappeUploadResult> UploadFileStreamAsync(Stream fileStream, string fileName,
            string apiKey, string apiSecret,
            string folder = "Home/Attachments", string doctype = null, string docname = null);
    }

    public class FrappeUploadResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string FileUrl { get; set; }
        public string FileName { get; set; }
        public JObject RawResponse { get; set; }
    }

    public class FrappeFileUploader : IFrappeFileUploader
    {
        private readonly HttpClient _httpClient;
        private readonly string _defaultApiKey;
        private readonly string _defaultApiSecret;
        private readonly string _defaultFolder;

        // ✅ CONSTRUCTOR: Lee credenciales por defecto pero permite que sean opcionales
        public FrappeFileUploader(HttpClient httpClient, IConfiguration configuration)
        {
            var frappeUrl = configuration["Frappe:Url"] 
                ?? throw new ArgumentNullException("Frappe:Url", "Frappe:Url no configurado");

            // ✅ Lee credenciales por defecto (si existen)
            _defaultApiKey = configuration["Frappe:ApiKey"] ?? string.Empty;
            _defaultApiSecret = configuration["Frappe:ApiSecret"] ?? string.Empty;
            _defaultFolder = configuration["Frappe:FolderRetencion"] ?? "Home/Attachments";

            httpClient.BaseAddress = new Uri(frappeUrl);
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            _httpClient = httpClient;

          
        }

        // ✅ MÉTODO ORIGINAL - Usa credenciales del config (para compatibilidad con código existente)
        public async Task<FrappeUploadResult> UploadFileAsync(
            string filePath,
            string fileName,
            string folder = "Home/Attachments",
            string doctype = null,
            string docname = null)
        {
            if (string.IsNullOrEmpty(_defaultApiKey) || string.IsNullOrEmpty(_defaultApiSecret))
            {
                
                return new FrappeUploadResult
                {
                    Success = false,
                    Error = "Credenciales por defecto no configuradas. Use la sobrecarga con apiKey y apiSecret.",
                    FileName = fileName
                };
            }

            return await UploadFileAsync(filePath, fileName, _defaultApiKey, _defaultApiSecret, folder, doctype, docname);
        }

        // ✅ MÉTODO ORIGINAL - Usa credenciales del config
        public async Task<FrappeUploadResult> UploadFileStreamAsync(
            Stream fileStream,
            string fileName,
            string folder = "Home/Attachments",
            string doctype = null,
            string docname = null)
        {
            if (string.IsNullOrEmpty(_defaultApiKey) || string.IsNullOrEmpty(_defaultApiSecret))
            {
                
                return new FrappeUploadResult
                {
                    Success = false,
                    Error = "Credenciales por defecto no configuradas. Use la sobrecarga con apiKey y apiSecret.",
                    FileName = fileName
                };
            }

            return await UploadFileStreamAsync(fileStream, fileName, _defaultApiKey, _defaultApiSecret, folder, doctype, docname);
        }

        // 🔥 NUEVO MÉTODO - Con credenciales dinámicas (para tu controlador específico)
        public async Task<FrappeUploadResult> UploadFileAsync(
            string filePath,
            string fileName,
            string apiKey,
            string apiSecret,
            string folder = "Home/Attachments",
            string doctype = null,
            string docname = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
                {
                    
                    return new FrappeUploadResult
                    {
                        Success = false,
                        Error = "Se requieren credenciales válidas (apiKey y apiSecret)",
                        FileName = fileName
                    };
                }

                if (!File.Exists(filePath))
                {
                    
                    return new FrappeUploadResult
                    {
                        Success = false,
                        Error = "file_not_found",
                        FileName = fileName
                    };
                }

                using var fileStream = File.OpenRead(filePath);
                return await UploadFileStreamAsync(fileStream, fileName, apiKey, apiSecret, folder, doctype, docname);
            }
            catch (Exception ex)
            {
                
                return new FrappeUploadResult
                {
                    Success = false,
                    Error = ex.Message,
                    FileName = fileName
                };
            }
        }

        // 🔥 IMPLEMENTACIÓN REAL - Con credenciales dinámicas
        public async Task<FrappeUploadResult> UploadFileStreamAsync(
            Stream fileStream,
            string fileName,
            string apiKey,
            string apiSecret,
            string folder = "Home/Attachments",
            string doctype = null,
            string docname = null)
        {
            try
            {
              

                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
                {
                    
                    return new FrappeUploadResult
                    {
                        Success = false,
                        Error = "Credenciales requeridas",
                        FileName = fileName
                    };
                }

                folder ??= "Home/Attachments";

                using var content = new MultipartFormDataContent();

                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "file", fileName);

                content.Add(new StringContent("0"), "is_private");
                content.Add(new StringContent(folder), "folder");

                if (!string.IsNullOrEmpty(doctype) && !string.IsNullOrEmpty(docname))
                {
                    content.Add(new StringContent(doctype), "attached_to_doctype");
                    content.Add(new StringContent(docname), "attached_to_name");
                    
                }

                // 🔥 REQUEST CON CREDENCIALES ESPECÍFICAS
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/method/upload_file");
                request.Content = content;
                request.Headers.Authorization = new AuthenticationHeaderValue("token", $"{apiKey}:{apiSecret}");

               

                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    
                    
                    return new FrappeUploadResult
                    {
                        Success = false,
                        Error = $"HTTP {response.StatusCode}: {responseBody}",
                        FileName = fileName,
                        RawResponse = TryParseJson(responseBody)
                    };
                }

                var jsonResponse = JObject.Parse(responseBody);
                var messageData = jsonResponse["message"];
                var fileUrl = messageData?["file_url"]?.ToString();
                
             

                return new FrappeUploadResult
                {
                    Success = true,
                    FileUrl = fileUrl,
                    FileName = messageData?["file_name"]?.ToString() ?? fileName,
                    RawResponse = jsonResponse
                };
            }
            catch (HttpRequestException httpEx)
            {
            
                return new FrappeUploadResult
                {
                    Success = false,
                    Error = $"Error HTTP: {httpEx.Message}",
                    FileName = fileName
                };
            }
            catch (Exception ex)
            {
              
                return new FrappeUploadResult
                {
                    Success = false,
                    Error = ex.Message,
                    FileName = fileName
                };
            }
        }

        private JObject TryParseJson(string json)
        {
            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return new JObject { ["raw"] = json };
            }
        }
    }
}