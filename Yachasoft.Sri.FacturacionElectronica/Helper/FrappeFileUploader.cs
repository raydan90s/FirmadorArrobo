using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Yachasoft.Sri.FacturacionElectronica.Services
{
    // ✅ INTERFAZ ACTUALIZADA - Sobrecargas con credenciales dinámicas
    public interface IFrappeFileUploader
    {
        // Métodos originales (usan credenciales del config)
        Task<FrappeUploadResult> UploadFileAsync(string filePath, string fileName,
            string folder = "Home/Attachments", string doctype = null, string docname = null);

        Task<FrappeUploadResult> UploadFileStreamAsync(Stream fileStream, string fileName,
            string folder = "Home/Attachments", string doctype = null, string docname = null);

        // 🔥 NUEVOS MÉTODOS - Con credenciales dinámicas
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

        // ✅ CONSTRUCTOR CORREGIDO - Recibe HttpClient inyectado
        public FrappeFileUploader(HttpClient httpClient, IConfiguration configuration)
        {
            var frappeUrl = configuration["Frappe:Url"] 
                ?? throw new ArgumentNullException("Frappe:Url no configurado");
            _defaultApiKey = configuration["Frappe:ApiKey"] 
                ?? throw new ArgumentNullException("Frappe:ApiKey no configurado");
            _defaultApiSecret = configuration["Frappe:ApiSecret"] 
                ?? throw new ArgumentNullException("Frappe:ApiSecret no configurado");
            _defaultFolder = configuration["Frappe:FolderRetencion"] ?? "Home/Attachments";

            // ✅ Configura el cliente inyectado (no crees uno nuevo)
            httpClient.BaseAddress = new Uri(frappeUrl);
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            
            _httpClient = httpClient;
            
            // ⚠️ NO configurar Authorization aquí - se hará por request
        }

        // ✅ MÉTODO ORIGINAL - Usa credenciales del config
        public async Task<FrappeUploadResult> UploadFileAsync(
            string filePath,
            string fileName,
            string folder = "Home/Attachments",
            string doctype = null,
            string docname = null)
        {
            return await UploadFileAsync(filePath, fileName, _defaultApiKey, _defaultApiSecret, folder, doctype, docname);
        }

        // 🔥 MÉTODO CON CREDENCIALES DINÁMICAS
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
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"❌ [ERROR] El archivo no existe: {filePath}");
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
                Console.WriteLine($"❌ [ERROR] Exception subiendo archivo a Frappe: {ex.Message}");
                return new FrappeUploadResult
                {
                    Success = false,
                    Error = ex.Message,
                    FileName = fileName
                };
            }
        }

        // ✅ MÉTODO ORIGINAL - Usa credenciales del config
        public async Task<FrappeUploadResult> UploadFileStreamAsync(
            Stream fileStream,
            string fileName,
            string folder = "Home/Attachments",
            string doctype = null,
            string docname = null)
        {
            return await UploadFileStreamAsync(fileStream, fileName, _defaultApiKey, _defaultApiSecret, folder, doctype, docname);
        }

        // 🔥 MÉTODO CON CREDENCIALES DINÁMICAS - Implementación real
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
                Console.WriteLine($"📤 Subiendo archivo: {fileName}");
                Console.WriteLine($"📁 Carpeta destino: {folder ?? "Home/Attachments"}");
                Console.WriteLine($"🔑 Usando API Key: {apiKey?.Substring(0, Math.Min(8, apiKey?.Length ?? 0))}...");

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
                    Console.WriteLine($"📎 Adjuntando a: {doctype}/{docname}");
                }

                // 🔥 CREAR REQUEST CON CREDENCIALES ESPECÍFICAS
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/method/upload_file");
                request.Content = content;
                request.Headers.Authorization = new AuthenticationHeaderValue("token", $"{apiKey}:{apiSecret}");

                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Frappe respondió con código {response.StatusCode}");
                    Console.WriteLine($"📋 Response: {responseBody}");
                    
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
                Console.WriteLine($"✅ Archivo subido exitosamente");
                Console.WriteLine($"🔗 URL: {fileUrl}");

                return new FrappeUploadResult
                {
                    Success = true,
                    FileUrl = fileUrl,
                    FileName = messageData?["file_name"]?.ToString() ?? fileName,
                    RawResponse = jsonResponse
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [ERROR] Exception en UploadFileStreamAsync: {ex.Message}");
                Console.WriteLine($"📋 [TRACE] {ex.StackTrace}");
                
                return new FrappeUploadResult
                {
                    Success = false,
                    Error = ex.Message,
                    FileName = fileName
                };
            }
        }

        // Helper para parsear JSON de forma segura
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