using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Yachasoft.Sri.FacturacionElectronica.Models.Request;
using Yachasoft.Sri.FacturacionElectronica.Models.Response;

namespace Yachasoft.Sri.FacturacionElectronica.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConsultaSriController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        
        private const string SRI_CONTRIBUYENTE_URL = "https://srienlinea.sri.gob.ec/sri-catastro-sujeto-servicio-internet/rest/ConsolidadoContribuyente/obtenerPorNumerosRuc?=&ruc=";
        private const string SRI_ESTABLECIMIENTO_URL = "https://srienlinea.sri.gob.ec/sri-catastro-sujeto-servicio-internet/rest/Establecimiento/consultarPorNumeroRuc?numeroRuc=";

        public ConsultaSriController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost("consultaSRI")]
        public async Task<IActionResult> ConsultaSri([FromBody] RucRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Ruc))
                {
                    return Ok(new ConsultaSriResponse
                    { 
                        Error = "El RUC es requerido" 
                    });
                }

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                // Consultar contribuyente
                var contribuyenteUrl = SRI_CONTRIBUYENTE_URL + request.Ruc;
                Console.WriteLine($"🔍 Consultando contribuyente: {contribuyenteUrl}");
                
                var responseContribuyente = await client.GetAsync(contribuyenteUrl);

                // Validar código de estado HTTP
                if (responseContribuyente.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    Console.WriteLine($"⚠️ RUC no encontrado: {request.Ruc}");
                    return Ok(new ConsultaSriResponse
                    {
                        Error = "El RUC ingresado no existe o no es válido"
                    });
                }

                if (!responseContribuyente.IsSuccessStatusCode)
                {
                    return Ok(new ConsultaSriResponse
                    {
                        Error = $"Error al consultar el SRI. Status: {responseContribuyente.StatusCode}"
                    });
                }

                var contribuyenteJson = await responseContribuyente.Content.ReadAsStringAsync();
                
                // Validar que la respuesta no esté vacía
                if (string.IsNullOrWhiteSpace(contribuyenteJson))
                {
                    Console.WriteLine($"⚠️ Respuesta vacía del SRI para RUC: {request.Ruc}");
                    return Ok(new ConsultaSriResponse
                    {
                        Error = "El RUC ingresado no existe o no es válido"
                    });
                }

                Console.WriteLine($"📄 JSON contribuyente recibido: {contribuyenteJson.Substring(0, Math.Min(200, contribuyenteJson.Length))}...");

                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                };

                var contribuyenteList = JsonSerializer.Deserialize<List<ContribuyenteDataSri>>(
                    contribuyenteJson,
                    options
                );

                if (contribuyenteList == null || !contribuyenteList.Any())
                {
                    return Ok(new ConsultaSriResponse
                    {
                        Error = "El RUC ingresado no existe o no es válido"
                    });
                }

                var contribuyenteSri = contribuyenteList[0];

                // Consultar establecimientos
                var establecimientoUrl = SRI_ESTABLECIMIENTO_URL + request.Ruc;
                Console.WriteLine($"🔍 Consultando establecimientos: {establecimientoUrl}");
                
                var responseEstablecimiento = await client.GetAsync(establecimientoUrl);

                List<EstablecimientoDataSri> establecimientos = null;

                // Los establecimientos pueden no existir o devolver 204
                if (responseEstablecimiento.IsSuccessStatusCode && 
                    responseEstablecimiento.StatusCode != System.Net.HttpStatusCode.NoContent)
                {
                    var establecimientoJson = await responseEstablecimiento.Content.ReadAsStringAsync();
                    
                    if (!string.IsNullOrWhiteSpace(establecimientoJson))
                    {
                        Console.WriteLine($"📄 JSON establecimientos recibido: {establecimientoJson.Substring(0, Math.Min(200, establecimientoJson.Length))}...");
                        
                        establecimientos = JsonSerializer.Deserialize<List<EstablecimientoDataSri>>(
                            establecimientoJson,
                            options
                        );
                    }
                }

                // Mapear a estructura de respuesta - FORMATO EXACTO PARA FRAPPE
                var response = new ConsultaSriResponse
                {
                    Ruc = request.Ruc,
                    Data = new ContribuyenteData
                    {
                        RazonSocial = contribuyenteSri.RazonSocial,
                        NombreComercial = contribuyenteSri.NombreFantasiaComercial,
                        ObligadoLlevarContabilidad = contribuyenteSri.ObligadoLlevarContabilidad,
                        AgenteRetencion = contribuyenteSri.AgenteRetencion,
                        ContribuyenteEspecial = contribuyenteSri.ContribuyenteEspecial,
                        ActividadEconomicaPrincipal = contribuyenteSri.ActividadEconomicaPrincipal,
                        Regimen = contribuyenteSri.Regimen,
                        TipoContribuyente = contribuyenteSri.TipoContribuyente,
                        InformacionFechasContribuyente = contribuyenteSri.InformacionFechasContribuyente != null 
                            ? new InformacionFechasContribuyente
                            {
                                FechaInicioActividades = contribuyenteSri.InformacionFechasContribuyente.FechaInicioActividades,
                                FechaActualizacion = contribuyenteSri.InformacionFechasContribuyente.FechaActualizacion
                            }
                            : null,
                        RepresentantesLegales = contribuyenteSri.RepresentantesLegales?.Select(r => new RepresentanteLegal
                        {
                            Nombre = r.Nombre
                        }).ToList() ?? new List<RepresentanteLegal>(),
                        Establecimientos = establecimientos?.Select(e => new EstablecimientoData
                        {
                            NumeroEstablecimiento = e.NumeroEstablecimiento,
                            DireccionCompleta = e.DireccionCompleta,
                            NombreFantasiaComercial = e.NombreFantasiaComercial,
                            Matriz = e.Matriz,
                            Estado = e.Estado,
                            TipoEstablecimiento = e.TipoEstablecimiento
                        }).ToList() ?? new List<EstablecimientoData>()
                    },
                    Error = null
                };

                Console.WriteLine($"✅ Contribuyente: {contribuyenteSri.RazonSocial}");
                Console.WriteLine($"✅ Establecimientos: {establecimientos?.Count ?? 0}");

                // IMPORTANTE: Configurar opciones de serialización para camelCase
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = true
                };

                return new JsonResult(response, jsonOptions);
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"❌ Error HTTP: {httpEx.Message}");
                return Ok(new ConsultaSriResponse
                {
                    Error = $"Error de conexión con el SRI: {httpEx.Message}"
                });
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"❌ Error JSON: {jsonEx.Message}");
                // Si hay error al deserializar JSON, significa que el RUC no existe o la respuesta está vacía
                return Ok(new ConsultaSriResponse
                {
                    Error = "El RUC ingresado no existe o no es válido"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                
                return Ok(new ConsultaSriResponse
                {
                    Error = "Error al procesar la consulta. Intente nuevamente."
                });
            }
        }
    }
}