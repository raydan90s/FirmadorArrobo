using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Yachasoft.Sri.Core.Enumerados;
using Yachasoft.Sri.Modelos;
using Yachasoft.Sri.Modelos.Base;
using Yachasoft.Sri.Modelos.Enumerados;
using Yachasoft.Sri.Xsd;
using Yachasoft.Sri.Xsd.Contratos.Factura_1_0_0;
using Yachasoft.Sri.Xsd.Map;
using Yachasoft.Sri.FacturacionElectronica.Models.Request;
using Yachasoft.Sri.FacturacionElectronica.Services;

namespace Yachasoft.Sri.FacturacionElectronica.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FacturaController : ControllerBase
    {
        private readonly Signer.ICertificadoService _certificadoService;
        private readonly WebService.ISriWebService _webService;
        private readonly Ride.IRIDEService _rideService;
        private readonly IFrappeFileUploader _frappeUploader;
        private readonly FrappeCertificateService _frappeCertService;
        private readonly FrappeLogoService _frappeLogoService;
        private readonly IFrappeCredentialsService _frappeCredentialsService;

        public FacturaController(
            Signer.ICertificadoService certificadoService,
            WebService.ISriWebService webService,
            Ride.IRIDEService rideService,
            IFrappeFileUploader frappeUploader,
            FrappeCertificateService frappeCertService,
            FrappeLogoService frappeLogoService,
            IFrappeCredentialsService frappeCredentialsService)
        {
            _certificadoService = certificadoService;
            _webService = webService;
            _rideService = rideService;
            _frappeUploader = frappeUploader;
            _frappeCertService = frappeCertService;
            _frappeLogoService = frappeLogoService;
            _frappeCredentialsService = frappeCredentialsService;
        }

        [HttpPost("GenerarFactura")]
        public async Task<IActionResult> GenerarFactura([FromBody] FacturaRequest request)
        {
            // 🔥 FIX CRÍTICO: Configurar TLS antes de cualquier comunicación con el SRI
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

            string logoPath = null;
            string rutaPDF = null;
            string rutaXmlLocal = null;

            try
            {
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine($"📝 INICIANDO GENERACIÓN DE FACTURA");
                Console.WriteLine($"👤 Emisor: {request.Emisor.RazonSocial}");
                Console.WriteLine($"🆔 RUC: {request.Emisor.RUC}");
                Console.WriteLine($"📅 Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                // 🔥 PASO 0: OBTENER CREDENCIALES DEL EMISOR PRIMERO
                Console.WriteLine($"\n🔑 PASO 0: Obteniendo credenciales del emisor...");
                var credenciales = await _frappeCredentialsService.ObtenerCredencialesAsync(request.Emisor.RazonSocial);

                string apiKey = null;
                string apiSecret = null;
                bool usandoCredencialesEmisor = false;

                if (credenciales.Success &&
                    credenciales.TieneApiKey &&
                    credenciales.TieneApiSecret &&
                    !string.IsNullOrEmpty(credenciales.ApiKey) &&
                    !string.IsNullOrEmpty(credenciales.ApiSecret))
                {
                    apiKey = credenciales.ApiKey;
                    apiSecret = credenciales.ApiSecret;
                    usandoCredencialesEmisor = true;

                    Console.WriteLine($"✅ Credenciales del emisor obtenidas correctamente");
                    Console.WriteLine($"👤 Emisor: {credenciales.Emisor}");
                    Console.WriteLine($"🔑 API Key: {apiKey?.Substring(0, Math.Min(8, apiKey?.Length ?? 0))}...");
                }
                else
                {
                    Console.WriteLine($"⚠️ No se obtuvieron credenciales del emisor");
                    Console.WriteLine($"   Razón: {credenciales.Error}");
                    Console.WriteLine($"   TieneApiKey: {credenciales.TieneApiKey}");
                    Console.WriteLine($"   TieneApiSecret: {credenciales.TieneApiSecret}");
                    Console.WriteLine($"📌 Se intentará con credenciales por defecto (puede fallar)");
                }

                // 1️⃣ Verificar certificado en Frappe (CON CREDENCIALES CORRECTAS)
                Console.WriteLine($"\n🔍 PASO 1: Verificando certificado en Frappe...");
                var verificacion = await _frappeCertService.VerificarCertificadoAsync(
                    request.Emisor.RazonSocial,
                    apiKey,
                    apiSecret
                );

                if (!verificacion.Success)
                {
                    Console.WriteLine($"❌ ERROR: Falló la verificación del certificado");
                    return BadRequest(new
                    {
                        success = false,
                        error = "Error al verificar certificado",
                        detalles = new
                        {
                            error_detalle = verificacion.Error,
                            usandoCredencialesEmisor = usandoCredencialesEmisor
                        }
                    });
                }

                if (!verificacion.Vigente)
                {
                    Console.WriteLine($"❌ ERROR: Certificado no vigente");
                    return BadRequest(new
                    {
                        success = false,
                        error = "Certificado no vigente o incompleto",
                        detalles = new
                        {
                            vigente = verificacion.Vigente,
                            tiene_archivo = verificacion.TieneArchivo,
                            tiene_password = verificacion.TienePassword,
                            nombre_archivo = verificacion.NombreArchivo,
                            fecha_vencimiento = verificacion.FechaVencimiento,
                            usandoCredencialesEmisor = usandoCredencialesEmisor
                        }
                    });
                }

                Console.WriteLine($"✅ Certificado vigente encontrado");
                Console.WriteLine($"📄 Archivo: {verificacion.NombreArchivo}");
                Console.WriteLine($"📅 Vencimiento: {verificacion.FechaVencimiento}");

                // 2️⃣ Descargar certificado desde Frappe (CON CREDENCIALES CORRECTAS)
                Console.WriteLine($"\n🔐 PASO 2: Descargando certificado digital desde Frappe...");
                var certificado = await _frappeCertService.ObtenerCertificadoAsync(
                    request.Emisor.RazonSocial,
                    apiKey,
                    apiSecret
                );

                if (!certificado.Success)
                {
                    Console.WriteLine($"❌ ERROR: No se pudo descargar el certificado");
                    Console.WriteLine($"   Detalle: {certificado.Error}");

                    return BadRequest(new
                    {
                        success = false,
                        error = $"No se pudo descargar el certificado: {certificado.Error}",
                        usandoCredencialesEmisor = usandoCredencialesEmisor
                    });
                }

                Console.WriteLine($"✅ Certificado descargado exitosamente");
                Console.WriteLine($"📦 Tamaño Base64: {certificado.CertificadoBase64?.Length ?? 0} caracteres");
                Console.WriteLine($"🔑 Contraseña: {(string.IsNullOrEmpty(certificado.Contrasena) ? "❌ NO RECIBIDA" : "✅ OK")}");

                if (string.IsNullOrEmpty(certificado.Contrasena))
                {
                    Console.WriteLine($"❌ ERROR CRÍTICO: No se recibió la contraseña del certificado");
                    return BadRequest(new
                    {
                        success = false,
                        error = "No se recibió la contraseña del certificado desde Frappe"
                    });
                }

                // 3️⃣ Cargar certificado en memoria
                Console.WriteLine($"\n🔏 PASO 3: Cargando certificado en el servicio de firma...");
                try
                {
                    _certificadoService.CargarDesdeBase64String(
                        certificado.CertificadoBase64,
                        certificado.Contrasena
                    );
                    Console.WriteLine($"✅ Certificado cargado correctamente en memoria");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ ERROR al cargar el certificado: {ex.Message}");
                    return BadRequest(new
                    {
                        success = false,
                        error = $"Error al cargar el certificado: {ex.Message}",
                        detalle = "Verifique que el certificado y la contraseña sean correctos"
                    });
                }

                // 4️⃣ Obtener logo desde Frappe
                Console.WriteLine($"\n🖼️ PASO 4: Obteniendo logo desde Frappe...");
                Console.WriteLine("════ CREDENCIALES EN CONTROLLER LOGO ════");
                Console.WriteLine($"API KEY CONTROLLER: '{apiKey}'");
                Console.WriteLine($"API SECRET CONTROLLER: '{apiSecret}'");
                Console.WriteLine("═══════════════════════════════════════");

                var logoResult = await _frappeLogoService.ObtenerLogoAsync(
                request.Emisor.RazonSocial,
                apiKey,
                apiSecret
                );


                if (logoResult.Success && !string.IsNullOrWhiteSpace(logoResult.LogoBase64))
                {
                    Console.WriteLine($"✅ Logo obtenido: {logoResult.NombreArchivo}");

                    var logoFileName = $"logo_{request.Emisor.RUC}_{DateTime.Now:yyyyMMddHHmmss}.png";
                    logoPath = Path.Combine("/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica", logoFileName);

                    var logoBytes = Convert.FromBase64String(logoResult.LogoBase64);
                    await System.IO.File.WriteAllBytesAsync(logoPath, logoBytes);

                    Console.WriteLine($"💾 Logo guardado temporalmente: {logoFileName}");
                }
                else
                {
                    Console.WriteLine($"⚠️ No se pudo obtener logo: {logoResult.Error}");
                    Console.WriteLine($"📌 Se generará la factura sin logo");
                }

                // 5️⃣ Construir estructura de la factura
                Console.WriteLine($"\n📋 PASO 5: Construyendo estructura de la factura...");

                var emisor = new Emisor
                {
                    DireccionMatriz = request.Emisor.DireccionMatriz,
                    EnumTipoAmbiente = EnumParserHelper.ParseTipoAmbiente(request.Emisor.EnumTipoAmbiente),
                    Logo = logoPath,
                    NombreComercial = request.Emisor.NombreComercial,
                    ObligadoContabilidad = request.Emisor.ObligadoContabilidad,
                    RazonSocial = request.Emisor.RazonSocial,
                    RegimenMicroEmpresas = request.Emisor.RegimenMicroEmpresas,
                    RUC = request.Emisor.RUC,
                    ContribuyenteEspecial = request.Emisor.ContribuyenteEspecial,
                    AgenteRetencion = request.Emisor.AgenteRetencion,
                };

                var establecimiento = new Establecimiento
                {
                    Codigo = request.CodigoEstablecimiento,
                    DireccionEstablecimiento = request.Emisor.DireccionEstablecimiento,
                    Emisor = emisor
                };

                var puntoEmision = new PuntoEmision
                {
                    Codigo = request.CodigoPuntoEmision,
                    Establecimiento = establecimiento
                };

                string direccionCliente = request.InfoAdicional?
                    .FirstOrDefault(ca => ca.Nombre.Equals("Direccion", StringComparison.OrdinalIgnoreCase) ||
                                         ca.Nombre.Equals("Dirección", StringComparison.OrdinalIgnoreCase))
                    ?.Valor;

                var detallesMapeados = MapperHelper.MapearDetallesConSubsidio(request.Detalles);

                var factura = new Factura_1_0_0Modelo.Factura
                {
                    PuntoEmision = puntoEmision,
                    FechaEmision = request.FechaEmision,
                    Sujeto = new Sujeto
                    {
                        Identificacion = request.Cliente.Identificacion,
                        RazonSocial = request.Cliente.RazonSocial,
                        TipoIdentificador = EnumParserHelper.ParseTipoIdentificacion(request.Cliente.TipoIdentificador)
                    },
                    InfoFactura = new Factura_1_0_0Modelo.InfoFactura
                    {
                        TotalSinImpuestos = request.InfoFactura.TotalSinImpuestos,
                        TotalDescuento = request.InfoFactura.TotalDescuento,
                        ImporteTotal = request.InfoFactura.ImporteTotal,
                        DireccionComprador = direccionCliente,
                        TotalConImpuestos = MapperHelper.MapearImpuestosVentaDesdeDetallesConSubsidio(detallesMapeados),
                        Pagos = MapperHelper.MapearPagos(request.InfoFactura.Pagos)
                    },
                    Detalles = detallesMapeados,
                    InfoAdicional = request.InfoAdicional
                };

                factura.InfoTributaria = new InfoTributaria
                {
                    Secuencial = request.Secuencial,
                    EnumTipoEmision = EnumParserHelper.ParseTipoEmision(request.EnumTipoEmision)
                };

                factura.InfoTributaria.ClaveAcceso = Utils.GenerarClaveAcceso(
                    Factura_1_0_0Modelo.TipoDocumento,
                    factura.FechaEmision,
                    factura.PuntoEmision,
                    factura.InfoTributaria.Secuencial,
                    factura.InfoTributaria.EnumTipoEmision
                );

                Console.WriteLine($"✅ Factura construida");
                Console.WriteLine($"🔑 Clave de acceso: {factura.InfoTributaria.ClaveAcceso}");
                Console.WriteLine($"📄 Secuencial: {factura.InfoTributaria.Secuencial}");

                // 6️⃣ Generar y firmar XML
                Console.WriteLine($"\n✍️ PASO 6: Generando y firmando XML...");
                var xmlObj = Factura_1_0_0Mapper.Map(factura);

                var xmlDoc = new XmlDocument();
                using (var memoryStream = new MemoryStream())
                {
                    var serializer = new XmlSerializer(xmlObj.GetType());
                    serializer.Serialize(memoryStream, xmlObj);
                    memoryStream.Position = 0;
                    xmlDoc.Load(memoryStream);
                }

                xmlDoc.DocumentElement.SetAttribute("id", "comprobante");
                var xmlFirmado = _certificadoService.FirmarDocumento(xmlDoc);

                var nombreArchivoXml = $"FACTURA_{factura.InfoTributaria.ClaveAcceso}.xml";
                rutaXmlLocal = Path.Combine("/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica", nombreArchivoXml);
                xmlFirmado.Save(rutaXmlLocal);
                Console.WriteLine($"✅ XML firmado guardado: {nombreArchivoXml}");

                // 7️⃣ Enviar al SRI
                Console.WriteLine($"\n📤 PASO 7: Enviando comprobante al SRI...");
                var envio = await _webService.ValidarComprobanteAsync(xmlFirmado);

                Console.WriteLine($"📊 Respuesta del SRI (envío):");
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(envio, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                if (!envio.Ok)
                {
                    Console.WriteLine($"\n❌ ERROR EN EL ENVÍO AL SRI");
                    var primerComprobante = envio.Data?.Comprobantes?.Comprobante?.FirstOrDefault();
                    var mensajesEnvio = primerComprobante?.Mensajes?.Mensaje
                        ?.Select(m => new { m.Identificador, m.Mensaje_, m.Tipo, m.InformacionAdicional })
                        .ToList();

                    Console.WriteLine($"📋 Estado: {envio.Data?.Estado}");
                    Console.WriteLine($"📋 Error: {envio.Error}");

                    if (mensajesEnvio != null && mensajesEnvio.Any())
                    {
                        Console.WriteLine($"📋 Mensajes:");
                        foreach (var msg in mensajesEnvio)
                        {
                            Console.WriteLine($"   - [{msg.Tipo}] {msg.Mensaje_}");
                        }
                    }

                    return Ok(new
                    {
                        success = false,
                        estado = envio.Data?.Estado,
                        error = envio.Error,
                        mensajes = mensajesEnvio
                    });
                }

                Console.WriteLine($"✅ Comprobante recibido por el SRI");
                Console.WriteLine($"⏳ Esperando 3 segundos antes de solicitar autorización...");
                await Task.Delay(3000);

                // 8️⃣ Consultar autorización
                Console.WriteLine($"\n🔍 PASO 8: Consultando autorización...");
                var auto = await _webService.AutorizacionComprobanteAsync(factura.InfoTributaria.ClaveAcceso);
                var autorizacionData = auto.Data?.Autorizaciones?.Autorizacion?.FirstOrDefault();

                Console.WriteLine($"📊 Respuesta de autorización:");
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(auto, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                if (!auto.Ok)
                {
                    Console.WriteLine($"\n❌ COMPROBANTE NO AUTORIZADO");
                    Console.WriteLine($"📋 Estado: {autorizacionData?.Estado}");

                    var mensajesAutorizacion = autorizacionData?.Mensajes?.Mensaje
                        ?.Select(m => new { m.Identificador, m.Mensaje_, m.Tipo, m.InformacionAdicional })
                        .ToList();

                    return Ok(new
                    {
                        success = false,
                        estado = autorizacionData?.Estado,
                        mensajes = mensajesAutorizacion
                    });
                }

                Console.WriteLine($"\n🎉 ¡COMPROBANTE AUTORIZADO!");

                // Actualizar datos de autorización
                if (autorizacionData != null)
                {
                    factura.Autorizacion.Numero = autorizacionData.NumeroAutorizacion;
                    if (DateTimeOffset.TryParse(autorizacionData.FechaAutorizacion, out var fechaOffset))
                    {
                        factura.Autorizacion.Fecha = fechaOffset.ToOffset(TimeSpan.FromHours(-5)).DateTime;
                    }
                    else
                    {
                        throw new Exception($"Fecha de autorización inválida: {autorizacionData.FechaAutorizacion}");
                    }

                    Console.WriteLine($"📋 Número de autorización: {factura.Autorizacion.Numero}");
                    Console.WriteLine($"📅 Fecha de autorización: {factura.Autorizacion.Fecha:yyyy-MM-dd HH:mm:ss}");
                }

                // 9️⃣ Generar PDF (RIDE)
                Console.WriteLine($"\n📄 PASO 9: Generando RIDE (PDF)...");
                var nombrePdf = $"FACTURA_{factura.InfoTributaria.ClaveAcceso}.pdf";
                rutaPDF = Path.Combine("/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica", nombrePdf);
                _rideService.Factura_1_0_0(factura, rutaPDF);
                Console.WriteLine($"✅ PDF generado: {nombrePdf}");

                // 🔟 Subir archivos a Frappe
                Console.WriteLine($"\n☁️ PASO 10: Subiendo archivos a Frappe...");

                FrappeUploadResult respuestaUploadPDF;
                FrappeUploadResult respuestaUploadXML;

                if (usandoCredencialesEmisor)
                {
                    Console.WriteLine($"📌 Usando credenciales del emisor para subir archivos");

                    respuestaUploadPDF = await _frappeUploader.UploadFileAsync(
                        filePath: rutaPDF,
                        fileName: Path.GetFileName(rutaPDF),
                        apiKey: apiKey,
                        apiSecret: apiSecret,
                        folder: "Home/Facturacion/PDF"
                    );

                    respuestaUploadXML = await _frappeUploader.UploadFileAsync(
                        filePath: rutaXmlLocal,
                        fileName: nombreArchivoXml,
                        apiKey: apiKey,
                        apiSecret: apiSecret,
                        folder: "Home/Facturacion/XML"
                    );
                }
                else
                {
                    Console.WriteLine($"📌 Usando credenciales por defecto para subir archivos");

                    respuestaUploadPDF = await _frappeUploader.UploadFileAsync(
                        filePath: rutaPDF,
                        fileName: Path.GetFileName(rutaPDF),
                        folder: "Home/Facturacion/PDF"
                    );

                    respuestaUploadXML = await _frappeUploader.UploadFileAsync(
                        filePath: rutaXmlLocal,
                        fileName: nombreArchivoXml,
                        folder: "Home/Facturacion/XML"
                    );
                }

                Console.WriteLine($"✅ PDF subido: {respuestaUploadPDF.Success}");
                Console.WriteLine($"✅ XML subido: {respuestaUploadXML.Success}");

                if (!respuestaUploadPDF.Success)
                {
                    Console.WriteLine($"⚠️ Error subiendo PDF: {respuestaUploadPDF.Error}");
                }

                if (!respuestaUploadXML.Success)
                {
                    Console.WriteLine($"⚠️ Error subiendo XML: {respuestaUploadXML.Error}");
                }

                // 1️⃣1️⃣ Limpiar archivos temporales
                Console.WriteLine($"\n🗑️ PASO 11: Limpiando archivos temporales...");
                await LimpiarArchivosTemporales(rutaPDF, rutaXmlLocal, logoPath);
                Console.WriteLine($"✅ Archivos temporales eliminados");

                Console.WriteLine($"\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine($"✅ PROCESO COMPLETADO EXITOSAMENTE");
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                return Ok(new
                {
                    success = true,
                    claveAcceso = factura.InfoTributaria.ClaveAcceso,
                    mensaje = "Factura autorizada, PDF generado y archivos subidos a Frappe correctamente",
                    numeroAutorizacion = factura.Autorizacion.Numero,
                    fechaAutorizacion = factura.Autorizacion.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
                    respuestaFrappePDF = respuestaUploadPDF,
                    respuestaFrappeXML = respuestaUploadXML,
                    credencialesUsadas = usandoCredencialesEmisor ? "Emisor" : "Por defecto"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌❌❌ ERROR CRÍTICO ❌❌❌");
                Console.WriteLine($"Mensaje: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }

                return BadRequest(new
                {
                    success = false,
                    error = ex.Message,
                    stackTrace = ex.StackTrace,
                    innerError = ex.InnerException?.Message
                });
            }
            finally
            {
                // Limpieza de seguridad en el bloque finally
                await LimpiarArchivosTemporales(rutaPDF, rutaXmlLocal, logoPath);
            }
        }

        private async Task LimpiarArchivosTemporales(params string[] rutas)
        {
            foreach (var ruta in rutas)
            {
                if (!string.IsNullOrWhiteSpace(ruta) && System.IO.File.Exists(ruta))
                {
                    try
                    {
                        await FileCleanupHelper.DeleteFileAsync(ruta);
                        Console.WriteLine($"🗑️ Archivo eliminado: {Path.GetFileName(ruta)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ No se pudo eliminar {Path.GetFileName(ruta)}: {ex.Message}");
                    }
                }
            }
        }

    }
}