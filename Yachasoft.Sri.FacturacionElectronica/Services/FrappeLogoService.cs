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

                var apiUrl = $"{_settings.Url.TrimEnd('/')}/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.obtener_logo";

                var requestBody = new { emisor };
                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                req.Content = jsonContent;

                if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
                {
                    req.Headers.Add("Authorization", $"token {apiKey}:{apiSecret}");

                }
                else
                {
                    req.Headers.Add("Authorization", $"token {_settings.ApiKey}:{_settings.ApiSecret}");

                }

                var res = await _httpClient.SendAsync(req);
                var responseBody = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                {
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = $"HTTP {res.StatusCode}: {responseBody}"
                    };
                }
                using var doc = JsonDocument.Parse(responseBody);

                if (!doc.RootElement.TryGetProperty("message", out var message))
                {

                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = "Respuesta inválida (no 'message')"
                    };
                }

                if (message.TryGetProperty("success", out var successProp) && !successProp.GetBoolean())
                {
                    var errorMsg = message.TryGetProperty("error", out var errProp)
                        ? errProp.GetString()
                        : "Error desconocido desde Frappe";
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = errorMsg
                    };
                }

                if (!message.TryGetProperty("logo", out var logo))
                {
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = "No se recibió ningún logo del emisor"
                    };
                }

                if (!logo.TryGetProperty("contenido_base64", out var base64Prop))
                {

                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = "Logo recibido sin base64"
                    };
                }

                string base64 = base64Prop.GetString();
                if (string.IsNullOrEmpty(base64))
                {

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
                return new ObtenerLogoResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }
    }
}
