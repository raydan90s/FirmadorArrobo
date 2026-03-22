using System;
using System.Linq;
using System.Threading.Tasks;
using Yachasoft.Sri.WebService;
using Yachasoft.Sri.WebService.Response;

namespace Yachasoft.Sri.FacturacionElectronica.Helper
{
    public static class SriAutorizacionHelper
    {
        public static async Task<Response<AutorizarComprobanteResponse.RespuestaAutorizacionComprobante>>
            ConsultarAutorizacionConReintentosAsync(
                ISriWebService webService,
                string claveAcceso,
                int maxIntentos = 4,
                int delayInicialMs = 2000)
        {
            Response<AutorizarComprobanteResponse.RespuestaAutorizacionComprobante> ultimoResultado = null;

            for (int intento = 0; intento < maxIntentos; intento++)
            {
                int delayMs = delayInicialMs * (int)Math.Pow(2, intento);
                await Task.Delay(delayMs);

                ultimoResultado = await webService.AutorizacionComprobanteAsync(claveAcceso);

                if (ultimoResultado.Ok)
                    return ultimoResultado;

                var estado = ultimoResultado.Data?.Autorizaciones?.Autorizacion?.FirstOrDefault()?.Estado;
                if (estado == "NO AUTORIZADO")
                    return ultimoResultado;
            }

            return ultimoResultado;
        }
    }
}
