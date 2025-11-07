using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Yachasoft.Sri.FacturacionElectronica.Models.Response
{
    public class ContribuyenteDataSri
    {
        [JsonPropertyName("numeroRuc")]
        public string NumeroRuc { get; set; }

        [JsonPropertyName("razonSocial")]
        public string RazonSocial { get; set; }

        [JsonPropertyName("nombreFantasiaComercial")]
        public string NombreFantasiaComercial { get; set; }

        [JsonPropertyName("estadoContribuyenteRuc")]
        public string EstadoContribuyenteRuc { get; set; }

        [JsonPropertyName("tipoContribuyente")]
        public string TipoContribuyente { get; set; }

        [JsonPropertyName("categoria")]
        public string Categoria { get; set; }

        [JsonPropertyName("obligadoLlevarContabilidad")]
        public string ObligadoLlevarContabilidad { get; set; }

        [JsonPropertyName("agenteRetencion")]
        public string AgenteRetencion { get; set; }

        [JsonPropertyName("contribuyenteEspecial")]
        public string ContribuyenteEspecial { get; set; }

        [JsonPropertyName("regimen")]
        public string Regimen { get; set; }

        [JsonPropertyName("actividadEconomicaPrincipal")]
        public string ActividadEconomicaPrincipal { get; set; }

        [JsonPropertyName("informacionFechasContribuyente")]
        public InformacionFechas InformacionFechasContribuyente { get; set; }

        [JsonPropertyName("representantesLegales")]
        public List<RepresentanteLegalSri> RepresentantesLegales { get; set; }
    }

    public class InformacionFechas
    {
        [JsonPropertyName("fechaInicioActividades")]
        public string FechaInicioActividades { get; set; }

        [JsonPropertyName("fechaActualizacion")]
        public string FechaActualizacion { get; set; }

        [JsonPropertyName("fechaCese")]
        public string FechaCese { get; set; }
    }

    public class RepresentanteLegalSri
    {
        [JsonPropertyName("identificacion")]
        public string Identificacion { get; set; }

        [JsonPropertyName("nombre")]
        public string Nombre { get; set; }
    }

    public class EstablecimientoDataSri
    {
        [JsonPropertyName("numeroEstablecimiento")]
        public string NumeroEstablecimiento { get; set; }

        [JsonPropertyName("direccionCompleta")]
        public string DireccionCompleta { get; set; }

        [JsonPropertyName("estado")]
        public string Estado { get; set; }

        [JsonPropertyName("tipoEstablecimiento")]
        public string TipoEstablecimiento { get; set; }

        [JsonPropertyName("nombreFantasiaComercial")]
        public string NombreFantasiaComercial { get; set; }

        [JsonPropertyName("matriz")]
        public string Matriz { get; set; }
    }
}