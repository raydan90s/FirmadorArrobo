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
using System.Text.Json;


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
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine($"📝 INICIANDO GENERACIÓN DE RETENCIÓN");
                Console.WriteLine($"👤 Emisor: {request.Emisor.RazonSocial}");
                Console.WriteLine($"📅 Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                 
                // 1️⃣ OPCIONAL: Verificar que el certificado existe y es válido
                Console.WriteLine($"\n🔍 PASO 1: Verificando certificado en Frappe...");
                var verificacion = await _frappeCertService.VerificarCertificadoAsync(request.Emisor.RazonSocial);

                if (!verificacion.Success || !verificacion.Vigente)
                {
                    Console.WriteLine($"❌ ERROR: Certificado no vigente");
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
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    certificado.Success,
                    certificado.Emisor,
                    certificado.NombreArchivo,
                    Base64Length = certificado.CertificadoBase64?.Length ?? 0,
                    HasPassword = !string.IsNullOrEmpty(certificado.Contrasena)
                }, new JsonSerializerOptions { WriteIndented = true }));

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

                // 3️⃣ Cargar el certificado desde Base64 (NO desde archivo)
                Console.WriteLine($"\n🔏 PASO 3: Cargando certificado en el servicio de firma...");
                certificadoService.CargarDesdeBase64String(
                    certificado.CertificadoBase64,
                    certificado.Contrasena
                );
                Console.WriteLine($"✅ Certificado cargado correctamente en memoria");

                // 4️⃣ Construcción de emisor, establecimiento, punto de emisión y retención
                Console.WriteLine($"\n📋 PASO 4: Construyendo estructura del comprobante...");

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

                Console.WriteLine($"✅ Comprobante construido");
                Console.WriteLine($"🔑 Clave de acceso: {retencion.InfoTributaria.ClaveAcceso}");
                Console.WriteLine($"📄 Secuencial: {retencion.InfoTributaria.Secuencial}");

                // 5️⃣ Generar y firmar el XML
                Console.WriteLine($"\n✍️ PASO 5: Generando y firmando XML...");
                var comprobanteXml = ComprobanteRetencion_1_0_0Mapper.Map(retencion);
                var xmlFirmado = certificadoService.FirmarDocumento(comprobanteXml);

                var nombreArchivoXml = $"COMPROBANTE_RETENCION_{retencion.InfoTributaria.ClaveAcceso}.xml";
                var rutaXmlLocal = Path.Combine("/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica", nombreArchivoXml);
                xmlFirmado.Save(rutaXmlLocal);
                Console.WriteLine($"✅ XML firmado guardado: {nombreArchivoXml}");

                // 6️⃣ Enviar al SRI
                Console.WriteLine($"\n📤 PASO 6: Enviando comprobante al SRI...");
                var envio = await webService.ValidarComprobanteAsync(xmlFirmado);

                Console.WriteLine($"📊 Respuesta del SRI:");
                Console.WriteLine(JsonSerializer.Serialize(envio,
                    new JsonSerializerOptions { WriteIndented = true }));

                if (envio.Ok)
                {
                    Console.WriteLine($"✅ Comprobante recibido por el SRI");
                    Console.WriteLine($"⏳ Esperando 3 segundos antes de solicitar autorización...");
                    await Task.Delay(3000); // ✅ Mejor usar Task.Delay que Thread.Sleep

                    Console.WriteLine($"\n🔍 PASO 7: Consultando autorización...");
                    var auto = await webService.AutorizacionComprobanteAsync(retencion.InfoTributaria.ClaveAcceso);
                    var autorizacionData = auto.Data?.Autorizaciones?.Autorizacion?.FirstOrDefault();

                    Console.WriteLine($"📊 Respuesta de autorización:");
                    Console.WriteLine(JsonSerializer.Serialize(auto,
                        new JsonSerializerOptions { WriteIndented = true }));

                    if (auto.Ok)
                    {
                        Console.WriteLine($"\n🎉 ¡COMPROBANTE AUTORIZADO!");

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

                            Console.WriteLine($"📋 Número de autorización: {retencion.Autorizacion.Numero}");
                            Console.WriteLine($"📅 Fecha de autorización: {retencion.Autorizacion.Fecha:yyyy-MM-dd HH:mm:ss}");
                        }

                        // 8️⃣ Generar PDF y subir a Frappe
                        Console.WriteLine($"\n📄 PASO 8: Generando RIDE (PDF)...");
                        var nombrePdf = $"COMPROBANTE_RETENCION_{retencion.InfoTributaria.ClaveAcceso}.pdf";
                        var rutaPDF = Path.Combine("/home/bitnami/GeneradorPDF/Yachasoft.Sri.FacturacionElectronica", nombrePdf);
                        rIDEService.ComprobanteRetencion_1_0_0(retencion, rutaPDF);
                        Console.WriteLine($"✅ PDF generado: {nombrePdf}");

                        Console.WriteLine($"\n☁️ PASO 9: Subiendo archivos a Frappe...");

                        // Subir PDF
                        Console.WriteLine($"📤 Subiendo PDF...");
                        var respuestaUpload = await _frappeUploader.UploadFileAsync(
                            rutaPDF,
                            nombrePdf,
                            folder: "Home/Documento de Retencion/PDF"
                        );
                        Console.WriteLine($"✅ PDF subido exitosamente");
                        Console.WriteLine($"📋 Respuesta: {respuestaUpload}");

                        // Subir XML
                        Console.WriteLine($"📤 Subiendo XML...");
                        var respuestaXmlUpload = await _frappeUploader.UploadFileAsync(
                            rutaXmlLocal,
                            nombreArchivoXml,
                            folder: "Home/Documento de Retencion/XML"
                        );
                        Console.WriteLine($"✅ XML subido exitosamente");
                        Console.WriteLine($"📋 Respuesta: {respuestaXmlUpload}");

                        // Limpiar archivos locales
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
                            claveAcceso = retencion.InfoTributaria.ClaveAcceso,
                            mensaje = "Retención autorizada, PDF generado y archivos subidos a Frappe correctamente",
                            numeroAutorizacion = retencion.Autorizacion.Numero,
                            fechaAutorizacion = retencion.Autorizacion.Fecha.ToString("yyyy-MM-dd HH:mm:ss"),
                            respuestaFrappePDF = respuestaUpload,
                            respuestaFrappeXML = respuestaXmlUpload
                        });
                    }

                    Console.WriteLine($"\n❌ COMPROBANTE NO AUTORIZADO");
                    Console.WriteLine($"📋 Estado: {autorizacionData?.Estado}");

                    return Ok(new
                    {
                        success = false,
                        estado = autorizacionData?.Estado,
                        mensajes = autorizacionData?.Mensajes?.Mensaje?.Select(m => new
                        {
                            m.Identificador,
                            m.Mensaje_,
                            m.Tipo,
                            m.InformacionAdicional
                        }).ToList()
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
            // ✅ NO necesitas bloque finally porque no estás creando archivos temporales
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