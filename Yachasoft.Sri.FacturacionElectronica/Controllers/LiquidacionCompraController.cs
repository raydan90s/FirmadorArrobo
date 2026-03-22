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
using Yachasoft.Sri.Xsd.Contratos.LiquidacionCompra_1_0_0;
using Yachasoft.Sri.Xsd.Map;
using Yachasoft.Sri.FacturacionElectronica.Models.Request;
using Yachasoft.Sri.FacturacionElectronica.Services;

namespace Yachasoft.Sri.FacturacionElectronica.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LiquidacionController : ControllerBase
    {
        private readonly Signer.ICertificadoService _certificadoService;
        private readonly WebService.ISriWebService _webService;
        private readonly Ride.IRIDEService _rideService;
        private readonly IFrappeFileUploader _frappeUploader;
        private readonly FrappeCertificateService _frappeCertService;
        private readonly FrappeLogoService _frappeLogoService;
        private readonly IFrappeCredentialsService _frappeCredentialsService;

        static LiquidacionController()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
        }

        public LiquidacionController(
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

        [HttpGet("Test")]
        public IActionResult Test()
        {
            return Ok(new { mensaje = "Liquidacion Controller funcionando correctamente", timestamp = DateTime.Now });
        }

        [HttpPost("GenerarLiquidacion")]
        public async Task<IActionResult> GenerarLiquidacion([FromBody] LiquidacionRequest request)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

            string logoPath = null;
            string rutaPDF = null;
            string rutaXmlLocal = null;

            try
            {
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
                }

                var verificacion = await _frappeCertService.VerificarCertificadoAsync(
                    request.Emisor.RazonSocial,
                    apiKey,
                    apiSecret
                );

                if (!verificacion.Success)
                {
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

                var certificado = await _frappeCertService.ObtenerCertificadoAsync(
                    request.Emisor.RazonSocial,
                    apiKey,
                    apiSecret
                );

                if (!certificado.Success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = $"No se pudo descargar el certificado: {certificado.Error}",
                        usandoCredencialesEmisor = usandoCredencialesEmisor
                    });
                }

                if (string.IsNullOrEmpty(certificado.Contrasena))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "No se recibió la contraseña del certificado desde Frappe"
                    });
                }

                try
                {
                    _certificadoService.CargarDesdeBase64String(
                        certificado.CertificadoBase64,
                        certificado.Contrasena
                    );
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

                var logoResult = await _frappeLogoService.ObtenerLogoAsync(
                    request.Emisor.RazonSocial,
                    apiKey,
                    apiSecret
                );

                if (logoResult.Success && !string.IsNullOrWhiteSpace(logoResult.LogoBase64))
                {
                    var logoFileName = $"logo_{request.Emisor.RUC}_{DateTime.Now:yyyyMMddHHmmss}.png";
                    logoPath = Path.Combine(Path.GetTempPath(), logoFileName);

                    var logoBytes = Convert.FromBase64String(logoResult.LogoBase64);
                    await System.IO.File.WriteAllBytesAsync(logoPath, logoBytes);
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

                var detallesMapeados = MapperHelper.MapearDetalles(request.Detalles);

                var liquidacion = new LiquidacionCompra_1_0_0Modelo.LiquidacionCompra
                {
                    PuntoEmision = puntoEmision,
                    FechaEmision = request.FechaEmision,
                    Sujeto = new Sujeto
                    {
                        Identificacion = request.Cliente.Identificacion,
                        RazonSocial = request.Cliente.RazonSocial,
                        TipoIdentificador = EnumParserHelper.ParseTipoIdentificacion(request.Cliente.TipoIdentificador)
                    },
                    InfoLiquidacionCompra = new LiquidacionCompra_1_0_0Modelo.InfoLiquidacionCompra
                    {
                        TotalSinImpuestos = request.InfoLiquidacion.TotalSinImpuestos,
                        TotalDescuento = request.InfoLiquidacion.TotalDescuento,
                        ImporteTotal = request.InfoLiquidacion.ImporteTotal,
                        DireccionProveedor = direccionCliente,
                        TotalConImpuestos = MapperHelper.MapearImpuestosVentaDesdeDetalles(detallesMapeados),
                        Pagos = MapperHelper.MapearPagos(request.InfoLiquidacion.Pagos)
                    },
                    Detalles = detallesMapeados,
                    InfoAdicional = request.InfoAdicional
                };

                liquidacion.InfoTributaria = new InfoTributaria
                {
                    Secuencial = request.Secuencial,
                    EnumTipoEmision = EnumParserHelper.ParseTipoEmision(request.EnumTipoEmision)
                };

                liquidacion.InfoTributaria.ClaveAcceso = Utils.GenerarClaveAcceso(
                    liquidacion.TipoDocumento,
                    liquidacion.FechaEmision,
                    liquidacion.PuntoEmision,
                    liquidacion.InfoTributaria.Secuencial,
                    liquidacion.InfoTributaria.EnumTipoEmision
                );

                var xmlObj = LiquidacionCompra_1_0_0Mapper.Map(liquidacion);

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

                var nombreArchivoXml = $"LIQUIDACION_COMPRA_{liquidacion.InfoTributaria.ClaveAcceso}.xml";
                rutaXmlLocal = Path.Combine(Path.GetTempPath(), nombreArchivoXml);
                xmlFirmado.Save(rutaXmlLocal);

                var envio = await _webService.ValidarComprobanteAsync(xmlFirmado);

                if (!envio.Ok)
                {
                    var primerComprobante = envio.Data?.Comprobantes?.Comprobante?.FirstOrDefault();
                    var mensajesEnvio = primerComprobante?.Mensajes?.Mensaje
                        ?.Select(m => new { m.Identificador, m.Mensaje_, m.Tipo, m.InformacionAdicional })
                        .ToList();

                    return Ok(new
                    {
                        success = false,
                        estado = envio.Data?.Estado,
                        error = envio.Error,
                        mensajes = mensajesEnvio
                    });
                }

                await Task.Delay(3000);

                var auto = await _webService.AutorizacionComprobanteAsync(liquidacion.InfoTributaria.ClaveAcceso);
                var autorizacionData = auto.Data?.Autorizaciones?.Autorizacion?.FirstOrDefault();

                if (!auto.Ok)
                {
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

                if (autorizacionData != null)
                {
                    liquidacion.Autorizacion.Numero = autorizacionData.NumeroAutorizacion;
                    if (DateTimeOffset.TryParse(autorizacionData.FechaAutorizacion, out var fechaOffset))
                    {
                        liquidacion.Autorizacion.Fecha = fechaOffset.ToOffset(TimeSpan.FromHours(-5)).DateTime;
                    }
                    else
                    {
                        throw new Exception($"Fecha de autorización inválida: {autorizacionData.FechaAutorizacion}");
                    }
                }

                var nombrePdf = $"LIQUIDACION_COMPRA_{liquidacion.InfoTributaria.ClaveAcceso}.pdf";
                rutaPDF = Path.Combine(Path.GetTempPath(), nombrePdf);
                _rideService.LiquidacionCompra_1_0_0(liquidacion, rutaPDF);

                FrappeUploadResult respuestaUploadPDF;
                FrappeUploadResult respuestaUploadXML;

                if (usandoCredencialesEmisor)
                {
                    respuestaUploadPDF = await _frappeUploader.UploadFileAsync(
                        filePath: rutaPDF,
                        fileName: Path.GetFileName(rutaPDF),
                        apiKey: apiKey,
                        apiSecret: apiSecret,
                        folder: "Home/Liquidacion de Bienes y Servicios/PDF"
                    );

                    respuestaUploadXML = await _frappeUploader.UploadFileAsync(
                        filePath: rutaXmlLocal,
                        fileName: nombreArchivoXml,
                        apiKey: apiKey,
                        apiSecret: apiSecret,
                        folder: "Home/Liquidacion de Bienes y Servicios/XML"
                    );
                }
                else
                {
                    respuestaUploadPDF = await _frappeUploader.UploadFileAsync(
                        filePath: rutaPDF,
                        fileName: Path.GetFileName(rutaPDF),
                        folder: "Home/Liquidacion de Bienes y Servicios/PDF"
                    );

                    respuestaUploadXML = await _frappeUploader.UploadFileAsync(
                        filePath: rutaXmlLocal,
                        fileName: nombreArchivoXml,
                        folder: "Home/Liquidacion de Bienes y Servicios/XML"
                    );
                }

                await LimpiarArchivosTemporales(rutaPDF, rutaXmlLocal, logoPath);

                return Ok(new
                {
                    success = true,
                    claveAcceso = liquidacion.InfoTributaria.ClaveAcceso,
                    mensaje = "Liquidación de compra autorizada, PDF generado y archivos subidos a Frappe correctamente",
                    numeroAutorizacion = liquidacion.Autorizacion.Numero,
                    fechaAutorizacion = liquidacion.Autorizacion.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
                    respuestaFrappePDF = respuestaUploadPDF,
                    respuestaFrappeXML = respuestaUploadXML,
                    credencialesUsadas = usandoCredencialesEmisor ? "Emisor" : "Por defecto"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    error = ex.Message,
                    stackTrace = ex.StackTrace,
                    innerError = ex.InnerException?.Message,
                    innerStackTrace = ex.InnerException?.StackTrace
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