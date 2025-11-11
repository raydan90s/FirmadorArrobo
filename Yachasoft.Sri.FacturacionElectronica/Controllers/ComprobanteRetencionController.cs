using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Threading.Tasks;
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
        private readonly Signer.ICertificadoService certificadoService;
        private readonly WebService.ISriWebService webService;
        private readonly Ride.IRIDEService rIDEService;
        private readonly FrappeFileUploader _frappeUploader;
        private readonly FrappeCertificateService _frappeCertService; // 👈 NUEVA DEPENDENCIA

        public RetencionController(
            Signer.ICertificadoService certificadoService,
            WebService.ISriWebService webService,
            Ride.IRIDEService rIDEService,
            FrappeFileUploader frappeUploader,
            FrappeCertificateService frappeCertService // 👈 INYECTADO DESDE STARTUP



            )
        {
            this.certificadoService = certificadoService;
            this.webService = webService;
            this.rIDEService = rIDEService;
            this._frappeUploader = frappeUploader;
            this._frappeCertService = frappeCertService;
        }

        [HttpPost("GenerarRetencion")]
        
        public async Task<IActionResult> GenerarRetencion([FromBody] RetencionRequest request)
        {
            try
            {
                // 1️⃣ Descargar certificado desde Frappe dinámicamente
                var certificado = await _frappeCertService.DownloadCertificateAsync(request.Emisor.RazonSocial);
                if (!certificado.success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = $"No se pudo descargar el certificado desde Frappe: {certificado.error}"
                    });
                }

                // 2️⃣ Cargar el certificado descargado para firmar el XML
                certificadoService.CargarDesdeP12(certificado.filePath, certificado.password);

                // 3️⃣ Construcción de emisor, establecimiento, punto de emisión y retención
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

                // 4️⃣ Generar y firmar el XML con el certificado descargado
                var comprobanteXml = ComprobanteRetencion_1_0_0Mapper.Map(retencion);
                var xmlFirmado = certificadoService.FirmarDocumento(comprobanteXml);

                var nombreArchivoXml = $"COMPROBANTE_RETENCION_{retencion.InfoTributaria.ClaveAcceso}.xml";
                xmlFirmado.Save(nombreArchivoXml);

                // 5️⃣ Enviar al SRI
                var envio = await webService.ValidarComprobanteAsync(xmlFirmado);
                Console.WriteLine($"ESTADO DE COMPROBANTE DE ENVIO: {System.Text.Json.JsonSerializer.Serialize(envio, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}");

                if (envio.Ok)
                {
                    System.Threading.Thread.Sleep(3000);
                    var auto = await webService.AutorizacionComprobanteAsync(retencion.InfoTributaria.ClaveAcceso);
                    var autorizacionData = auto.Data?.Autorizaciones?.Autorizacion?.FirstOrDefault();
                    Console.WriteLine($"ESTADO DE COMPROBANTE DE AUTORIZACION: {System.Text.Json.JsonSerializer.Serialize(auto, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}");

                    if (auto.Ok)
                    {
                        Console.WriteLine("AUTORIZADO");

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

                        // 6️⃣ Generar PDF y subir a Frappe
                        var rutaPDF = $"/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica/COMPROBANTE_RETENCION_{retencion.InfoTributaria.ClaveAcceso}.pdf";
                        rIDEService.ComprobanteRetencion_1_0_0(retencion, rutaPDF);

                        var respuestaUpload = await _frappeUploader.UploadFileAsync(
                            rutaPDF,
                            Path.GetFileName(rutaPDF),
                            folder: "Home/Documento de Retencion/PDF"
                        );
                        Console.WriteLine("📤 Archivo PDF subido a Frappe:");
                        Console.WriteLine(respuestaUpload);

                        var rutaXML = $"/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica/COMPROBANTE_RETENCION_{retencion.InfoTributaria.ClaveAcceso}.xml";
                        var respuestaXmlUpload = await _frappeUploader.UploadFileAsync(
                            rutaXML,
                            Path.GetFileName(rutaXML),
                            folder: "Home/Documento de Retencion/XML"
                        );
                        Console.WriteLine("📤 Archivo XML subido a Frappe:");
                        Console.WriteLine(respuestaXmlUpload);

                        await FileCleanupHelper.DeleteFileAsync(rutaPDF);
                        await FileCleanupHelper.DeleteFileAsync(rutaXML);

                        return Ok(new
                        {
                            success = true,
                            claveAcceso = retencion.InfoTributaria.ClaveAcceso,
                            mensaje = "Retención autorizada, PDF generado y archivos subidos a Frappe correctamente",
                            numeroAutorizacion = retencion.Autorizacion.Numero,
                            fechaAutorizacion = retencion.Autorizacion.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
                            respuestaFrappePDF = respuestaUpload,
                            respuestaFrappeXML = respuestaXmlUpload
                        });
                    }

                    return Ok(new
                    {
                        success = false,
                        estado = autorizacionData?.Estado,
                        mensajes = autorizacionData?.Mensajes?.Mensaje?.Select(m => new { m.Identificador, m.Mensaje_, m.Tipo, m.InformacionAdicional }).ToList()
                    });
                }
                else
                {
                    var primerComprobante = envio.Data?.Comprobantes?.Comprobante?.FirstOrDefault();
                    var mensajesEnvio = primerComprobante?.Mensajes?.Mensaje
                        ?.Select(m => new { m.Identificador, m.Mensaje_, m.Tipo, m.InformacionAdicional })
                        .ToList();

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
                return BadRequest(new
                {
                    success = false,
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
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
                    Console.WriteLine($"Procesando impuesto con código: {impuesto.CodigoRetencion}");

                    var codigoRetencion = EnumParserHelper.ParseCodigoRetencion(impuesto.CodigoRetencion);
                    Console.WriteLine($"Código parseado: {codigoRetencion.GetType().Name} = {codigoRetencion}");

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

                        Console.WriteLine($"Impuesto Renta creado - Base: {impuestoRenta.BaseImponible}, Código: {impuestoRenta.CodigoRetencion}");
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


                        Console.WriteLine($"Impuesto IVA creado - Base: {impuestoIVA.BaseImponible}, Código: {impuestoIVA.CodigoRetencion}");
                        resultado.Add(impuestoIVA);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al mapear impuesto: {ex.Message}");
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

            Console.WriteLine($"Código de documento a retener: {documentoRequest.CodDocumento} mapeado es {tipoDocumento}");

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