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

    // 🆕 NUEVO: Clase para el resultado de verificación
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

    // 🆕 NUEVO: Clase para el resultado de obtener certificado
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
                Console.WriteLine($"📁 Carpeta temporal creada: {_tempFolder}");
            }
        }

        /// <summary>
        /// 🆕 NUEVO MÉTODO: Verifica que el certificado exista y esté vigente
        /// Este es el método que tu controlador necesita
        /// </summary>
        public async Task<VerificarCertificadoResult> VerificarCertificadoAsync(string emisor)
        {
            try
            {
                var apiUrl = $"{_settings.Url.TrimEnd('/')}/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.verificar_certificado";

                var requestBody = new { emisor = emisor };
                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                req.Content = jsonContent;
                req.Headers.Add("Authorization", $"token {_settings.ApiKey}:{_settings.ApiSecret}");

                var res = await _httpClient.SendAsync(req);
                if (!res.IsSuccessStatusCode)
                {
                    var errorBody = await res.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Error al verificar certificado: {res.StatusCode}");
                    return new VerificarCertificadoResult
                    {
                        Success = false,
                        Vigente = false,
                        Error = $"Error HTTP {res.StatusCode}: {errorBody}"
                    };
                }

                var json = await res.Content.ReadAsStringAsync();
                Console.WriteLine($"📋 Respuesta verificación: {json}");
                
                using var doc = JsonDocument.Parse(json);

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
                
                Console.WriteLine($"   📋 Vigente: {tieneVigente}");
                Console.WriteLine($"   📁 Tiene archivo: {tieneArchivo}");
                Console.WriteLine($"   🔐 Tiene password: {tienePassword}");
                Console.WriteLine($"   ✅ Resultado final: {(vigente ? "VÁLIDO" : "INVÁLIDO")}");

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
                Console.WriteLine($"❌ Error al verificar certificado: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return new VerificarCertificadoResult
                {
                    Success = false,
                    Vigente = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// 🆕 NUEVO MÉTODO: Obtiene el certificado en Base64 desde Frappe
        /// Este es el método que tu controlador necesita
        /// </summary>
        public async Task<ObtenerCertificadoResult> ObtenerCertificadoAsync(string emisor)
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

                Console.WriteLine($"🔍 Obteniendo certificado para: {emisor}");

                var apiUrl = $"{_settings.Url.TrimEnd('/')}/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.obtener_certificado";

                var requestBody = new { emisor = emisor };
                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                req.Content = jsonContent;
                req.Headers.Add("Authorization", $"token {_settings.ApiKey}:{_settings.ApiSecret}");

                var res = await _httpClient.SendAsync(req);
                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Error HTTP {res.StatusCode}: {body}");
                    return new ObtenerCertificadoResult
                    {
                        Success = false,
                        Error = $"Frappe respondió {res.StatusCode}: {body}"
                    };
                }

                var json = await res.Content.ReadAsStringAsync();
                Console.WriteLine($"📥 Respuesta Frappe recibida ({json.Length} caracteres)");

                using var doc = JsonDocument.Parse(json);
                
                if (!doc.RootElement.TryGetProperty("message", out var message))
                {
                    return new ObtenerCertificadoResult
                    {
                        Success = false,
                        Error = "Respuesta inválida (no 'message')"
                    };
                }

                // Verificar si hay error en la respuesta
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

                // Extraer el archivo en base64
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

                // Extraer contraseña
                string password = null;
                if (message.TryGetProperty("contrasena", out var passProp))
                {
                    password = passProp.GetString();
                }

                if (string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("⚠️ ADVERTENCIA: No se recibió contraseña del certificado");
                }
                else
                {
                    Console.WriteLine($"🔑 Contraseña recibida correctamente");
                }

                // Extraer nombre del archivo
                string nombreArchivo = null;
                if (archivo.TryGetProperty("nombre", out var nombreProp))
                {
                    nombreArchivo = nombreProp.GetString();
                }

                Console.WriteLine($"✅ Certificado obtenido exitosamente");
                Console.WriteLine($"📦 Tamaño Base64: {base64Content.Length} caracteres");

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
                Console.WriteLine($"❌ Excepción al obtener certificado: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return new ObtenerCertificadoResult
                {
                    Success = false,
                    Error = $"Excepción: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Descarga el certificado .p12 desde Frappe y retorna la ruta temporal + contraseña
        /// ⚠️ MÉTODO LEGACY - Usa ObtenerCertificadoAsync en su lugar
        /// </summary>
        public async Task<DownloadCertificateResult> DownloadCertificateAsync(string emisor)
        {
            try
            {
                // Primero obtener el certificado en Base64
                var certificado = await ObtenerCertificadoAsync(emisor);
                
                if (!certificado.Success)
                {
                    return new DownloadCertificateResult
                    {
                        success = false,
                        error = certificado.Error
                    };
                }

                // Decodificar y guardar en archivo temporal
                var bytes = Convert.FromBase64String(certificado.CertificadoBase64);
                if (bytes == null || bytes.Length == 0)
                {
                    return new DownloadCertificateResult
                    {
                        success = false,
                        error = "El archivo decodificado está vacío"
                    };
                }

                Console.WriteLine($"📦 Certificado decodificado: {bytes.Length} bytes");

                var fileName = !string.IsNullOrEmpty(certificado.NombreArchivo)
                    ? certificado.NombreArchivo
                    : $"{emisor.Replace(" ", "_")}.p12";

                var fileNameWithTimestamp = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}.p12";
                var tempPath = Path.Combine(_tempFolder, fileNameWithTimestamp);
                
                await File.WriteAllBytesAsync(tempPath, bytes);

                Console.WriteLine($"✅ Certificado guardado en: {tempPath}");
                
                // Validar certificado
                try
                {
                    Console.WriteLine($"🧪 Validando certificado con la contraseña...");
                    var testCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(tempPath, certificado.Contrasena);
                    Console.WriteLine($"✅ ¡Certificado validado correctamente!");
                    Console.WriteLine($"📋 Subject: {testCert.Subject}");
                    testCert.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ ERROR al validar certificado: {ex.Message}");
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
                Console.WriteLine($"❌ Excepción al descargar certificado: {ex.Message}");
                return new DownloadCertificateResult
                {
                    success = false,
                    error = $"Excepción: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Limpia archivos de certificados temporales antiguos (más de 1 hora)
        /// </summary>
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

                if (deletedCount > 0)
                {
                    Console.WriteLine($"🗑️ {deletedCount} certificado(s) temporal(es) eliminado(s)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error al limpiar certificados temporales: {ex.Message}");
            }
        }

        /// <summary>
        /// Elimina un certificado específico de forma segura
        /// </summary>
        public void DeleteCertificate(string filePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Console.WriteLine($"🗑️ Certificado temporal eliminado: {Path.GetFileName(filePath)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error al eliminar certificado: {ex.Message}");
            }
        }
    }
}