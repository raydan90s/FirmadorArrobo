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
        private readonly string _tempFolder = "/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica/temp_certs";

        public FrappeCertificateService(HttpClient httpClient, IOptions<FrappeSettings> options)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

            // Crear carpeta temporal si no existe
            if (!Directory.Exists(_tempFolder))
            {
                Directory.CreateDirectory(_tempFolder);
                Console.WriteLine($"📁 Carpeta temporal creada: {_tempFolder}");
            }
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

                // 1️⃣ Primero verificar que el certificado esté vigente
                var verificado = await VerificarCertificadoAsync(emisor);
                if (!verificado.vigente)
                {
                    return new DownloadCertificateResult 
                    { 
                        success = false, 
                        error = $"Certificado no vigente o no encontrado para: {emisor}" 
                    };
                }

                Console.WriteLine($"✅ Certificado vigente");
                if (!string.IsNullOrEmpty(verificado.fechaVencimiento))
                {
                    Console.WriteLine($"📅 Fecha vencimiento: {verificado.fechaVencimiento}");
                }

                // 2️⃣ Construir la URL de la API para obtener el certificado
                var apiUrl = $"{_settings.Url.TrimEnd('/')}/api/method/sri.sri.doctype.certificado_electronico.certificado_electronico.obtener_certificado";

                // 3️⃣ Crear el body JSON como espera tu API de Frappe
                var requestBody = new { emisor = emisor };
                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                // 4️⃣ Crear request POST con autenticación
                using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                req.Content = jsonContent;
                req.Headers.Add("Authorization", $"token {_settings.ApiKey}:{_settings.ApiSecret}");

                // 5️⃣ Enviar request
                var res = await _httpClient.SendAsync(req);
                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Error HTTP {res.StatusCode}: {body}");
                    return new DownloadCertificateResult 
                    { 
                        success = false, 
                        error = $"Frappe respondió {res.StatusCode}: {body}" 
                    };
                }

                // 6️⃣ Parsear respuesta de Frappe
                var json = await res.Content.ReadAsStringAsync();
                Console.WriteLine($"📥 Respuesta Frappe recibida ({json.Length} caracteres)");

                using var doc = JsonDocument.Parse(json);
                
                // La respuesta viene en message.archivo.contenido_base64
                if (!doc.RootElement.TryGetProperty("message", out var message))
                    return new DownloadCertificateResult { success = false, error = "Respuesta inválida (no 'message')" };

                // Verificar si hay un error en la respuesta
                if (message.TryGetProperty("success", out var successProp) && !successProp.GetBoolean())
                {
                    var errorMsg = message.TryGetProperty("error", out var errProp) 
                        ? errProp.GetString() 
                        : "Error desconocido desde Frappe";
                    return new DownloadCertificateResult { success = false, error = errorMsg };
                }

                // 7️⃣ Extraer el archivo en base64
                if (!message.TryGetProperty("archivo", out var archivo))
                    return new DownloadCertificateResult { success = false, error = "No se encontró 'archivo' en la respuesta" };

                if (!archivo.TryGetProperty("contenido_base64", out var base64Prop))
                    return new DownloadCertificateResult { success = false, error = "No se encontró 'contenido_base64'" };

                var base64Content = base64Prop.GetString();
                if (string.IsNullOrEmpty(base64Content))
                    return new DownloadCertificateResult { success = false, error = "El contenido base64 está vacío" };

                // 8️⃣ Extraer la contraseña
                string password = null;
                if (message.TryGetProperty("contrasena", out var passProp))
                    password = passProp.GetString();

                if (string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("⚠️ ADVERTENCIA: No se recibió contraseña del certificado");
                }
                else
                {
                    Console.WriteLine($"🔑 Contraseña recibida correctamente");
                }

                // 9️⃣ Decodificar base64 y guardar en archivo temporal
                var bytes = Convert.FromBase64String(base64Content);
                if (bytes == null || bytes.Length == 0)
                    return new DownloadCertificateResult { success = false, error = "El archivo decodificado está vacío" };

                Console.WriteLine($"📦 Certificado decodificado: {bytes.Length} bytes");

                // Guardar con nombre descriptivo y timestamp para evitar conflictos
                var fileName = archivo.TryGetProperty("nombre", out var nombreProp) 
                    ? nombreProp.GetString() 
                    : $"{emisor.Replace(" ", "_")}.p12";

                // Agregar timestamp para evitar conflictos entre requests concurrentes
                var fileNameWithTimestamp = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}.p12";
                var tempPath = Path.Combine(_tempFolder, fileNameWithTimestamp);
                
                await File.WriteAllBytesAsync(tempPath, bytes);

                Console.WriteLine($"✅ Certificado guardado en: {tempPath}");
                Console.WriteLine($"📊 Tamaño del archivo: {new FileInfo(tempPath).Length} bytes");
                
                // 🧪 PRUEBA DE VALIDACIÓN DEL CERTIFICADO
                try
                {
                    Console.WriteLine($"🧪 Validando certificado con la contraseña...");
                    var testCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(tempPath, password);
                    Console.WriteLine($"✅ ¡Certificado validado correctamente!");
                    Console.WriteLine($"📋 Subject: {testCert.Subject}");
                    Console.WriteLine($"📅 Válido desde: {testCert.NotBefore}");
                    Console.WriteLine($"📅 Válido hasta: {testCert.NotAfter}");
                    testCert.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ ERROR al validar certificado: {ex.Message}");
                    Console.WriteLine($"💡 La contraseña proporcionada NO es correcta para este certificado");
                    return new DownloadCertificateResult 
                    { 
                        success = false, 
                        filePath = tempPath,
                        password = password,
                        error = $"Certificado descargado pero contraseña incorrecta: {ex.Message}" 
                    };
                }

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
                Console.WriteLine($"❌ Excepción al descargar certificado: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return new DownloadCertificateResult 
                { 
                    success = false, 
                    error = $"Excepción: {ex.Message}" 
                };
            }
        }

        /// <summary>
        /// Verifica que el certificado exista y esté vigente
        /// </summary>
        private async Task<(bool vigente, string fechaVencimiento)> VerificarCertificadoAsync(string emisor)
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
                    Console.WriteLine($"❌ Error al verificar certificado: {res.StatusCode}");
                    return (false, null);
                }

                var json = await res.Content.ReadAsStringAsync();
                Console.WriteLine($"📋 Respuesta verificación: {json}");
                
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("message", out var message))
                    return (false, null);

                // 👇 LÓGICA CORREGIDA: Tu API devuelve directamente las propiedades
                bool vigente = false;
                string fechaVencimiento = null;

                // Verificar que tenga las 3 propiedades necesarias
                var tieneVigente = message.TryGetProperty("vigente", out var vigenteProp) && vigenteProp.GetBoolean();
                var tieneArchivo = message.TryGetProperty("tiene_archivo", out var archivoProp) && archivoProp.GetBoolean();
                var tienePassword = message.TryGetProperty("tiene_password", out var passProp) && passProp.GetBoolean();
                
                // Solo es válido si las 3 condiciones se cumplen
                vigente = tieneVigente && tieneArchivo && tienePassword;
                
                Console.WriteLine($"   📋 Vigente: {tieneVigente}");
                Console.WriteLine($"   📁 Tiene archivo: {tieneArchivo}");
                Console.WriteLine($"   🔐 Tiene password: {tienePassword}");
                Console.WriteLine($"   ✅ Resultado final: {(vigente ? "VÁLIDO" : "INVÁLIDO")}");

                // Intentar obtener fecha de vencimiento si existe
                if (message.TryGetProperty("fecha_vencimiento", out var fechaProp) && fechaProp.ValueKind != JsonValueKind.Null)
                {
                    fechaVencimiento = fechaProp.GetString();
                }

                return (vigente, fechaVencimiento);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error al verificar certificado: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return (false, null);
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
                else if (!string.IsNullOrEmpty(filePath))
                {
                    Console.WriteLine($"⚠️ Certificado ya no existe: {Path.GetFileName(filePath)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error al eliminar certificado: {ex.Message}");
                // No lanzamos la excepción para no interrumpir el flujo
            }
        }
    }
}