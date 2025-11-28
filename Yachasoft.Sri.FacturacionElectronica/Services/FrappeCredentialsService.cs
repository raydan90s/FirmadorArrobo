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

         
        }

        public async Task<FrappeCredentialsResult> ObtenerCredencialesAsync(string emisor)
        {
            try
            {
              

                // 🔥 Endpoint público que retorna credenciales del emisor
                var endpoint = "/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.obtener_apikeys";
               

                // ✅ Construir el payload JSON
                var payload = new JObject
                {
                    ["emisor"] = emisor
                };

                

                // 🚀 Hacer la petición POST
                var content = new StringContent(
                    payload.ToString(),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync(endpoint, content);
                var jsonResponse = await response.Content.ReadAsStringAsync();

               

                if (!response.IsSuccessStatusCode)
                {
                    
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

             

                // ✅ Validar campo "success"
                var success = message?["success"]?.Value<bool>() ?? false;

                if (!success)
                {
                    var error = message?["error"]?.ToString() ?? "El servidor retornó success=false";
                
                    
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

            

                // ⚠️ Validar que las credenciales existan
                if (!tieneApiKey || !tieneApiSecret || 
                    string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
                {
                   
                    
                    return new FrappeCredentialsResult
                    {
                        Success = false,
                        Error = "El emisor no tiene credenciales configuradas en Frappe",
                        Emisor = emisor,
                        TieneApiKey = tieneApiKey,
                        TieneApiSecret = tieneApiSecret
                    };
                }

                

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
                
                
                return new FrappeCredentialsResult
                {
                    Success = false,
                    Error = $"Error de conexión: {httpEx.Message}",
                    Emisor = emisor
                };
            }
            catch (Exception ex)
            {
              
                
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