using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Yachasoft.Sri.FacturacionElectronica.Services
{
    public interface IFrappeCredentialsService
    {
        Task<FrappeCredentialsResult> ObtenerCredencialesAsync(string emisor);
    }

    public class FrappeCredentialsResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Emisor { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public bool TieneApiKey { get; set; }
        public bool TieneApiSecret { get; set; }
    }

    public class FrappeCredentialsService : IFrappeCredentialsService
    {
        private readonly HttpClient _httpClient;
        private readonly string _frappeUrl;

        // ✅ CONSTRUCTOR SIN CREDENCIALES REQUERIDAS (endpoint público)
        public FrappeCredentialsService(HttpClient httpClient, IConfiguration configuration)
        {
            _frappeUrl = configuration["Frappe:Url"] 
                ?? throw new ArgumentNullException("Frappe:Url", "Frappe:Url no configurado en appsettings.json");

            // ✅ Configura el cliente inyectado
            httpClient.BaseAddress = new Uri(_frappeUrl);
            httpClient.Timeout = TimeSpan.FromMinutes(2);
            
            // ⚠️ NO configurar Authorization - El endpoint es público
            
            _httpClient = httpClient;

            Console.WriteLine($"✅ FrappeCredentialsService inicializado");
            Console.WriteLine($"🌐 URL Base: {_frappeUrl}");
            Console.WriteLine($"📌 Endpoint público (sin autenticación requerida)");
        }

        public async Task<FrappeCredentialsResult> ObtenerCredencialesAsync(string emisor)
        {
            try
            {
                Console.WriteLine($"\n🔐 ═══════════════════════════════════════════════");
                Console.WriteLine($"🔐 OBTENIENDO CREDENCIALES PARA: {emisor}");
                Console.WriteLine($"🔐 ═══════════════════════════════════════════════");

                // 🔥 Endpoint público que retorna credenciales del emisor
                var endpoint = "/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.obtener_apikeys";
                Console.WriteLine($"📡 Endpoint: {endpoint}");

                // ✅ Construir el payload JSON
                var payload = new JObject
                {
                    ["emisor"] = emisor
                };

                Console.WriteLine($"📤 Payload: {payload}");

                // 🚀 Hacer la petición POST
                var content = new StringContent(
                    payload.ToString(),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync(endpoint, content);
                var jsonResponse = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"📥 Status Code: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Error HTTP {response.StatusCode}");
                    Console.WriteLine($"📋 Response Body: {jsonResponse}");
                    return new FrappeCredentialsResult
                    {
                        Success = false,
                        Error = $"Error HTTP {response.StatusCode}: {jsonResponse}",
                        Emisor = emisor
                    };
                }

                // ✅ Parsear respuesta
                var json = JObject.Parse(jsonResponse);
                var message = json["message"];

                Console.WriteLine($"📋 Respuesta del servidor:");
                Console.WriteLine(message?.ToString(Newtonsoft.Json.Formatting.Indented));

                // ✅ Validar campo "success"
                var success = message?["success"]?.Value<bool>() ?? false;

                if (!success)
                {
                    var error = message?["error"]?.ToString() ?? "El servidor retornó success=false";
                    Console.WriteLine($"❌ {error}");
                    
                    return new FrappeCredentialsResult
                    {
                        Success = false,
                        Error = error,
                        Emisor = emisor
                    };
                }

                // 🔑 Extraer credenciales
                var apiKey = message?["api_key"]?.ToString();
                var apiSecret = message?["api_secret"]?.ToString();
                var tieneApiKey = message?["tiene_api_key"]?.Value<bool>() ?? false;
                var tieneApiSecret = message?["tiene_api_secret"]?.Value<bool>() ?? false;

                Console.WriteLine($"\n📊 RESULTADO:");
                Console.WriteLine($"   ✓ Emisor: {message?["emisor"]}");
                Console.WriteLine($"   ✓ Tiene API Key: {tieneApiKey}");
                Console.WriteLine($"   ✓ Tiene API Secret: {tieneApiSecret}");

                // ⚠️ Validar que las credenciales existan
                if (!tieneApiKey || !tieneApiSecret || 
                    string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
                {
                    Console.WriteLine($"\n⚠️ ADVERTENCIA: Credenciales incompletas");
                    Console.WriteLine($"   - API Key válida: {!string.IsNullOrWhiteSpace(apiKey) && tieneApiKey}");
                    Console.WriteLine($"   - API Secret válida: {!string.IsNullOrWhiteSpace(apiSecret) && tieneApiSecret}");
                    
                    return new FrappeCredentialsResult
                    {
                        Success = false,
                        Error = "El emisor no tiene credenciales configuradas en Frappe",
                        Emisor = emisor,
                        TieneApiKey = tieneApiKey,
                        TieneApiSecret = tieneApiSecret
                    };
                }

                // ✅ Todo correcto
                Console.WriteLine($"\n✅ CREDENCIALES OBTENIDAS EXITOSAMENTE");
                Console.WriteLine($"🔑 API Key: {apiKey.Substring(0, Math.Min(10, apiKey.Length))}...");
                Console.WriteLine($"🔐 API Secret: {apiSecret.Substring(0, Math.Min(10, apiSecret.Length))}...");
                Console.WriteLine($"🔐 ═══════════════════════════════════════════════\n");

                return new FrappeCredentialsResult
                {
                    Success = true,
                    Emisor = message?["emisor"]?.ToString() ?? emisor,
                    ApiKey = apiKey,
                    ApiSecret = apiSecret,
                    TieneApiKey = tieneApiKey,
                    TieneApiSecret = tieneApiSecret
                };
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"\n❌ ERROR DE CONEXIÓN HTTP");
                Console.WriteLine($"📋 Mensaje: {httpEx.Message}");
                Console.WriteLine($"📋 Inner Exception: {httpEx.InnerException?.Message}");
                
                return new FrappeCredentialsResult
                {
                    Success = false,
                    Error = $"Error de conexión: {httpEx.Message}",
                    Emisor = emisor
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ ERROR INESPERADO");
                Console.WriteLine($"📋 Tipo: {ex.GetType().Name}");
                Console.WriteLine($"📋 Mensaje: {ex.Message}");
                Console.WriteLine($"📋 StackTrace: {ex.StackTrace}");
                
                return new FrappeCredentialsResult
                {
                    Success = false,
                    Error = $"Error: {ex.Message}",
                    Emisor = emisor
                };
            }
        }
    }
}