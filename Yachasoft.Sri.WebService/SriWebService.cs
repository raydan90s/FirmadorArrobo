using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Yachasoft.Sri.Core.Enumerados;
using Yachasoft.Sri.Core.Helpers;
using Yachasoft.Sri.DocumentosElectronicos.Configuracion;
using Yachasoft.Sri.WebService.Request;
using Yachasoft.Sri.WebService.Response;

namespace Yachasoft.Sri.WebService
{
    public class SriWebService : ISriWebService
    {
        private readonly SRIDocumentosElectronicosOptions options;

        public SriWebService(SRIDocumentosElectronicosOptions options)
        {
            this.options = options;
            this.TipoEsquema = options.WebService.TipoEsquema;
            this.TipoAmbiente = options.WebService.TipoAmbiente;
        }

        public EnumTipoAmbiente TipoAmbiente { get; set; }

        public EnumTipoEsquema TipoEsquema { get; set; }

        public SriWebService Using(EnumTipoAmbiente tipoAmbiente, EnumTipoEsquema tipoEsquema)
        {
            return new SriWebService(this.options)
            {
                TipoAmbiente = tipoAmbiente,
                TipoEsquema = tipoEsquema
            };
        }

        public async Task<Response<ValidarComprobanteResponse.RespuestaRecepcionComprobante>> ValidarComprobanteAsync(XmlDocument firmado) => await this.ValidarComprobanteAsync(firmado.OuterXml);

        public async Task<Response<ValidarComprobanteResponse.RespuestaRecepcionComprobante>> ValidarComprobanteAsync(string xmlFirmado)
        {
            var response = new Response<ValidarComprobanteResponse.RespuestaRecepcionComprobante>();
            try
            {
                var soapEnvelopeXml = new XmlDocument();
                soapEnvelopeXml.LoadXml(RequestHelper.GetRequestStream("ValidarComprobanteRequest.xml").ToXmlDocument().OuterXml.Replace("{xmlParameter}", EncodeStringToBase64(xmlFirmado)));

                string responseXml = await ConReintentosAsync(async () =>
                {
                    HttpWebRequest webRequest = CreateWebRequest(this.GenerateUrlValidarComprobante(), "");
                    InsertSoapEnvelopeIntoWebRequest(soapEnvelopeXml, webRequest);
                    using WebResponse responseAsync = await webRequest.GetResponseAsync();
                    using var streamReader = new StreamReader(responseAsync.GetResponseStream());
                    return streamReader.ReadToEnd();
                });

                var xmlDocument2 = new XmlDocument();
                xmlDocument2.LoadXml(responseXml);
                XmlNode firstChild = xmlDocument2.FirstChild;
                while (firstChild != null && !string.Equals(firstChild.Name, "RespuestaRecepcionComprobante", StringComparison.InvariantCultureIgnoreCase))
                    firstChild = firstChild.FirstChild;
                var objectData = new XmlDocument();
                objectData.LoadXml(firstChild.OuterXml);
                ValidarComprobanteResponse.RespuestaRecepcionComprobante recepcionComprobante = objectData.XmlDeserialize<ValidarComprobanteResponse.RespuestaRecepcionComprobante>();
                response.Data = recepcionComprobante;
                response.Ok = recepcionComprobante.Estado == "RECIBIDA";
            }
            catch (Exception ex)
            {
                response.Error = ex.Message;
            }            
            return response;
        }

        public Response<AutoAutorizarComprobanteResponse.Autorizacion> AutoAutorizacionComprobante(
          XmlDocument firmado,
          DateTime fechaAutorizacion,
          string numeroAutorizacion)
        {
            if (this.TipoEsquema == EnumTipoEsquema.Online)
                throw new Exception("Solo se puede usar este método en modo Offline");
            return new Response<AutoAutorizarComprobanteResponse.Autorizacion>()
            {
                Ok = true,
                Data = new AutoAutorizarComprobanteResponse.Autorizacion()
                {
                    Ambiente = this.TipoAmbiente == EnumTipoAmbiente.Prueba ? "PRUEBAS" : "PRODUCCION",
                    FechaAutorizacion = fechaAutorizacion,
                    NumeroAutorizacion = numeroAutorizacion,
                    Comprobante = firmado.OuterXml,
                    Estado = "AUTORIZADO",
                    Mensajes = (string)null
                }
            };
        }

