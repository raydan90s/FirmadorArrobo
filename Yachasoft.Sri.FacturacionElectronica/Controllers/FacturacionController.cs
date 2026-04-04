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
using Yachasoft.Sri.FacturacionElectronica.Helper;
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
            string logoPath = null;
            string rutaPDF = null;
            string rutaXmlLocal = null;

            try
            {
                var tempDir = Path.GetTempPath();

                // ✅ NUEVAS RUTAS LOCALES PARA DESARROLLO
                var rutaCertificadoLocal = @"D:\GitHub\GeneradorPDF\Yachasoft.Sri.FacturacionElectronica\signature.p12";
                var rutaLogoLocal = @"D:\GitHub\GeneradorPDF\Yachasoft.Sri.FacturacionElectronica\logo.png";
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

                string direccionCliente = request.InfoAdicional?
                    .FirstOrDefault(ca => ca.Nombre.Equals("Direccion", StringComparison.OrdinalIgnoreCase) ||
                                        ca.Nombre.Equals("Dirección", StringComparison.OrdinalIgnoreCase))
                    ?.Valor;

                var detallesMapeados = MapperHelper.MapearDetallesConSubsidio(request.Detalles);

                DateTime fechaEmision;
                if (DateTimeOffset.TryParse(request.FechaEmision, out var dto))
                {
                    // Usar la fecha/hora local del emisor (respeta el offset -05:00)
                    fechaEmision = dto.DateTime;
                }
                else if (!DateTime.TryParse(request.FechaEmision, out fechaEmision))
                {
                    fechaEmision = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                        DateTime.UtcNow,
                        "SA Pacific Standard Time"
                    );
                }

                var factura = new Factura_1_0_0Modelo.Factura
                {
                    PuntoEmision = puntoEmision,
                    FechaEmision = fechaEmision,
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

                Console.WriteLine("========================================");
                Console.WriteLine("🔑 DEBUG DE CLAVE DE ACCESO");
                Console.WriteLine("========================================");
                Console.WriteLine($"Fecha usada: {factura.FechaEmision:dd-MM-yyyy HH:mm:ss.fff}");
                Console.WriteLine($"Secuencial: {factura.InfoTributaria.Secuencial}");
                Console.WriteLine($"Clave generada: {factura.InfoTributaria.ClaveAcceso}");
                Console.WriteLine("========================================");

                var xmlObj = Factura_1_0_0Mapper.Map(factura);

                var xmlDoc = new XmlDocument();
                using (var memoryStream = new MemoryStream())
                {
                    var serializer = new XmlSerializer(xmlObj.GetType());
                    serializer.Serialize(memoryStream, xmlObj);
                    memoryStream.Position = 0;
                    xmlDoc.Load(memoryStream);
                }

                // ---------------------------------------------------------
                // 1. CORRECCIÓN DE FECHA (Código que ya tenías)
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
                
                // Definición de la función local (dentro del método)
                void CorregirDecimales(XmlDocument doc, string xpath, string formato)
                {
                    var nodos = doc.SelectNodes(xpath);
                    if (nodos == null) return;
                    foreach (XmlNode nodo in nodos)
                    {
                        // Usamos CultureInfo.InvariantCulture para asegurar que el punto sea el separador
                        if (decimal.TryParse(nodo.InnerText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal valor))
                        {
                            nodo.InnerText = valor.ToString(formato, System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                }

                // Aplicar corrección a campos de Totales (2 decimales fijos)
                CorregirDecimales(xmlDoc, "//totalSinImpuestos", "F2");
                CorregirDecimales(xmlDoc, "//totalDescuento", "F2");
                CorregirDecimales(xmlDoc, "//baseImponible", "F2");
                CorregirDecimales(xmlDoc, "//valor", "F2"); // Impuestos
                CorregirDecimales(xmlDoc, "//importeTotal", "F2");
                CorregirDecimales(xmlDoc, "//propina", "F2");
                CorregirDecimales(xmlDoc, "//total", "F2"); // Total pagos

                // Aplicar corrección a Detalles (Precios unitarios y totales)
                CorregirDecimales(xmlDoc, "//precioUnitario", "F2");
                CorregirDecimales(xmlDoc, "//precioTotalSinImpuesto", "F2");
                CorregirDecimales(xmlDoc, "//descuento", "F2");
                CorregirDecimales(xmlDoc, "//cantidad", "F2");

                xmlDoc.DocumentElement.SetAttribute("id", "comprobante");
                var xmlFirmado = _certificadoService.FirmarDocumento(xmlDoc);

                var nombreArchivoXml = $"FACTURA_{factura.InfoTributaria.ClaveAcceso}.xml";
                rutaXmlLocal = Path.Combine(tempDir, nombreArchivoXml);
                xmlFirmado.Save(rutaXmlLocal);

                // 👇👇 AGREGA ESTO AQUÍ 👇👇
                Console.WriteLine("========================================");
                Console.WriteLine("📄 XML GENERADO Y FIRMADO (PREVIO AL ENVÍO)");
                Console.WriteLine("========================================");
                
                // Opción A: Imprimir en una sola línea (rápido)
                // Console.WriteLine(xmlFirmado.OuterXml);

                // Opción B: Imprimir con formato bonito (Recomendado para leer)
                using (var stringWriter = new StringWriter())
                using (var xmlTextWriter = new XmlTextWriter(stringWriter))
                {
                    xmlTextWriter.Formatting = Formatting.Indented;
                    xmlFirmado.WriteTo(xmlTextWriter);
                    Console.WriteLine(stringWriter.ToString());
                }
                Console.WriteLine("========================================");
                // 👆👆 TERMINA EL BLOQUE AGREGADO 👆👆

                // ========================================
                // ✅ ENVÍO AL SRI CON LOGGING COMPLETO
                // ========================================
                Console.WriteLine("========================================");
                Console.WriteLine("📤 ENVIANDO FACTURA AL SRI");
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
                        Console.WriteLine($"✓ envio.Data.GetType(): {envio.Data.GetType().FullName}");
                        
                        // Intentar serializar a JSON
                        try
                        {
                            var jsonEnvio = Newtonsoft.Json.JsonConvert.SerializeObject(envio, Newtonsoft.Json.Formatting.Indented);
                            Console.WriteLine("--- INICIO JSON COMPLETO DE ENVÍO ---");
                            Console.WriteLine(jsonEnvio);
                            Console.WriteLine("--- FIN JSON COMPLETO DE ENVÍO ---");
                        }
                        catch (Exception exJson)
                        {
                            Console.WriteLine($"⚠️ No se pudo serializar a JSON: {exJson.Message}");
                        }
                        
                        // Inspeccionar propiedades manualmente
                        Console.WriteLine("--- PROPIEDADES DE envio.Data ---");
                        try
                        {
                            var estado = envio.Data.Estado;
                            Console.WriteLine($"✓ Estado: '{estado}'");
                            Console.WriteLine($"✓ Estado (tipo): {estado?.GetType().FullName}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"✗ Error al leer Estado: {ex.Message}");
                        }
                        
                        try
                        {
                            var comprobantes = envio.Data.Comprobantes;
                            Console.WriteLine($"✓ Comprobantes: {comprobantes}");
                            Console.WriteLine($"✓ Comprobantes != null: {comprobantes != null}");
                            
                            if (comprobantes != null)
                            {
                                Console.WriteLine($"✓ Comprobantes (tipo): {comprobantes.GetType().FullName}");
                                
                                var comprobantesList = comprobantes.Comprobante;
                                Console.WriteLine($"✓ Comprobante (lista): {comprobantesList}");
                                Console.WriteLine($"✓ Comprobante != null: {comprobantesList != null}");
                                
                                if (comprobantesList != null)
                                {
                                    Console.WriteLine($"✓ Comprobante (tipo): {comprobantesList.GetType().FullName}");
                                    Console.WriteLine($"✓ Cantidad de comprobantes: {comprobantesList.Count}");
                                    
                                    for (int i = 0; i < comprobantesList.Count; i++)
                                    {
                                        var comp = comprobantesList[i];
                                        Console.WriteLine($"--- COMPROBANTE [{i}] ---");
                                        Console.WriteLine($"  Tipo: {comp.GetType().FullName}");
                                        
                                        try { Console.WriteLine($"  ClaveAcceso: {comp.ClaveAcceso}"); } catch { }
                                        
                                        try
                                        {
                                            var mensajes = comp.Mensajes;
                                            Console.WriteLine($"  Mensajes: {mensajes}");
                                            Console.WriteLine($"  Mensajes != null: {mensajes != null}");
                                            
                                            if (mensajes != null)
                                            {
                                                var mensajeList = mensajes.Mensaje;
                                                Console.WriteLine($"  Mensaje (lista): {mensajeList}");
                                                Console.WriteLine($"  Cantidad de mensajes: {mensajeList?.Count}");
                                                
                                                if (mensajeList != null)
                                                {
                                                    foreach (var msg in mensajeList)
                                                    {
                                                        Console.WriteLine($"    ► Mensaje:");
                                                        Console.WriteLine($"      - Identificador: {msg.Identificador}");
                                                        Console.WriteLine($"      - Tipo: {msg.Tipo}");
                                                        Console.WriteLine($"      - Mensaje_: {msg.Mensaje_}");
                                                        Console.WriteLine($"      - InformacionAdicional: {msg.InformacionAdicional}");
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception exMsg)
                                        {
                                            Console.WriteLine($"  ✗ Error al leer Mensajes: {exMsg.Message}");
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception exComp)
                        {
                            Console.WriteLine($"✗ Error al leer Comprobantes: {exComp.Message}");
                            Console.WriteLine($"   StackTrace: {exComp.StackTrace}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("⚠️ envio.Data ES NULL");
                    }
                    
                    Console.WriteLine("========================================");
                }
                catch (Exception exEnvio)
                {
                    Console.WriteLine("========================================");
                    Console.WriteLine("❌ EXCEPCIÓN AL ENVIAR AL SRI");
                    Console.WriteLine("========================================");
                    Console.WriteLine($"Mensaje: {exEnvio.Message}");
                    Console.WriteLine($"Tipo: {exEnvio.GetType().FullName}");
                    Console.WriteLine($"StackTrace: {exEnvio.StackTrace}");
                    
                    if (exEnvio.InnerException != null)
                    {
                        Console.WriteLine($"InnerException.Mensaje: {exEnvio.InnerException.Message}");
                        Console.WriteLine($"InnerException.Tipo: {exEnvio.InnerException.GetType().FullName}");
                        Console.WriteLine($"InnerException.StackTrace: {exEnvio.InnerException.StackTrace}");
                    }
                    
                    Console.WriteLine("========================================");
                    
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
                Console.WriteLine($"🔍 envio.Ok: {envio.Ok}");
                
                if (!envio.Ok || estadoEnvio == "DEVUELTA")
                {
                    Console.WriteLine("⚠️ Factura DEVUELTA o con errores");
                    
                    // Extraer mensajes de error
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
                        error = "Factura rechazada por el SRI",
                        mensajes = mensajesEnvio
                    });
                }

                // Consultar autorización con reintentos
                var auto = await SriAutorizacionHelper.ConsultarAutorizacionConReintentosAsync(
                    _webService, factura.InfoTributaria.ClaveAcceso);
                var autorizacionData = auto.Data?.Autorizaciones?.Autorizacion?.FirstOrDefault();

                if (!auto.Ok)
                {
                    var mensajesAutorizacion = autorizacionData?.Mensajes?.Mensaje
                        ?.Select(m => new { m.Identificador, m.Mensaje_, m.Tipo, m.InformacionAdicional })
                        .ToList();

                    return Ok(new
                    {
                        success = false,
                        estado = autorizacionData?.Estado ?? "SIN_RESPUESTA",
                        mensajes = mensajesAutorizacion,
                        claveAcceso = factura.InfoTributaria.ClaveAcceso,
                        error = autorizacionData?.Estado == null
                            ? "La factura aún está en procesamiento. Consulte la autorización más tarde con la clave de acceso."
                            : "Factura no autorizada por el SRI"
                    });
                }

                // ✅ FACTURA AUTORIZADA - GENERAR PDF
                Console.WriteLine("✅ Factura AUTORIZADA");
                
                factura.Autorizacion.Numero = autorizacionData.NumeroAutorizacion;
                
                DateTimeOffset fechaOffset;
                if (DateTimeOffset.TryParse(autorizacionData.FechaAutorizacion, out fechaOffset))
                {
                    factura.Autorizacion.Fecha = fechaOffset.ToOffset(TimeSpan.FromHours(-5)).DateTime;
                }
                else
                {
                    throw new Exception($"Fecha de autorización inválida: {autorizacionData.FechaAutorizacion}");
                }
                
                Console.WriteLine($"✅ Número de autorización: {factura.Autorizacion.Numero}");
                Console.WriteLine($"✅ Fecha de autorización: {factura.Autorizacion.Fecha}");

                // Generar PDF
                var nombrePdf = $"FACTURA_{factura.InfoTributaria.ClaveAcceso}.pdf";
                rutaPDF = Path.Combine(tempDir, nombrePdf);
                _rideService.Factura_1_0_0(factura, rutaPDF);

                var pdfBase64 = Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(rutaPDF));
                var xmlBase64 = Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(rutaXmlLocal));

                return Ok(new
                {
                    success = true,
                    claveAcceso = factura.InfoTributaria.ClaveAcceso,
                    mensaje = "Factura autorizada y PDF generado correctamente",
                    numeroAutorizacion = factura.Autorizacion.Numero,
                    fechaAutorizacion = factura.Autorizacion.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
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
                Console.WriteLine($"Tipo: {ex.GetType().FullName}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"InnerException.Mensaje: {ex.InnerException.Message}");
                    Console.WriteLine($"InnerException.StackTrace: {ex.InnerException.StackTrace}");
                }
                
                Console.WriteLine("========================================");
                
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