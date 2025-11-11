using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Yachasoft.Sri.FacturacionElectronica.Services
{
    public class DownloadCertificateResult
    {
        public bool success { get; set; }
        public string filePath { get; set; }
        public string password { get; set; }
        public string error { get; set; }
    }

    public class FrappeSettings
    {
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
    }

    public class FrappeCertificateService
    {
        private readonly HttpClient _httpClient;
        private readonly FrappeSettings _settings;

        public FrappeCertificateService(HttpClient httpClient, IOptions<FrappeSettings> options)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settings = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Descarga el certificado .p12 desde Frappe y retorna la ruta temporal + contraseña
        /// </summary>
        public async Task<DownloadCertificateResult> DownloadCertificateAsync(string emisor)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(emisor))
                    return new DownloadCertificateResult { success = false, error = "Emisor vacío" };

                Console.WriteLine($"🔍 Buscando certificado para: {emisor}");

                // 1️⃣ Construir la URL de la API (POST, no GET)
                var apiUrl = $"{_settings.Url.TrimEnd('/')}/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.obtener_certificado";

                // 2️⃣ Crear el body JSON como espera tu API de Frappe
                var requestBody = new { emisor = emisor };
                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                // 3️⃣ Crear request POST con autenticación
                using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                req.Content = jsonContent;
                req.Headers.Add("Authorization", $"token {_settings.ApiKey}:{_settings.ApiSecret}");

                // 4️⃣ Enviar request
                var res = await _httpClient.SendAsync(req);
                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Error HTTP {res.StatusCode}: {body}");
                    return new DownloadCertificateResult 
                    { 
                        success = false, 
                        error = $"Frappe responded {res.StatusCode}: {body}" 
                    };
                }

                // 5️⃣ Parsear respuesta de Frappe
                var json = await res.Content.ReadAsStringAsync();
                Console.WriteLine($"📥 Respuesta Frappe: {json.Substring(0, Math.Min(300, json.Length))}...");

                using var doc = JsonDocument.Parse(json);
                
                // La respuesta viene en message.archivo.contenido_base64
                if (!doc.RootElement.TryGetProperty("message", out var message))
                    return new DownloadCertificateResult { success = false, error = "Respuesta inválida (no 'message')" };

                if (!message.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
                {
                    var errorMsg = message.TryGetProperty("error", out var errProp) 
                        ? errProp.GetString() 
                        : "Error desconocido desde Frappe";
                    return new DownloadCertificateResult { success = false, error = errorMsg };
                }

                // 6️⃣ Extraer el archivo en base64
                if (!message.TryGetProperty("archivo", out var archivo))
                    return new DownloadCertificateResult { success = false, error = "No se encontró 'archivo' en la respuesta" };

                if (!archivo.TryGetProperty("contenido_base64", out var base64Prop))
                    return new DownloadCertificateResult { success = false, error = "No se encontró 'contenido_base64'" };

                var base64Content = base64Prop.GetString();
                if (string.IsNullOrEmpty(base64Content))
                    return new DownloadCertificateResult { success = false, error = "El contenido base64 está vacío" };

                // 7️⃣ Extraer la contraseña
                string password = null;
                if (message.TryGetProperty("contrasena", out var passProp))
                    password = passProp.GetString();

                Console.WriteLine($"🔑 Contraseña encontrada: {!string.IsNullOrEmpty(password)}");

                // 8️⃣ Decodificar base64 y guardar en archivo temporal
                var bytes = Convert.FromBase64String(base64Content);
                if (bytes == null || bytes.Length == 0)
                    return new DownloadCertificateResult { success = false, error = "El archivo decodificado está vacío" };

                // Guardar con nombre descriptivo
                var fileName = archivo.TryGetProperty("nombre", out var nombreProp) 
                    ? nombreProp.GetString() 
                    : $"{emisor.Replace(" ", "")}.p12";

                var tempPath = Path.Combine(Path.GetTempPath(), fileName);
                await File.WriteAllBytesAsync(tempPath, bytes);

                Console.WriteLine($"✅ Certificado descargado: {tempPath} ({bytes.Length} bytes)");

                return new DownloadCertificateResult
                {
                    success = true,
                    filePath = tempPath,
                    password = password,
                    error = null
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Excepción: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return new DownloadCertificateResult 
                { 
                    success = false, 
                    error = $"Excepción: {ex.Message}" 
                };
            }
        }
    }
}