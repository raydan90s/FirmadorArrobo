using System;
using System.Net.Http;
using System.Net.Http.Headers;
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
    }

    public class FrappeCredentialsService : IFrappeCredentialsService
    {
        private readonly HttpClient _httpClient;

        // ✅ CONSTRUCTOR CORREGIDO: Recibe HttpClient inyectado
        public FrappeCredentialsService(HttpClient httpClient, IConfiguration configuration)
        {
            var frappeUrl = configuration["Frappe:Url"] 
                ?? throw new ArgumentNullException("Frappe:Url no configurado");
            var apiKey = configuration["Frappe:ApiKey"] 
                ?? throw new ArgumentNullException("Frappe:ApiKey no configurado");
            var apiSecret = configuration["Frappe:ApiSecret"] 
                ?? throw new ArgumentNullException("Frappe:ApiSecret no configurado");

            // ✅ Configura el cliente inyectado (no crees uno nuevo)
            httpClient.BaseAddress = new Uri(frappeUrl);
            httpClient.Timeout = TimeSpan.FromMinutes(2);
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("token", $"{apiKey}:{apiSecret}");

            _httpClient = httpClient;
        }

        public async Task<FrappeCredentialsResult> ObtenerCredencialesAsync(string emisor)
        {
            try
            {
                Console.WriteLine($"🔐 Obteniendo credenciales para emisor: {emisor}");

                var response = await _httpClient.PostAsync(
                    "/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.obtener_certificado",
                    new StringContent(
                        $"{{\"emisor\": \"{emisor}\"}}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    )
                );

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Error HTTP {response.StatusCode}: {errorBody}");
                    return new FrappeCredentialsResult
                    {
                        Success = false,
                        Error = $"Error HTTP {response.StatusCode}: {errorBody}"
                    };
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(jsonResponse);

                if (json["message"]?["success"]?.Value<bool>() == true)
                {
                    var message = json["message"];
                    var apiKey = message["api_key"]?.ToString();
                    var apiSecret = message["api_secret"]?.ToString();

                    if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                    {
                        Console.WriteLine($"⚠️ Credenciales vacías en la respuesta");
                        return new FrappeCredentialsResult
                        {
                            Success = false,
                            Error = "El servicio no retornó api_key o api_secret"
                        };
                    }

                    Console.WriteLine($"✅ Credenciales obtenidas exitosamente");
                    Console.WriteLine($"🔑 API Key: {apiKey.Substring(0, Math.Min(8, apiKey.Length))}...");

                    return new FrappeCredentialsResult
                    {
                        Success = true,
                        Emisor = message["emisor"]?.ToString(),
                        ApiKey = apiKey,
                        ApiSecret = apiSecret
                    };
                }

                Console.WriteLine($"❌ El servicio no retornó success=true");
                return new FrappeCredentialsResult
                {
                    Success = false,
                    Error = "El servicio no retornó success=true"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception obteniendo credenciales: {ex.Message}");
                return new FrappeCredentialsResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }
    }
}