using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly Signer.ICertificadoService certificadoService;
        private readonly WebService.ISriWebService webService;
        private readonly Ride.IRIDEService rIDEService;
        private readonly FrappeFileUploader _frappeUploader;
        private readonly FrappeCertificateService _frappeCertService; // 👈 NUEVA DEPENDENCIA


        public FacturaController(
            Signer.ICertificadoService certificadoService,
            WebService.ISriWebService webService,
            Ride.IRIDEService rIDEService,
            FrappeFileUploader frappeUploader,
            FrappeCertificateService frappeCertService) // <-- inyectado
                                                        // FrappeCertificateService frappeCertService // 👈 INYECTADO DESDE STARTUP
        {
            this.certificadoService = certificadoService;
            this.webService = webService;
            this.rIDEService = rIDEService;
            this._frappeUploader = frappeUploader;
            this._frappeCertService = frappeCertService;
        }

        [HttpPost("GenerarFactura")]
        public async Task<IActionResult> GenerarFactura([FromBody] FacturaRequest request)
        {
            try
            {
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine($"📝 INICIANDO GENERACIÓN DE FACTURA");
                Console.WriteLine($"👤 Emisor: {request.Emisor.RazonSocial}");
                Console.WriteLine($"📅 Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                // 1️⃣ OPCIONAL: Verificar que el certificado existe y es válido en Frappe
                Console.WriteLine($"\n🔍 PASO 1: Verificando certificado en Frappe...");
                var verificacion = await _frappeCertService.VerificarCertificadoAsync(request.Emisor.RazonSocial);

                if (!verificacion.Success || !verificacion.Vigente)
                {
                    Console.WriteLine($"❌ ERROR: Certificado no vigente o no encontrado");
                    return BadRequest(new
                    {
                        success = false,
                        error = "Certificado no vigente o no encontrado",
                        detalles = new
                        {
                            vigente = verificacion.Vigente,
                            tiene_archivo = verificacion.TieneArchivo,
                            tiene_password = verificacion.TienePassword,
                            nombre_archivo = verificacion.NombreArchivo
                        }
                    });
                }

                Console.WriteLine($"✅ Certificado vigente encontrado");
                Console.WriteLine($"📄 Archivo: {verificacion.NombreArchivo}");

                // 2️⃣ Obtener certificado en Base64 desde Frappe
                Console.WriteLine($"\n🔐 PASO 2: Descargando certificado digital desde Frappe...");
                var certificado = await _frappeCertService.ObtenerCertificadoAsync(request.Emisor.RazonSocial);

                Console.WriteLine("Certificado Info:");
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    certificado.Success,
                    certificado.Emisor,
                    certificado.NombreArchivo,
                    Base64Length = certificado.CertificadoBase64?.Length ?? 0,
                    HasPassword = !string.IsNullOrEmpty(certificado.Contrasena)
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                if (!certificado.Success)
                {
                    Console.WriteLine($"❌ ERROR: {certificado.Error}");
                    return BadRequest(new
                    {
                        success = false,
                        error = $"No se pudo descargar el certificado: {certificado.Error}"
                    });
                }

                Console.WriteLine($"✅ Certificado descargado exitosamente");
                Console.WriteLine($"📦 Tamaño Base64: {certificado.CertificadoBase64?.Length ?? 0} caracteres");

                // 3️⃣ Cargar el certificado desde Base64 (NO desde archivo local)
                Console.WriteLine($"\n🔏 PASO 3: Cargando certificado en el servicio de firma desde Base64...");
                certificadoService.CargarDesdeBase64String(
                    certificado.CertificadoBase64,
                    certificado.Contrasena
                );
                Console.WriteLine($"✅ Certificado cargado correctamente en memoria");

                // 4️⃣ Construcción de emisor, establecimiento, punto de emisión y factura
                Console.WriteLine($"\n📋 PASO 4: Construyendo estructura de la factura...");

                var emisor = new Emisor
                {
                    DireccionMatriz = request.Emisor.DireccionMatriz,
                    EnumTipoAmbiente = EnumParserHelper.ParseTipoAmbiente(request.Emisor.EnumTipoAmbiente),
                    Logo = "/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica/Logo_UTPL.png",
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

                string direccionCliente = null;
                if (request.InfoAdicional != null)
                {
                    var direccionAdicional = request.InfoAdicional.FirstOrDefault(ca =>
                        ca.Nombre.Equals("Direccion", StringComparison.OrdinalIgnoreCase) ||
                        ca.Nombre.Equals("Dirección", StringComparison.OrdinalIgnoreCase));
                    direccionCliente = direccionAdicional?.Valor;
                }

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

                // 5️⃣ Generar XML y firmar
                Console.WriteLine($"\n✍️ PASO 5: Generando y firmando XML...");
                var xmlObj = Factura_1_0_0Mapper.Map(factura);

                var xmlDoc = new XmlDocument();
                using (var memoryStream = new MemoryStream())
                {
                    // Serializar usando el tipo real del objeto mapeado (corrección del bug)
                    var serializer = new XmlSerializer(xmlObj.GetType());
                    serializer.Serialize(memoryStream, xmlObj);
                    memoryStream.Position = 0;
                    xmlDoc.Load(memoryStream);
                }

                xmlDoc.DocumentElement.SetAttribute("id", "comprobante");

                // Firmar usando el certificado cargado desde Base64
                var xmlFirmado = certificadoService.FirmarDocumento(xmlDoc);

                var nombreArchivoXml = $"FACTURA_{factura.InfoTributaria.ClaveAcceso}.xml";
                var rutaXmlLocal = Path.Combine("/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica", nombreArchivoXml);
                xmlFirmado.Save(rutaXmlLocal);
                Console.WriteLine($"✅ XML firmado guardado: {nombreArchivoXml}");

                // 6️⃣ Enviar al SRI
                Console.WriteLine($"\n📤 PASO 6: Enviando comprobante al SRI...");
                var envio = await webService.ValidarComprobanteAsync(xmlFirmado);

                Console.WriteLine($"📊 Respuesta del SRI (envío):");
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(envio, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                if (envio.Ok)
                {
                    Console.WriteLine($"✅ Comprobante recibido por el SRI");
                    Console.WriteLine($"⏳ Esperando 3 segundos antes de solicitar autorización...");
                    await Task.Delay(3000);

                    Console.WriteLine($"\n🔍 PASO 7: Consultando autorización...");
                    var auto = await webService.AutorizacionComprobanteAsync(factura.InfoTributaria.ClaveAcceso);
                    var autorizacionData = auto.Data?.Autorizaciones?.Autorizacion?.FirstOrDefault();

                    Console.WriteLine($"📊 Respuesta de autorización:");
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(auto, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                    if (auto.Ok)
                    {
                        Console.WriteLine($"\n🎉 ¡COMPROBANTE AUTORIZADO!");

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

                        // 8️⃣ Generar PDF (RIDE)
                        Console.WriteLine($"\n📄 PASO 8: Generando RIDE (PDF)...");
                        var nombrePdf = $"FACTURA_{factura.InfoTributaria.ClaveAcceso}.pdf";
                        var rutaPDF = Path.Combine("/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica", nombrePdf);
                        rIDEService.Factura_1_0_0(factura, rutaPDF);
                        Console.WriteLine($"✅ PDF generado: {nombrePdf}");

                        // 9️⃣ Subir PDF y XML a Frappe
                        Console.WriteLine($"\n☁️ PASO 9: Subiendo archivos a Frappe...");

                        // Subir PDF
                        Console.WriteLine($"📤 Subiendo PDF...");
                        var respuestaUploadPDF = await _frappeUploader.UploadFileAsync(
                            rutaPDF,
                            Path.GetFileName(rutaPDF),
                            folder: "Home/Facturacion/PDF"
                        );
                        Console.WriteLine($"✅ PDF subido exitosamente");
                        Console.WriteLine($"📋 Respuesta: {respuestaUploadPDF}");

                        // Subir XML
                        Console.WriteLine($"📤 Subiendo XML...");
                        var respuestaUploadXML = await _frappeUploader.UploadFileAsync(
                            rutaXmlLocal,
                            nombreArchivoXml,
                            folder: "Home/Facturacion/XML"
                        );
                        Console.WriteLine($"✅ XML subido exitosamente");
                        Console.WriteLine($"📋 Respuesta: {respuestaUploadXML}");

                        // 10️⃣ Limpiar archivos locales
                        Console.WriteLine($"\n🗑️ PASO 10: Limpiando archivos temporales...");
                        await FileCleanupHelper.DeleteFileAsync(rutaPDF);
                        await FileCleanupHelper.DeleteFileAsync(rutaXmlLocal);
                        Console.WriteLine($"✅ Archivos locales eliminados");

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
                            respuestaFrappeXML = respuestaUploadXML
                        });
                    }

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
                else
                {
                    Console.WriteLine($"\n❌ ERROR EN EL ENVÍO AL SRI");
                    var primerComprobante = envio.Data?.Comprobantes?.Comprobante?.FirstOrDefault();
                    var mensajesEnvio = primerComprobante?.Mensajes?.Mensaje
                        ?.Select(m => new { m.Identificador, m.Mensaje_, m.Tipo, m.InformacionAdicional })
                        .ToList();

                    Console.WriteLine($"📋 Estado: {envio.Data?.Estado}");
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
                        mensajes = mensajesEnvio
                    });
                }
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
        }
    }
}
