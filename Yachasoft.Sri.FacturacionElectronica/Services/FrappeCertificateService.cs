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

    public class VerificarCertificadoResult
    {
        public bool Success { get; set; }
        public bool Vigente { get; set; }
        public bool TieneArchivo { get; set; }
        public bool TienePassword { get; set; }
        public string NombreArchivo { get; set; }
        public string FechaVencimiento { get; set; }
        public string Error { get; set; }
    }

    public class ObtenerCertificadoResult
    {
        public bool Success { get; set; }
        public string Emisor { get; set; }
        public string CertificadoBase64 { get; set; }
        public string Contrasena { get; set; }
        public string NombreArchivo { get; set; }
        public string Error { get; set; }
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
        private readonly string _tempFolder = "/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica/temp_certs";

        public FrappeCertificateService(HttpClient httpClient, IOptions<FrappeSettings> options)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

            if (!Directory.Exists(_tempFolder))
            {
                Directory.CreateDirectory(_tempFolder);
               
            }
        }

        public async Task<VerificarCertificadoResult> VerificarCertificadoAsync(
            string emisor, 
            string apiKey = null, 
            string apiSecret = null)
        {
            try
            {
                

                var apiUrl = $"{_settings.Url.TrimEnd('/')}/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.verificar_certificado";

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
                    return new VerificarCertificadoResult
                    {
                        Success = false,
                        Vigente = false,
                        Error = $"Error HTTP {res.StatusCode}: {responseBody}"
                    };
                }

                using var doc = JsonDocument.Parse(responseBody);

                if (!doc.RootElement.TryGetProperty("message", out var message))
                {    
                    return new VerificarCertificadoResult
                    {
                        Success = false,
                        Vigente = false,
                        Error = "Respuesta inválida (no 'message')"
                    };
                }

                var tieneVigente = message.TryGetProperty("vigente", out var vigenteProp) && vigenteProp.GetBoolean();
                var tieneArchivo = message.TryGetProperty("tiene_archivo", out var archivoProp) && archivoProp.GetBoolean();
                var tienePassword = message.TryGetProperty("tiene_password", out var passProp) && passProp.GetBoolean();
                
                string nombreArchivo = null;
                if (message.TryGetProperty("nombre_archivo", out var nombreProp) && nombreProp.ValueKind != JsonValueKind.Null)
                {
                    nombreArchivo = nombreProp.GetString();
                }

                string fechaVencimiento = null;
                if (message.TryGetProperty("fecha_vencimiento", out var fechaProp) && fechaProp.ValueKind != JsonValueKind.Null)
                {
                    fechaVencimiento = fechaProp.GetString();
                }

                bool vigente = tieneVigente && tieneArchivo && tienePassword;
                
                return new VerificarCertificadoResult
                {
                    Success = true,
                    Vigente = vigente,
                    TieneArchivo = tieneArchivo,
                    TienePassword = tienePassword,
                    NombreArchivo = nombreArchivo,
                    FechaVencimiento = fechaVencimiento
                };
            }
            catch (Exception ex)
            {  
                return new VerificarCertificadoResult
                {
                    Success = false,
                    Vigente = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<ObtenerCertificadoResult> ObtenerCertificadoAsync(
            string emisor,
            string apiKey = null,
            string apiSecret = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(emisor))
                {
                    return new ObtenerCertificadoResult
                    {
                        Success = false,
                        Error = "Emisor vacío"
                    };
                }
                var apiUrl = $"{_settings.Url.TrimEnd('/')}/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.obtener_certificado";

                var requestBody = new { emisor = emisor };
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
                    return new ObtenerCertificadoResult
                    {
                        Success = false,
                        Error = $"Frappe respondió {res.StatusCode}: {responseBody}"
                    };
                }

                using var doc = JsonDocument.Parse(responseBody);
                
                if (!doc.RootElement.TryGetProperty("message", out var message))
                {
                   
                    return new ObtenerCertificadoResult
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
                    return new ObtenerCertificadoResult
                    {
                        Success = false,
                        Error = errorMsg
                    };
                }

                if (!message.TryGetProperty("archivo", out var archivo))
                {
                   
                    return new ObtenerCertificadoResult
                    {
                        Success = false,
                        Error = "No se encontró 'archivo' en la respuesta"
                    };
                }

                if (!archivo.TryGetProperty("contenido_base64", out var base64Prop))
                {
                  
                    return new ObtenerCertificadoResult
                    {
                        Success = false,
                        Error = "No se encontró 'contenido_base64'"
                    };
                }

                var base64Content = base64Prop.GetString();
                if (string.IsNullOrEmpty(base64Content))
                {
                    
                    return new ObtenerCertificadoResult
                    {
                        Success = false,
                        Error = "El contenido base64 está vacío"
                    };
                }

                string password = null;
                if (message.TryGetProperty("contrasena", out var passProp))
                {
                    password = passProp.GetString();
                }

                string nombreArchivo = null;
                if (archivo.TryGetProperty("nombre", out var nombreProp))
                {
                    nombreArchivo = nombreProp.GetString();
                }

               

                return new ObtenerCertificadoResult
                {
                    Success = true,
                    Emisor = emisor,
                    CertificadoBase64 = base64Content,
                    Contrasena = password,
                    NombreArchivo = nombreArchivo
                };
            }
            catch (Exception ex)
            {
                return new ObtenerCertificadoResult
                {
                    Success = false,
                    Error = $"Excepción: {ex.Message}"
                };
            }
        }

        public async Task<DownloadCertificateResult> DownloadCertificateAsync(
            string emisor,
            string apiKey = null,
            string apiSecret = null)
        {
            try
            {
                var certificado = await ObtenerCertificadoAsync(emisor, apiKey, apiSecret);
                
                if (!certificado.Success)
                {
                    return new DownloadCertificateResult
                    {
                        success = false,
                        error = certificado.Error
                    };
                }

                var bytes = Convert.FromBase64String(certificado.CertificadoBase64);
                if (bytes == null || bytes.Length == 0)
                {
                    return new DownloadCertificateResult
                    {
                        success = false,
                        error = "El archivo decodificado está vacío"
                    };
                }

                var fileName = !string.IsNullOrEmpty(certificado.NombreArchivo)
                    ? certificado.NombreArchivo
                    : $"{emisor.Replace(" ", "_")}.p12";

                var fileNameWithTimestamp = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}.p12";
                var tempPath = Path.Combine(_tempFolder, fileNameWithTimestamp);
                
                await File.WriteAllBytesAsync(tempPath, bytes);

                try
                {
                    var testCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(tempPath, certificado.Contrasena);
                    testCert.Dispose();
                }
                catch (Exception ex)
                {
                    
                    return new DownloadCertificateResult
                    {
                        success = false,
                        filePath = tempPath,
                        password = certificado.Contrasena,
                        error = $"Certificado descargado pero contraseña incorrecta: {ex.Message}"
                    };
                }

                return new DownloadCertificateResult
                {
                    success = true,
                    filePath = tempPath,
                    password = certificado.Contrasena,
                    error = null
                };
            }
            catch (Exception ex)
            {
                
                return new DownloadCertificateResult
                {
                    success = false,
                    error = $"Excepción: {ex.Message}"
                };
            }
        }

        public void CleanupOldCertificates()
        {
            try
            {
                if (!Directory.Exists(_tempFolder)) return;

                var files = Directory.GetFiles(_tempFolder, "*.p12");
                var threshold = DateTime.Now.AddHours(-1);
                int deletedCount = 0;

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < threshold)
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                }     
            }
            catch (Exception ex)
            {    
            }
        }

        public void DeleteCertificate(string filePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    File.Delete(filePath);
                   
                }
            }
            catch (Exception ex)
            {
                
            }
        }
    }
}