using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Yachasoft.Sri.FacturacionElectronica.Services
{
    public class FrappeLogoService
    {
        private readonly HttpClient _http;
        private readonly FrappeSettings _settings;

        public FrappeLogoService(HttpClient http, IOptions<FrappeSettings> settings)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<ObtenerLogoResult> ObtenerLogoAsync(string emisor)
        {
            if (string.IsNullOrWhiteSpace(emisor))
            {
                return new ObtenerLogoResult
                {
                    Success = false,
                    Error = "El parámetro 'emisor' está vacío."
                };
            }

            try
            {
                var url = $"{_settings.Url.TrimEnd('/')}/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.obtener_logo";

                var body = new { emisor };
                var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };
                request.Headers.Add("Authorization", $"token {_settings.ApiKey}:{_settings.ApiSecret}");

                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = $"HTTP {response.StatusCode}: {json}"
                    };
                }

                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("message", out var msg))
                {
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = "Respuesta inválida (no contiene 'message')"
                    };
                }

                if (!msg.TryGetProperty("logo", out var logo))
                {
                    return new ObtenerLogoResult
                    {
                        Success = false,
                        Error = "Respuesta inválida (no contiene 'logo')"
                    };
                }

                return new ObtenerLogoResult
                {
                    Success = true,
                    NombreArchivo = logo.GetProperty("nombre").GetString(),
                    LogoBase64 = logo.GetProperty("contenido_base64").GetString()
                };
            }
            catch (Exception ex)
            {
                return new ObtenerLogoResult
                {
                    Success = false,
                    Error = $"Excepción al obtener logo: {ex.Message}"
                };
            }
        }
    }

    public class ObtenerLogoResult
    {
        public bool Success { get; set; }
        public string NombreArchivo { get; set; }
        public string LogoBase64 { get; set; }
        public string Error { get; set; }
    }
}
