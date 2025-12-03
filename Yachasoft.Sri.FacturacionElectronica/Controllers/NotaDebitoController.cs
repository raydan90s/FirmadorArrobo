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

        static NotaDebitoController()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
        }

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

        [HttpGet("Test")]
        public IActionResult Test()
        {
            return Ok(new { mensaje = "NotaDebito Controller funcionando correctamente", timestamp = DateTime.Now });
        }

        [HttpPost("GenerarNotaDebito")]
        public async Task<IActionResult> GenerarNotaDebito([FromBody] NotaDebitoRequest request)
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
                    logoPath = Path.Combine("/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica", logoFileName);

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
                    AgenteRetencion = request.Emisor.AgenteRetencion
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

                var notaDebito = new NotaDebito_1_0_0Modelo.NotaDebito
                {
                    PuntoEmision = puntoEmision,
                    FechaEmision = request.FechaEmision,
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

                var xmlObj = NotaDebito_1_0_0Mapper.Map(notaDebito);

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

                var nombreArchivoXml = $"NOTADEBITO_{notaDebito.InfoTributaria.ClaveAcceso}.xml";
                rutaXmlLocal = Path.Combine("/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica", nombreArchivoXml);
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

                var auto = await _webService.AutorizacionComprobanteAsync(notaDebito.InfoTributaria.ClaveAcceso);
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
                    notaDebito.Autorizacion.Numero = autorizacionData.NumeroAutorizacion;
                    if (DateTimeOffset.TryParse(autorizacionData.FechaAutorizacion, out var fechaOffset))
                    {
                        notaDebito.Autorizacion.Fecha = fechaOffset.ToOffset(TimeSpan.FromHours(-5)).DateTime;
                    }
                    else
                    {
                        throw new Exception($"Fecha de autorización inválida: {autorizacionData.FechaAutorizacion}");
                    }
                }

                var nombrePdf = $"NOTADEBITO_{notaDebito.InfoTributaria.ClaveAcceso}.pdf";
                rutaPDF = Path.Combine("/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica", nombrePdf);
                _rideService.NotaDebito_1_0_0(notaDebito, rutaPDF);

                FrappeUploadResult respuestaUploadPDF;
                FrappeUploadResult respuestaUploadXML;

                if (usandoCredencialesEmisor)
                {
                    respuestaUploadPDF = await _frappeUploader.UploadFileAsync(
                        filePath: rutaPDF,
                        fileName: Path.GetFileName(rutaPDF),
                        apiKey: apiKey,
                        apiSecret: apiSecret,
                        folder: "Home/Nota de Débito/PDF"
                    );

                    respuestaUploadXML = await _frappeUploader.UploadFileAsync(
                        filePath: rutaXmlLocal,
                        fileName: nombreArchivoXml,
                        apiKey: apiKey,
                        apiSecret: apiSecret,
                        folder: "Home/Nota de Débito/XML"
                    );
                }
                else
                {
                    respuestaUploadPDF = await _frappeUploader.UploadFileAsync(
                        filePath: rutaPDF,
                        fileName: Path.GetFileName(rutaPDF),
                        folder: "Home/Nota de Débito/PDF"
                    );

                    respuestaUploadXML = await _frappeUploader.UploadFileAsync(
                        filePath: rutaXmlLocal,
                        fileName: nombreArchivoXml,
                        folder: "Home/Nota de Débito/XML"
                    );
                }

                await LimpiarArchivosTemporales(rutaPDF, rutaXmlLocal, logoPath);

                return Ok(new
                {
                    success = true,
                    claveAcceso = notaDebito.InfoTributaria.ClaveAcceso,
                    mensaje = "Nota de Débito autorizada, PDF generado y archivos subidos a Frappe correctamente",
                    numeroAutorizacion = notaDebito.Autorizacion.Numero,
                    fechaAutorizacion = notaDebito.Autorizacion.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
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