        public async Task<Response<AutorizarComprobanteResponse.RespuestaAutorizacionComprobante>> AutorizacionComprobanteAsync(
          string claveAcceso)
        {
            var response = new Response<AutorizarComprobanteResponse.RespuestaAutorizacionComprobante>();
            try
            {
                var soapEnvelopeXml = new XmlDocument();
                soapEnvelopeXml.LoadXml(RequestHelper.GetRequestStream("AutorizacionComprobanteRequest.xml").ToXmlDocument().OuterXml.Replace("{claveAccesoParameter}", claveAcceso));

                string responseXml = await ConReintentosAsync(async () =>
                {
                    HttpWebRequest webRequest = CreateWebRequest(this.GenerateUrlAutorizacionComprobante(), "");
                    InsertSoapEnvelopeIntoWebRequest(soapEnvelopeXml, webRequest);
                    using WebResponse responseAsync = await webRequest.GetResponseAsync();
                    using var streamReader = new StreamReader(responseAsync.GetResponseStream());
                    return streamReader.ReadToEnd();
                });

                var xmlDocument2 = new XmlDocument();
                xmlDocument2.LoadXml(responseXml);
                XmlNode firstChild = xmlDocument2.FirstChild;
                while (firstChild != null && !string.Equals(firstChild.Name, "RespuestaAutorizacionComprobante", StringComparison.InvariantCultureIgnoreCase))
                    firstChild = firstChild.FirstChild;
                var objectData = new XmlDocument();
                objectData.LoadXml(firstChild.OuterXml);
                AutorizarComprobanteResponse.RespuestaAutorizacionComprobante autorizacionComprobante = objectData.XmlDeserialize<AutorizarComprobanteResponse.RespuestaAutorizacionComprobante>();
                response.Data = autorizacionComprobante;
                response.Ok = "AUTORIZADO".Equals(autorizacionComprobante.Autorizaciones?.Autorizacion?.FirstOrDefault()?.Estado);
            }
            catch (Exception ex)
            {
                response.Error = ex.Message;
            }
            return response;
        }

        internal static void InsertSoapEnvelopeIntoWebRequest(XmlDocument soapEnvelopeXml, HttpWebRequest webRequest) => soapEnvelopeXml.Save(webRequest.GetRequestStream());

        internal static HttpWebRequest CreateWebRequest(string url, string action)
        {
            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
            httpWebRequest.ContentType = "text/xml;charset=\"utf-8\"";
            httpWebRequest.Accept = "text/xml";
            httpWebRequest.Method = "POST";
            httpWebRequest.Headers.Add("SOAPAction", action);
            httpWebRequest.Timeout = 30000;
            httpWebRequest.ReadWriteTimeout = 30000;
            return httpWebRequest;
        }

        private string GenerateUrlValidarComprobante() => this.GenerarUrlPara("RecepcionComprobantes");

        private string GenerateUrlAutorizacionComprobante() => this.GenerarUrlPara("AutorizacionComprobantes");

        private string GenerarUrlPara(string metodo) => $"{this.GenerateRootUrl()}/comprobantes-electronicos-ws/{metodo}{(this.TipoEsquema == EnumTipoEsquema.Offline ? "Offline" : "")}";

        private string GenerateRootUrl() => $"https://{(this.TipoAmbiente == EnumTipoAmbiente.Prueba ? "celcer.sri.gob.ec" : "cel.sri.gob.ec")}";

        internal static string EncodeStringToBase64(string plainTextBytes) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plainTextBytes));

        private static async Task<T> ConReintentosAsync<T>(Func<Task<T>> operacion, int maxIntentos = 3)
        {
            for (int intento = 1; intento < maxIntentos; intento++)
            {
                try
                {
                    return await operacion();
                }
                catch (WebException)
                {
                    await Task.Delay(intento * 1000);
                }
            }
            return await operacion();
        }
    }
}
