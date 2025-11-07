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
        private readonly Signer.ICertificadoService certificadoService;
        private readonly WebService.ISriWebService webService;
        private readonly Ride.IRIDEService rIDEService;
        private readonly FrappeFileUploader _frappeUploader;

        public NotaDebitoController(
            Signer.ICertificadoService certificadoService,
            WebService.ISriWebService webService,
            Ride.IRIDEService rIDEService,
            FrappeFileUploader frappeUploader)
        {
            this.certificadoService = certificadoService;
            this.webService = webService;
            this.rIDEService = rIDEService;
            this._frappeUploader = frappeUploader;
        }

        [HttpPost("GenerarNotaDebito")]
        public async Task<IActionResult> GenerarNotaDebito([FromBody] NotaDebitoRequest request)
        {
            try
            {
                Console.WriteLine($"Fecha de emisión documento modificado: {request.DocumentoModificado.FechaEmisionDocumento}");
                Console.WriteLine($"Fecha de emisión nota de débito: {request.FechaEmision}");

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
                    var serializer = new XmlSerializer(typeof(notaDebito));
                    serializer.Serialize(memoryStream, xmlObj);
                    memoryStream.Position = 0;
                    xmlDoc.Load(memoryStream);
                }

                xmlDoc.DocumentElement.SetAttribute("id", "comprobante");

                certificadoService.CargarDesdeP12(
                    "/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica/signature.p12",
                    "Compus1234"
                );
                var xmlFirmado = certificadoService.FirmarDocumento(xmlDoc);

                var nombreArchivoXml = $"NOTADEBITO_{notaDebito.InfoTributaria.ClaveAcceso}.xml";
                xmlFirmado.Save(nombreArchivoXml);

                var envio = await webService.ValidarComprobanteAsync(xmlFirmado);
                Console.WriteLine($"ESTADO DE COMPROBANTE DE ENVIO: {System.Text.Json.JsonSerializer.Serialize(envio, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}");

                if (envio.Ok)
                {
                    System.Threading.Thread.Sleep(3000);

                    var auto = await webService.AutorizacionComprobanteAsync(notaDebito.InfoTributaria.ClaveAcceso);
                    var autorizacionData = auto.Data?.Autorizaciones?.Autorizacion?.FirstOrDefault();
                    Console.WriteLine($"ESTADO DE COMPROBANTE DE AUTORIZACION: {System.Text.Json.JsonSerializer.Serialize(auto, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}");

                    Console.WriteLine("📌 Datos completos de la autorización recibida:");
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(autorizacionData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                    if (auto.Ok)
                    {
                        Console.WriteLine("AUTORIZADO");

                        if (autorizacionData != null)
                        {
                            Console.WriteLine("📌 Fecha de autorización recibida del SRI:");
                            Console.WriteLine(autorizacionData.FechaAutorizacion);

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

                        var rutaPDF = $"/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica/NOTADEBITO_{notaDebito.InfoTributaria.ClaveAcceso}.pdf";
                        rIDEService.NotaDebito_1_0_0(notaDebito, rutaPDF);

                        var respuestaUploadPDF = await _frappeUploader.UploadFileAsync(
                            rutaPDF,
                            Path.GetFileName(rutaPDF),
                            folder: "Home/Nota de Débito/PDF"
                        );
                        Console.WriteLine("📤 Archivo PDF subido a Frappe:");
                        Console.WriteLine(respuestaUploadPDF);

                        var rutaXML = $"/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica/NOTADEBITO_{notaDebito.InfoTributaria.ClaveAcceso}.xml";
                        var respuestaUploadXML = await _frappeUploader.UploadFileAsync(
                            rutaXML,
                            Path.GetFileName(rutaXML),
                            folder: "Home/Nota de Débito/XML"
                        );
                        Console.WriteLine("📤 Archivo XML subido a Frappe:");
                        Console.WriteLine(respuestaUploadXML);

                        await FileCleanupHelper.DeleteFileAsync(rutaPDF);
                        await FileCleanupHelper.DeleteFileAsync(rutaXML);

                        var resultado = new
                        {
                            success = true,
                            claveAcceso = notaDebito.InfoTributaria.ClaveAcceso,
                            mensaje = "Nota de Débito autorizada, PDF generado y archivos subidos a Frappe correctamente",
                            numeroAutorizacion = notaDebito.Autorizacion.Numero,
                            fechaAutorizacion = notaDebito.Autorizacion.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
                            respuestaFrappePDF = respuestaUploadPDF,
                            respuestaFrappeXML = respuestaUploadXML
                        };

                        return Ok(resultado);
                    }
                    else
                    {
                        var mensajesAutorizacion = autorizacionData?.Mensajes?.Mensaje?
                            .Select(m => new { m.Identificador, m.Mensaje_, m.Tipo, m.InformacionAdicional })
                            .ToList();

                        if (mensajesAutorizacion != null && mensajesAutorizacion.Count > 0)
                        {
                            Console.WriteLine("MENSAJES DE AUTORIZACIÓN:");
                            foreach (var msg in mensajesAutorizacion)
                            {
                                Console.WriteLine($"- Identificador: {msg.Identificador}");
                                Console.WriteLine($"  Mensaje: {msg.Mensaje_}");
                                Console.WriteLine($"  Tipo: {msg.Tipo}");
                                Console.WriteLine($"  Información Adicional: {msg.InformacionAdicional}");
                                Console.WriteLine("-------------------------------");
                            }
                        }
                        else
                        {
                            Console.WriteLine("No hay mensajes de autorización disponibles.");
                        }

                        return Ok(new
                        {
                            success = false,
                            estado = autorizacionData?.Estado,
                            mensajes = mensajesAutorizacion
                        });
                    }
                }
                else
                {
                    var primerComprobante = envio.Data?.Comprobantes?.Comprobante?.FirstOrDefault();
                    var mensajesEnvio = primerComprobante?.Mensajes?.Mensaje
                        ?.Select(m => new { m.Identificador, m.Mensaje_, m.Tipo, m.InformacionAdicional })
                        .ToList();

                    if (mensajesEnvio != null && mensajesEnvio.Count > 0)
                    {
                        Console.WriteLine("MENSAJES DE ENVÍO:");
                        foreach (var msg in mensajesEnvio)
                        {
                            Console.WriteLine($"- Identificador: {msg.Identificador}");
                            Console.WriteLine($"  Mensaje: {msg.Mensaje_}");
                            Console.WriteLine($"  Tipo: {msg.Tipo}");
                            Console.WriteLine($"  Información Adicional: {msg.InformacionAdicional}");
                            Console.WriteLine("-------------------------------");
                        }
                    }
                    else
                    {
                        Console.WriteLine("No hay mensajes de envío disponibles.");
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
                Console.WriteLine($"❌ ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);

                return BadRequest(new
                {
                    success = false,
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }
    }
}