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
using Yachasoft.Sri.Xsd.Contratos.NotaDebito_1_0_0;
using Yachasoft.Sri.Xsd.Map;
using Yachasoft.Sri.FacturacionElectronica.Models.Request;
using Yachasoft.Sri.FacturacionElectronica.Services;

namespace Yachasoft.Sri.FacturacionElectronica.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotaDebitoController : ControllerBase
    {
        private readonly Signer.ICertificadoService _certificadoService;
        private readonly WebService.ISriWebService _webService;
        private readonly Ride.IRIDEService _rideService;
        private readonly IFrappeFileUploader _frappeUploader;
        private readonly FrappeCertificateService _frappeCertService;
        private readonly FrappeLogoService _frappeLogoService;
        private readonly IFrappeCredentialsService _frappeCredentialsService;

        public NotaDebitoController(
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

        [HttpPost("GenerarNotaDebito")]
        public async Task<IActionResult> GenerarNotaDebito([FromBody] NotaDebitoRequest request)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            ServicePointManager.DefaultConnectionLimit = 10;
            ServicePointManager.Expect100Continue = false;

            string logoPath = null;
            string rutaPDF = null;
            string rutaXmlLocal = null;

            try
            {
                var tempDir = Path.GetTempPath();

                // ✅ NUEVAS RUTAS LOCALES PARA DESARROLLO
                var rutaCertificadoLocal = Path.Combine(Directory.GetCurrentDirectory(), "devPruebasCertificados", "signature.p12");
                var rutaLogoLocal = Path.Combine(Directory.GetCurrentDirectory(), "devPruebasCertificados", "logo.png");
                var contrasenaP12 = "Compus1234"; // Contraseña hardcodeada para desarrollo

                // ✅ LÓGICA: Si viene en el request, usar eso. Si no, usar archivos locales
                string certificadoP12Base64;
                string contrasenaP12Final;

                if (string.IsNullOrEmpty(request.CertificadoP12Base64))
                {
                    Console.WriteLine("🔧 MODO DESARROLLO: Usando certificado local");
                    
                    if (!System.IO.File.Exists(rutaCertificadoLocal))
                    {
                        return BadRequest(new 
                        { 
                            success = false, 
                            error = $"Certificado local no encontrado en: {rutaCertificadoLocal}" 
                        });
                    }

                    var certBytes = await System.IO.File.ReadAllBytesAsync(rutaCertificadoLocal);
                    certificadoP12Base64 = Convert.ToBase64String(certBytes);
                    contrasenaP12Final = string.IsNullOrEmpty(request.ContrasenaP12) ? contrasenaP12 : request.ContrasenaP12;
                    
                    Console.WriteLine($"✅ Certificado cargado desde: {rutaCertificadoLocal}");
                    Console.WriteLine($"✅ Tamaño: {certBytes.Length:N0} bytes");
                }
                else
                {
                    Console.WriteLine("📤 MODO PRODUCCIÓN: Usando certificado del request");
                    certificadoP12Base64 = request.CertificadoP12Base64;
                    contrasenaP12Final = request.ContrasenaP12;

                    if (string.IsNullOrEmpty(contrasenaP12Final))
                    {
                        return BadRequest(new { success = false, error = "Contraseña del certificado requerida" });
                    }
                }

                // Cargar certificado
                try
                {
                    _certificadoService.CargarDesdeBase64String(certificadoP12Base64, contrasenaP12Final);
                    Console.WriteLine("✅ Certificado cargado correctamente");
                }
                catch (Exception ex)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = $"Error al cargar el certificado: {ex.Message}",
                        detalle = "Verifique que el certificado y la contraseña sean correctos"
                    });
                }

                // ✅ MANEJO DEL LOGO
                if (string.IsNullOrEmpty(request.LogoBase64))
                {
                    Console.WriteLine("🔧 MODO DESARROLLO: Usando logo local");
                    
                    if (System.IO.File.Exists(rutaLogoLocal))
                    {
                        logoPath = rutaLogoLocal;
                        Console.WriteLine($"✅ Logo cargado desde: {rutaLogoLocal}");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ Logo local no encontrado en: {rutaLogoLocal}");
                        Console.WriteLine("⚠️ Continuando sin logo");
                    }
                }
                else
                {
                    Console.WriteLine("📤 MODO PRODUCCIÓN: Usando logo del request");
                    var logoFileName = $"logo_{request.Emisor.RUC}_{DateTime.Now:yyyyMMddHHmmss}.png";
                    logoPath = Path.Combine(tempDir, logoFileName);
                    var logoBytes = Convert.FromBase64String(request.LogoBase64);
                    await System.IO.File.WriteAllBytesAsync(logoPath, logoBytes);
                    Console.WriteLine($"✅ Logo temporal creado: {logoPath}");
                }

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

                var motivos = request.Motivos.Select(m => new Motivo
                {
                    Razon = m.Razon,
                    Valor = m.Valor
                }).ToList();

                var documentoModificado = new DocumentoSustento
                {
                    CodDocumento = EnumParserHelper.ParseTipoDocumento(request.DocumentoModificado.CodDocumento),
                    NumDocumento = request.DocumentoModificado.NumDocumento,
                    FechaEmisionDocumento = request.DocumentoModificado.FechaEmisionDocumento
                };

                var impuestosMapeados = MapperHelper.MapearImpuestosVenta(request.InfoNotaDebito.Impuestos);

                DateTime fechaEmision;
                if (!DateTime.TryParse(request.FechaEmision, out fechaEmision))
                {
                    fechaEmision = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                        DateTime.UtcNow,
                        "SA Pacific Standard Time"
                    );
                }

                var notaDebito = new NotaDebito_1_0_0Modelo.NotaDebito
                {
                    PuntoEmision = puntoEmision,
                    FechaEmision = fechaEmision,
                    Sujeto = new Sujeto
                    {
                        Identificacion = request.Cliente.Identificacion,
                        RazonSocial = request.Cliente.RazonSocial,
                        TipoIdentificador = EnumParserHelper.ParseTipoIdentificacion(request.Cliente.TipoIdentificador)
                    },
                    InfoNotaDebito = new NotaDebito_1_0_0Modelo.InfoNotaDebito
                    {
                        DocumentoModificado = documentoModificado,
                        TotalSinImpuestos = request.InfoNotaDebito.TotalSinImpuestos,
                        Impuestos = impuestosMapeados,
                        ValorTotal = request.InfoNotaDebito.ValorTotal,
                        Pagos = MapperHelper.MapearPagosParaDocumento(request.InfoNotaDebito.Pagos)
                    },
                    Motivos = motivos,
                    InfoAdicional = request.InfoAdicional
                };

                notaDebito.InfoTributaria = new InfoTributaria
                {
                    Secuencial = request.Secuencial,
                    EnumTipoEmision = EnumParserHelper.ParseTipoEmision(request.EnumTipoEmision)
                };

                notaDebito.InfoTributaria.ClaveAcceso = Utils.GenerarClaveAcceso(
                    NotaDebito_1_0_0Modelo.TipoDocumento,
                    notaDebito.FechaEmision,
                    notaDebito.PuntoEmision,
                    notaDebito.InfoTributaria.Secuencial,
                    notaDebito.InfoTributaria.EnumTipoEmision
                );

                Console.WriteLine("========================================");
                Console.WriteLine("🔑 DEBUG DE CLAVE DE ACCESO");
                Console.WriteLine("========================================");
                Console.WriteLine($"Fecha usada: {notaDebito.FechaEmision:dd-MM-yyyy HH:mm:ss.fff}");
                Console.WriteLine($"Secuencial: {notaDebito.InfoTributaria.Secuencial}");
                Console.WriteLine($"Clave generada: {notaDebito.InfoTributaria.ClaveAcceso}");
                Console.WriteLine("========================================");

                var xmlObj = NotaDebito_1_0_0Mapper.Map(notaDebito);

                var xmlDoc = new XmlDocument();
                using (var memoryStream = new MemoryStream())
                {
                    var serializer = new XmlSerializer(xmlObj.GetType());
                    serializer.Serialize(memoryStream, xmlObj);
                    memoryStream.Position = 0;
                    xmlDoc.Load(memoryStream);
                }

                // ---------------------------------------------------------
                // 1. CORRECCIÓN DE FECHA
                // ---------------------------------------------------------
                var fechaNode = xmlDoc.SelectSingleNode("//fechaEmision");
                if (fechaNode != null)
                {
                    var formato3 = $"{fechaEmision.Day:D2}/{fechaEmision.Month:D2}/{fechaEmision.Year}";
                    fechaNode.InnerText = formato3;
                }

                // =========================================================
                // 👇👇 2. INICIO DEL BLOQUE NUEVO: CORRECCIÓN DE DECIMALES 👇👇
                // =========================================================
                
                void CorregirDecimales(XmlDocument doc, string xpath, string formato)
                {
                    var nodos = doc.SelectNodes(xpath);
                    if (nodos == null) return;
                    foreach (XmlNode nodo in nodos)
                    {
                        if (decimal.TryParse(nodo.InnerText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal valor))
                        {
                            nodo.InnerText = valor.ToString(formato, System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                }

                CorregirDecimales(xmlDoc, "//totalSinImpuestos", "F2");
                CorregirDecimales(xmlDoc, "//valorTotal", "F2");
                CorregirDecimales(xmlDoc, "//baseImponible", "F2");
                CorregirDecimales(xmlDoc, "//valor", "F2");
                CorregirDecimales(xmlDoc, "//total", "F2"); 

                xmlDoc.DocumentElement.SetAttribute("id", "comprobante");
                var xmlFirmado = _certificadoService.FirmarDocumento(xmlDoc);

                var nombreArchivoXml = $"NOTADEBITO_{notaDebito.InfoTributaria.ClaveAcceso}.xml";
                rutaXmlLocal = Path.Combine(tempDir, nombreArchivoXml);
                xmlFirmado.Save(rutaXmlLocal);

                Console.WriteLine("========================================");
                Console.WriteLine("📄 XML GENERADO Y FIRMADO (PREVIO AL ENVÍO)");
                Console.WriteLine("========================================");
                using (var stringWriter = new StringWriter())
                using (var xmlTextWriter = new XmlTextWriter(stringWriter))
                {
                    xmlTextWriter.Formatting = Formatting.Indented;
                    xmlFirmado.WriteTo(xmlTextWriter);
                    Console.WriteLine(stringWriter.ToString());
                }
                Console.WriteLine("========================================");

                // ========================================
                // ✅ ENVÍO AL SRI CON LOGGING COMPLETO
                // ========================================
                Console.WriteLine("========================================");
                Console.WriteLine("📤 ENVIANDO NOTA DE DÉBITO AL SRI");
                Console.WriteLine("========================================");
                dynamic envio = null;
                
                try
                {
                    envio = await _webService.ValidarComprobanteAsync(xmlFirmado);
                    
                    Console.WriteLine("========================================");
                    Console.WriteLine("📥 RESPUESTA COMPLETA DEL SRI - ENVÍO");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"✓ envio != null: {envio != null}");
                    Console.WriteLine($"✓ envio.Ok: {envio?.Ok}");
                    Console.WriteLine($"✓ envio.Error: '{envio?.Error}'");
                    Console.WriteLine($"✓ envio.Data != null: {envio?.Data != null}");
                    
                    if (envio?.Data != null)
                    {
                        var estado = envio.Data.Estado;
                        Console.WriteLine($"✓ Estado: '{estado}'");
                    }
                }
                catch (Exception exEnvio)
                {
                    Console.WriteLine("========================================");
                    Console.WriteLine("❌ EXCEPCIÓN AL ENVIAR AL SRI");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"Mensaje: {exEnvio.Message}");
                    return BadRequest(new
                    {
                        success = false,
                        error = "Error al conectar con el SRI",
                        detalleError = exEnvio.Message,
                        innerError = exEnvio.InnerException?.Message
                    });
                }

                // ✅ VERIFICAR RESPUESTA DE ENVÍO
                var estadoEnvio = envio.Data?.Estado?.ToString()?.ToUpper() ?? "";
                Console.WriteLine($"🔍 Estado procesado para validación: '{estadoEnvio}'");
                
                if (!envio.Ok || estadoEnvio == "DEVUELTA")
                {
                    Console.WriteLine("⚠️ Nota de Débito DEVUELTA o con errores");
                    
                    var mensajesEnvio = new List<object>();
                    
                    dynamic primerComprobante = null;
                    var comprobantes = envio.Data?.Comprobantes;
                    if (comprobantes != null)
                    {
                        var comprobantesList = comprobantes.Comprobante;
                        if (comprobantesList != null && comprobantesList.Count > 0)
                        {
                            primerComprobante = comprobantesList[0];
                        }
                    }
                    
                    if (primerComprobante?.Mensajes?.Mensaje != null)
                    {
                        foreach (var m in primerComprobante.Mensajes.Mensaje)
                        {
                            mensajesEnvio.Add(new 
                            { 
                                Identificador = m.Identificador, 
                                Mensaje_ = m.Mensaje_, 
                                Tipo = m.Tipo, 
                                InformacionAdicional = m.InformacionAdicional 
                            });
                        }
                    }

                    return Ok(new
                    {
                        success = false,
                        estado = estadoEnvio,
                        error = "Nota de Débito rechazada por el SRI",
                        mensajes = mensajesEnvio
                    });
                }

                // ========================================
                // ✅ FACTURA RECIBIDA - CONSULTAR AUTORIZACIÓN
                // ========================================
                Console.WriteLine("✅ Nota de Débito RECIBIDA por el SRI");
                Console.WriteLine("⏳ Esperando 7 segundos antes de consultar autorización...");
                await Task.Delay(7000);

                Console.WriteLine("========================================");
                Console.WriteLine("🔄 CONSULTANDO AUTORIZACIÓN");
                Console.WriteLine("========================================");
                Console.WriteLine($"Clave de acceso: {notaDebito.InfoTributaria.ClaveAcceso}");
                
                dynamic auto = null;
                dynamic autorizacionData = null;
                
                try
                {
                    auto = await _webService.AutorizacionComprobanteAsync(notaDebito.InfoTributaria.ClaveAcceso);
                    
                    if (auto?.Data != null)
                    {
                        var autorizaciones = auto.Data.Autorizaciones;
                        if (autorizaciones != null)
                        {
                            var autorizacionList = autorizaciones.Autorizacion;
                            if (autorizacionList != null && autorizacionList.Count > 0)
                            {
                                autorizacionData = autorizacionList[0];
                            }
                        }
                    }
                }
                catch (Exception exAuto)
                {
                    Console.WriteLine("========================================");
                    Console.WriteLine("❌ EXCEPCIÓN AL CONSULTAR AUTORIZACIÓN");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"Mensaje: {exAuto.Message}");
                    
                    return BadRequest(new
                    {
                        success = false,
                        error = "Error al consultar autorización en el SRI",
                        detalleError = exAuto.Message,
                        claveAcceso = notaDebito.InfoTributaria.ClaveAcceso,
                        mensaje = "La Nota de Débito fue recibida pero no se pudo consultar su autorización. Intente consultarla manualmente con la clave de acceso."
                    });
                }

                var estadoAutorizacion = autorizacionData?.Estado?.ToUpper() ?? "SIN_RESPUESTA";
                Console.WriteLine($"🔍 Estado de autorización procesado: '{estadoAutorizacion}'");

                // ✅ VERIFICAR ESTADO DE AUTORIZACIÓN
                if (estadoAutorizacion != "AUTORIZADO")
                {
                    Console.WriteLine("⚠️ Nota de Débito NO AUTORIZADA");
                    
                    var mensajesAutorizacion = new List<object>();
                    
                    if (autorizacionData?.Mensajes?.Mensaje != null)
                    {
                        foreach (var m in autorizacionData.Mensajes.Mensaje)
                        {
                            mensajesAutorizacion.Add(new 
                            { 
                                Identificador = m.Identificador, 
                                Mensaje_ = m.Mensaje_, 
                                Tipo = m.Tipo, 
                                InformacionAdicional = m.InformacionAdicional 
                            });
                        }
                    }

                    return Ok(new
                    {
                        success = false,
                        estado = estadoAutorizacion,
                        mensajes = mensajesAutorizacion,
                        claveAcceso = notaDebito.InfoTributaria.ClaveAcceso,
                        error = estadoAutorizacion == "SIN_RESPUESTA" 
                            ? "La Nota de Débito aún está en procesamiento. Consulte la autorización más tarde con la clave de acceso."
                            : "Nota de Débito no autorizada por el SRI"
                    });
                }

                // ✅ FACTURA AUTORIZADA - GENERAR PDF
                Console.WriteLine("✅ Nota de Débito AUTORIZADA");
                
                notaDebito.Autorizacion.Numero = autorizacionData.NumeroAutorizacion;
                
                DateTimeOffset fechaOffset;
                if (DateTimeOffset.TryParse(autorizacionData.FechaAutorizacion, out fechaOffset))
                {
                    notaDebito.Autorizacion.Fecha = fechaOffset.ToOffset(TimeSpan.FromHours(-5)).DateTime;
                }
                else
                {
                    throw new Exception($"Fecha de autorización inválida: {autorizacionData.FechaAutorizacion}");
                }
                
                Console.WriteLine($"✅ Número de autorización: {notaDebito.Autorizacion.Numero}");
                Console.WriteLine($"✅ Fecha de autorización: {notaDebito.Autorizacion.Fecha}");

                // Generar PDF
                var nombrePdf = $"NOTADEBITO_{notaDebito.InfoTributaria.ClaveAcceso}.pdf";
                rutaPDF = Path.Combine(tempDir, nombrePdf);
                _rideService.NotaDebito_1_0_0(notaDebito, rutaPDF);

                var pdfBase64 = Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(rutaPDF));
                var xmlBase64 = Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(rutaXmlLocal));

                return Ok(new
                {
                    success = true,
                    claveAcceso = notaDebito.InfoTributaria.ClaveAcceso,
                    mensaje = "Nota de Débito autorizada y PDF generado correctamente",
                    numeroAutorizacion = notaDebito.Autorizacion.Numero,
                    fechaAutorizacion = notaDebito.Autorizacion.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
                    pdfBase64 = pdfBase64,
                    xmlBase64 = xmlBase64
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("========================================");
                Console.WriteLine("❌ EXCEPCIÓN GENERAL");
                Console.WriteLine("========================================");
                Console.WriteLine($"Mensaje: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                
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
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"No se pudo eliminar {Path.GetFileName(ruta)}: {ex.Message}");
                    }
                }
            }
        }
    }
}