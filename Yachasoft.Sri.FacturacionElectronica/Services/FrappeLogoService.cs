using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Yachasoft.Sri.FacturacionElectronica.Services
{
    public class ObtenerLogoResult
    {
        public bool Success { get; set; }
        public string Emisor { get; set; }
        public string NombreArchivo { get; set; }
        public string LogoBase64 { get; set; }
        public string Error { get; set; }
    }

    public class FrappeLogoService
    {
        private readonly HttpClient _httpClient;
        private readonly FrappeSettings _settings;

        public FrappeLogoService(HttpClient httpClient, IOptions<FrappeSettings> options)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settings = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 🔥 MÉTODO ACTUALIZADO: OBTIENE LOGO CON CREDENCIALES DEL EMISOR
        /// </summary>
        public async Task<ObtenerLogoResult> ObtenerLogoAsync(
            string emisor,
            string apiKey = null,
            string apiSecret = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(emisor))
                {
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = "Emisor vacío"
                    };
                }

                Console.WriteLine($"\n🖼️ ═══════════════════════════════════════════════");
                Console.WriteLine($"🖼️ OBTENIENDO LOGO PARA: {emisor}");
                Console.WriteLine($"🖼️ ═══════════════════════════════════════════════");

                var apiUrl = $"{_settings.Url.TrimEnd('/')}/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.obtener_logo";

                var requestBody = new { emisor };
                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                req.Content = jsonContent;

                // 🔥 USAR CREDENCIALES DEL EMISOR SI VIENEN
                if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
                {
                    req.Headers.Add("Authorization", $"token {apiKey}:{apiSecret}");
                    Console.WriteLine($"🔑 Usando credenciales del EMISOR");
                    Console.WriteLine($"   API Key: {apiKey.Substring(0, Math.Min(8, apiKey.Length))}...");
                }
                else
                {
                    req.Headers.Add("Authorization", $"token {_settings.ApiKey}:{_settings.ApiSecret}");
                    Console.WriteLine($"⚠️ Usando credenciales por DEFAULT (appsettings.json)");
                    Console.WriteLine($"⚠️ Puede que no tenga permisos para este emisor");
                }

                Console.WriteLine($"📡 URL: {apiUrl}");

                var res = await _httpClient.SendAsync(req);
                var responseBody = await res.Content.ReadAsStringAsync();

                Console.WriteLine($"📥 Status Code: {res.StatusCode}");

                if (!res.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ ERROR HTTP {res.StatusCode}");
                    Console.WriteLine($"📋 Response: {responseBody}");

                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = $"HTTP {res.StatusCode}: {responseBody}"
                    };
                }

                Console.WriteLine($"📋 Respuesta recibida ({responseBody.Length} caracteres)");

                using var doc = JsonDocument.Parse(responseBody);

                if (!doc.RootElement.TryGetProperty("message", out var message))
                {
                    Console.WriteLine($"❌ Respuesta inválida: no contiene 'message'");
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = "Respuesta inválida (no 'message')"
                    };
                }

                // VALIDAR SI FRAPPE DEVUELVE success=false
                if (message.TryGetProperty("success", out var successProp) && !successProp.GetBoolean())
                {
                    var errorMsg = message.TryGetProperty("error", out var errProp)
                        ? errProp.GetString()
                        : "Error desconocido desde Frappe";

                    Console.WriteLine($"❌ FRAPPE: success=false => {errorMsg}");

                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = errorMsg
                    };
                }

                // EXTRAER LOGO
                if (!message.TryGetProperty("logo", out var logo))
                {
                    Console.WriteLine($"⚠️ No se encontró 'logo' en la respuesta");
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = "No se recibió ningún logo del emisor"
                    };
                }

                if (!logo.TryGetProperty("contenido_base64", out var base64Prop))
                {
                    Console.WriteLine($"⚠️ No se encontró 'contenido_base64'");
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = "Logo recibido sin base64"
                    };
                }

                string base64 = base64Prop.GetString();
                if (string.IsNullOrEmpty(base64))
                {
                    Console.WriteLine($"⚠️ Base64 del logo vacío");
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = "Logo vacío"
                    };
                }

                string nombreArchivo = null;
                if (logo.TryGetProperty("nombre", out var nombreProp))
                {
                    nombreArchivo = nombreProp.GetString();
                }

                Console.WriteLine($"\n✅ LOGO OBTENIDO:");
                Console.WriteLine($"   ✓ Emisor: {emisor}");
                Console.WriteLine($"   ✓ Archivo: {nombreArchivo ?? "N/A"}");
                Console.WriteLine($"   ✓ Tamaño Base64: {base64.Length}");
                Console.WriteLine($"🖼️ ═══════════════════════════════════════════════\n");

                return new ObtenerLogoResult
                {
                    Success = true,
                    Emisor = emisor,
                    LogoBase64 = base64,
                    NombreArchivo = nombreArchivo
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ EXCEPCIÓN AL OBTENER LOGO");
                Console.WriteLine($"📋 Tipo: {ex.GetType().Name}");
                Console.WriteLine($"📋 Mensaje: {ex.Message}");
                Console.WriteLine($"📋 StackTrace: {ex.StackTrace}");

                return new ObtenerLogoResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }
    }
}
