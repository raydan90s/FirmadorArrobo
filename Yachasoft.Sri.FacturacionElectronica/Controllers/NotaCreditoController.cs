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
using Yachasoft.Sri.Xsd.Contratos.NotaCredito_1_0_0;
using Yachasoft.Sri.Xsd.Map;
using Yachasoft.Sri.FacturacionElectronica.Models.Request;
using Yachasoft.Sri.FacturacionElectronica.Helper;
using Yachasoft.Sri.FacturacionElectronica.Services;

namespace Yachasoft.Sri.FacturacionElectronica.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotaCreditoController : ControllerBase
    {
        private readonly Signer.ICertificadoService _certificadoService;
        private readonly WebService.ISriWebService _webService;
        private readonly Ride.IRIDEService _rideService;
        private readonly IFrappeFileUploader _frappeUploader;
        private readonly FrappeCertificateService _frappeCertService;
        private readonly FrappeLogoService _frappeLogoService;
        private readonly IFrappeCredentialsService _frappeCredentialsService;
        public NotaCreditoController(
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

        [HttpPost("GenerarNotaCredito")]
        public async Task<IActionResult> GenerarNotaCredito([FromBody] NotaCreditoRequest request)
        {
            string logoPath = null;
            string rutaPDF = null;
            string rutaXmlLocal = null;

            try
            {
                var tempDir = Path.GetTempPath();

                var rutaCertificadoLocal = Path.Combine(Directory.GetCurrentDirectory(), "CertificadosDev", "signature.p12");
                var rutaLogoLocal = Path.Combine(Directory.GetCurrentDirectory(), "CertificadosDev", "logo.png");
                var contrasenaP12 = "Compus1234";

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

                var documentoModificado = new DocumentoSustento
                {
                    CodDocumento = EnumParserHelper.ParseTipoDocumento(request.DocumentoModificado.CodDocumento),
                    NumDocumento = request.DocumentoModificado.NumDocumento,
                    FechaEmisionDocumento = request.DocumentoModificado.FechaEmisionDocumento
                };

                var impuestosMapeados = MapperHelper.MapearImpuestosVenta(request.InfoNotaCredito.TotalConImpuestos);

                var detalles = request.Detalles.Select(d => new DetalleDocumentoItemPrecio
                {
                    Item = new Item
                    {
                        CodigoPrincipal = d.Item.CodigoPrincipal,
                        CodigoAuxiliar = d.Item.CodigoAuxiliar,
                        Descripcion = d.Item.Descripcion
                    },
                    Cantidad = d.Cantidad,
                    PrecioUnitario = d.PrecioUnitario,
                    Descuento = d.Descuento,
                    PrecioTotalSinImpuesto = d.PrecioTotalSinImpuesto,
                    Impuestos = MapperHelper.MapearImpuestosDetalle(d.Impuestos?.Select(i => new ImpuestoCreditoRequest
                    {
                        BaseImponible = i.BaseImponible,
                        Valor = i.Valor,
                        Tarifa = i.Tarifa,
                        Codigo = i.Codigo,
                        CodigoPorcentaje = i.CodigoPorcentaje
                    }).ToList()),
                    DetallesAdicionales = d.DetallesAdicionales
                }).ToList();

                DateTime fechaEmision;
                if (DateTimeOffset.TryParse(request.FechaEmision, out var dto))
                {
                    fechaEmision = dto.DateTime;
                }
                else if (!DateTime.TryParse(request.FechaEmision, out fechaEmision))
                {
                    fechaEmision = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                        DateTime.UtcNow,
                        "SA Pacific Standard Time"
                    );
                }

                var notaCredito = new NotaCredito_1_0_0Modelo.NotaCredito
                {
                    PuntoEmision = puntoEmision,
                    FechaEmision = fechaEmision,
                    Sujeto = new Sujeto
                    {
                        Identificacion = request.Cliente.Identificacion,
                        RazonSocial = request.Cliente.RazonSocial,
                        TipoIdentificador = EnumParserHelper.ParseTipoIdentificacion(request.Cliente.TipoIdentificador)
                    },
                    InfoNotaCredito = new NotaCredito_1_0_0Modelo.InfoNotaCredito
                    {
                        DocumentoModificado = documentoModificado,
                        TotalSinImpuestos = request.InfoNotaCredito.TotalSinImpuestos,
                        ValorModificacion = request.InfoNotaCredito.ValorModificacion,
                        Moneda = request.InfoNotaCredito.Moneda,
                        TotalConImpuestos = impuestosMapeados,
                        Motivo = request.Motivo
                    },
                    Detalles = detalles,
                    InfoAdicional = request.InfoAdicional
                };

                notaCredito.InfoTributaria = new InfoTributaria
                {
                    Secuencial = request.Secuencial,
                    EnumTipoEmision = EnumParserHelper.ParseTipoEmision(request.EnumTipoEmision)
                };

                notaCredito.InfoTributaria.ClaveAcceso = Utils.GenerarClaveAcceso(
                    NotaCredito_1_0_0Modelo.TipoDocumento,
                    notaCredito.FechaEmision,
                    notaCredito.PuntoEmision,
                    notaCredito.InfoTributaria.Secuencial,
                    notaCredito.InfoTributaria.EnumTipoEmision
                );

                Console.WriteLine("========================================");
                Console.WriteLine("🔑 DEBUG DE CLAVE DE ACCESO");
                Console.WriteLine("========================================");
                Console.WriteLine($"Fecha usada: {notaCredito.FechaEmision:dd-MM-yyyy HH:mm:ss.fff}");
                Console.WriteLine($"Secuencial: {notaCredito.InfoTributaria.Secuencial}");
                Console.WriteLine($"Clave generada: {notaCredito.InfoTributaria.ClaveAcceso}");
                Console.WriteLine("========================================");

                var xmlObj = NotaCredito_1_0_0Mapper.Map(notaCredito);

                var xmlDoc = new XmlDocument();
                using (var memoryStream = new MemoryStream())
                {
                    var serializer = new XmlSerializer(xmlObj.GetType());
                    serializer.Serialize(memoryStream, xmlObj);
                    memoryStream.Position = 0;
                    xmlDoc.Load(memoryStream);
                }

                var fechaNode = xmlDoc.SelectSingleNode("//fechaEmision");
                if (fechaNode != null)
                {
                    var formato3 = $"{fechaEmision.Day:D2}/{fechaEmision.Month:D2}/{fechaEmision.Year}";
                    fechaNode.InnerText = formato3;
                }

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
                CorregirDecimales(xmlDoc, "//valorModificacion", "F2");
                CorregirDecimales(xmlDoc, "//baseImponible", "F2");
                CorregirDecimales(xmlDoc, "//valor", "F2");

                CorregirDecimales(xmlDoc, "//precioUnitario", "F2");
                CorregirDecimales(xmlDoc, "//precioTotalSinImpuesto", "F2");
                CorregirDecimales(xmlDoc, "//descuento", "F2");
                CorregirDecimales(xmlDoc, "//cantidad", "F2");

                xmlDoc.DocumentElement.SetAttribute("id", "comprobante");
                var xmlFirmado = _certificadoService.FirmarDocumento(xmlDoc);

                var nombreArchivoXml = $"NOTACREDITO_{notaCredito.InfoTributaria.ClaveAcceso}.xml";
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

                Console.WriteLine("========================================");
                Console.WriteLine("📤 ENVIANDO NOTA DE CRÉDITO AL SRI");
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

                var estadoEnvio = envio.Data?.Estado?.ToString()?.ToUpper() ?? "";
                Console.WriteLine($"🔍 Estado procesado para validación: '{estadoEnvio}'");
                Console.WriteLine($"🔍 envio.Ok: {envio.Ok}");
                
                if (!envio.Ok || estadoEnvio == "DEVUELTA")
                {
                    Console.WriteLine("⚠️ Nota de Crédito DEVUELTA o con errores");
                    
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
                        error = "Nota de Crédito rechazada por el SRI",
                        mensajes = mensajesEnvio
                    });
                }

                var auto = await SriAutorizacionHelper.ConsultarAutorizacionConReintentosAsync(
                    _webService, notaCredito.InfoTributaria.ClaveAcceso);
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
                        claveAcceso = notaCredito.InfoTributaria.ClaveAcceso,
                        error = autorizacionData?.Estado == null
                            ? "La Nota de Crédito aún está en procesamiento. Consulte la autorización más tarde con la clave de acceso."
                            : "Nota de Crédito no autorizada por el SRI"
                    });
                }

                Console.WriteLine("✅ Nota de Crédito AUTORIZADA");
                
                notaCredito.Autorizacion.Numero = autorizacionData.NumeroAutorizacion;
                
                DateTimeOffset fechaOffset;
                if (DateTimeOffset.TryParse(autorizacionData.FechaAutorizacion, out fechaOffset))
                {
                    notaCredito.Autorizacion.Fecha = fechaOffset.ToOffset(TimeSpan.FromHours(-5)).DateTime;
                }
                else
                {
                    throw new Exception($"Fecha de autorización inválida: {autorizacionData.FechaAutorizacion}");
                }
                
                Console.WriteLine($"✅ Número de autorización: {notaCredito.Autorizacion.Numero}");
                Console.WriteLine($"✅ Fecha de autorización: {notaCredito.Autorizacion.Fecha}");

                var nombrePdf = $"NOTACREDITO_{notaCredito.InfoTributaria.ClaveAcceso}.pdf";
                rutaPDF = Path.Combine(tempDir, nombrePdf);
                _rideService.NotaCredito_1_0_0(notaCredito, rutaPDF);

                var pdfBase64 = Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(rutaPDF));
                var xmlBase64 = Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(rutaXmlLocal));

                return Ok(new
                {
                    success = true,
                    claveAcceso = notaCredito.InfoTributaria.ClaveAcceso,
                    mensaje = "Nota de Crédito autorizada y PDF generado correctamente",
                    numeroAutorizacion = notaCredito.Autorizacion.Numero,
                    fechaAutorizacion = notaCredito.Autorizacion.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
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