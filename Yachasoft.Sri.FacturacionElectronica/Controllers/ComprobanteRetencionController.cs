using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Threading.Tasks;
using System.Net;
using Yachasoft.Sri.Core.Enumerados;
using Yachasoft.Sri.Core.Atributos;
using Yachasoft.Sri.Modelos;
using Yachasoft.Sri.Modelos.Base;
using Yachasoft.Sri.Modelos.Enumerados;
using Yachasoft.Sri.Xsd;
using Yachasoft.Sri.Xsd.Map;
using Yachasoft.Sri.FacturacionElectronica.Models.Request;
using Yachasoft.Core.Extensions;
using Yachasoft.Sri.FacturacionElectronica.Services;
using System.IO;

namespace Yachasoft.Sri.FacturacionElectronica.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RetencionController : ControllerBase
    {
        private readonly Signer.ICertificadoService _certificadoService;
        private readonly WebService.ISriWebService _webService;
        private readonly Ride.IRIDEService _rideService;
        private readonly IFrappeFileUploader _frappeUploader;
        private readonly FrappeCertificateService _frappeCertService;
        private readonly FrappeLogoService _frappeLogoService;
        private readonly IFrappeCredentialsService _frappeCredentialsService;

        static RetencionController()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
        }

        public RetencionController(
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
            return Ok(new { mensaje = "Retencion Controller funcionando correctamente", timestamp = DateTime.Now });
        }

        [HttpPost("GenerarRetencion")]
        public async Task<IActionResult> GenerarRetencion([FromBody] RetencionRequest request)
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

                var retencion = new ComprobanteRetencion_1_0_0Modelo.ComprobanteRetencion
                {
                    PuntoEmision = puntoEmision,
                    FechaEmision = request.FechaEmision,
                    InfoCompRetencion = new ComprobanteRetencion_1_0_0Modelo.InfoCompRetencion
                    {
                        PeriodoFiscal = request.PeriodoFiscal,
                    },
                    Sujeto = new Sujeto
                    {
                        Identificacion = request.Sujeto.Identificacion,
                        RazonSocial = request.Sujeto.RazonSocial,
                        TipoIdentificador = EnumParserHelper.ParseTipoIdentificacion(request.Sujeto.TipoIdentificador)
                    },
                    Impuestos = MapearImpuestos(request.Impuestos),
                    InfoAdicional = request.InfoAdicional
                };

                retencion.InfoTributaria = new InfoTributaria
                {
                    Secuencial = request.Secuencial,
                    EnumTipoEmision = EnumParserHelper.ParseTipoEmision(request.EnumTipoEmision)
                };

                retencion.InfoTributaria.ClaveAcceso = Utils.GenerarClaveAcceso(
                    retencion.TipoDocumento,
                    retencion.FechaEmision,
                    retencion.PuntoEmision,
                    retencion.InfoTributaria.Secuencial,
                    retencion.InfoTributaria.EnumTipoEmision
                );

                var comprobanteXml = ComprobanteRetencion_1_0_0Mapper.Map(retencion);
                var xmlFirmado = _certificadoService.FirmarDocumento(comprobanteXml);

                var nombreArchivoXml = $"COMPROBANTE_RETENCION_{retencion.InfoTributaria.ClaveAcceso}.xml";
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

                var auto = await _webService.AutorizacionComprobanteAsync(retencion.InfoTributaria.ClaveAcceso);
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
                    retencion.Autorizacion.Numero = autorizacionData.NumeroAutorizacion;
                    if (DateTimeOffset.TryParse(autorizacionData.FechaAutorizacion, out var fechaOffset))
                    {
                        retencion.Autorizacion.Fecha = fechaOffset.ToOffset(TimeSpan.FromHours(-5)).DateTime;
                    }
                    else
                    {
                        throw new Exception($"Fecha de autorización inválida: {autorizacionData.FechaAutorizacion}");
                    }
                }

                var nombrePdf = $"COMPROBANTE_RETENCION_{retencion.InfoTributaria.ClaveAcceso}.pdf";
                rutaPDF = Path.Combine(Path.GetTempPath(), nombrePdf);
                _rideService.ComprobanteRetencion_1_0_0(retencion, rutaPDF);

                FrappeUploadResult respuestaUploadPDF;
                FrappeUploadResult respuestaUploadXML;

                if (usandoCredencialesEmisor)
                {
                    respuestaUploadPDF = await _frappeUploader.UploadFileAsync(
                        filePath: rutaPDF,
                        fileName: Path.GetFileName(rutaPDF),
                        apiKey: apiKey,
                        apiSecret: apiSecret,
                        folder: "Home/Documento de Retencion/PDF"
                    );

                    respuestaUploadXML = await _frappeUploader.UploadFileAsync(
                        filePath: rutaXmlLocal,
                        fileName: nombreArchivoXml,
                        apiKey: apiKey,
                        apiSecret: apiSecret,
                        folder: "Home/Documento de Retencion/XML"
                    );
                }
                else
                {
                    respuestaUploadPDF = await _frappeUploader.UploadFileAsync(
                        filePath: rutaPDF,
                        fileName: Path.GetFileName(rutaPDF),
                        folder: "Home/Documento de Retencion/PDF"
                    );

                    respuestaUploadXML = await _frappeUploader.UploadFileAsync(
                        filePath: rutaXmlLocal,
                        fileName: nombreArchivoXml,
                        folder: "Home/Documento de Retencion/XML"
                    );
                }

                await LimpiarArchivosTemporales(rutaPDF, rutaXmlLocal, logoPath);

                return Ok(new
                {
                    success = true,
                    claveAcceso = retencion.InfoTributaria.ClaveAcceso,
                    mensaje = "Retención autorizada, PDF generado y archivos subidos a Frappe correctamente",
                    numeroAutorizacion = retencion.Autorizacion.Numero,
                    fechaAutorizacion = retencion.Autorizacion.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
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

        #region Métodos de Mapeo y Parseo

        private List<ComprobanteRetencion_1_0_0Modelo.ImpuestoRetencion> MapearImpuestos(List<ImpuestoRetencionRequest> impuestos)
        {
            var resultado = new List<ComprobanteRetencion_1_0_0Modelo.ImpuestoRetencion>();

            foreach (var impuesto in impuestos)
            {
                try
                {
                    var codigoRetencion = EnumParserHelper.ParseCodigoRetencion(impuesto.CodigoRetencion);

                    if (codigoRetencion is EnumTipoRetencionRenta)
                    {
                        var impuestoRenta = new ComprobanteRetencion_1_0_0Modelo.ImpuestoRenta
                        {
                            BaseImponible = impuesto.BaseImponible,
                            Tarifa = impuesto.Tarifa,
                            Valor = Math.Round(impuesto.BaseImponible * impuesto.Tarifa / 100, 2),
                            CodigoRetencion = (EnumTipoRetencionRenta)codigoRetencion,
                            DocumentoSustento = MapearDocumentoSustento(impuesto.DocumentoSustento)
                        };

                        resultado.Add(impuestoRenta);
                    }
                    else if (codigoRetencion is EnumTipoRetencionIVA)
                    {
                        var impuestoIVA = new ComprobanteRetencion_1_0_0Modelo.ImpuestoIVA
                        {
                            BaseImponible = impuesto.BaseImponible,
                            Tarifa = impuesto.Tarifa,
                            Valor = Math.Round(impuesto.BaseImponible * impuesto.Tarifa / 100, 2),
                            CodigoRetencion = (EnumTipoRetencionIVA)codigoRetencion,
                            DocumentoSustento = MapearDocumentoSustento(impuesto.DocumentoSustento)
                        };

                        resultado.Add(impuestoIVA);
                    }
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Error al mapear impuesto con código {impuesto.CodigoRetencion}: {ex.Message}", ex);
                }
            }

            return resultado;
        }

        private DocumentoSustento MapearDocumentoSustento(DocumentoSustentoRequest documentoRequest)
        {
            if (documentoRequest == null)
                throw new ArgumentNullException(nameof(documentoRequest), "El documento sustento no puede ser nulo");

            var tipoDocumento = Enum.GetValues(typeof(EnumTipoDocumento))
                                    .Cast<EnumTipoDocumento>()
                                    .FirstOrDefault(e => e.GetAttributeOfType<SRICodigoAttribute>()?.Code == documentoRequest.CodDocumento.ToString());

            if (!Enum.IsDefined(typeof(EnumTipoDocumento), tipoDocumento))
                throw new ArgumentException($"Código de documento inválido: {documentoRequest.CodDocumento}");

            return new DocumentoSustento
            {
                CodDocumento = tipoDocumento,
                NumDocumento = documentoRequest.NumDocumento,
                FechaEmisionDocumento = documentoRequest.FechaEmisionDocumento
            };
        }

        #endregion
    }
}