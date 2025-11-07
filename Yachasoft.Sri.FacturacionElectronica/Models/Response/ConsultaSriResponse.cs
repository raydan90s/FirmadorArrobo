using System.Collections.Generic;

namespace Yachasoft.Sri.FacturacionElectronica.Models.Response
{
    public class ConsultaSriResponse
    {
        public string Ruc { get; set; }
        public ContribuyenteData Data { get; set; }
        public string Error { get; set; }
    }

    public class ContribuyenteData
    {
        public string RazonSocial { get; set; }
        public string NombreComercial { get; set; }
        public string ObligadoLlevarContabilidad { get; set; }
        public string AgenteRetencion { get; set; }
        public string ContribuyenteEspecial { get; set; }
        public string ActividadEconomicaPrincipal { get; set; }
        public string Regimen { get; set; }
        public string TipoContribuyente { get; set; }
        public InformacionFechasContribuyente InformacionFechasContribuyente { get; set; }
        public List<RepresentanteLegal> RepresentantesLegales { get; set; }
        public List<EstablecimientoData> Establecimientos { get; set; }
    }

    public class InformacionFechasContribuyente
    {
        public string FechaInicioActividades { get; set; }
        public string FechaActualizacion { get; set; }
    }

    public class RepresentanteLegal
    {
        public string Nombre { get; set; }
    }

    public class EstablecimientoData
    {
        public string NumeroEstablecimiento { get; set; }
        public string DireccionCompleta { get; set; }
        public string NombreFantasiaComercial { get; set; }
        public string Matriz { get; set; }
        public string Estado { get; set; }
        public string TipoEstablecimiento { get; set; }
    }
}