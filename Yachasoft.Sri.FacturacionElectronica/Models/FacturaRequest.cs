using System;
using System.Collections.Generic;
using Yachasoft.Sri.Modelos.Base;

namespace Yachasoft.Sri.FacturacionElectronica.Models.Request
{
    public class FacturaRequest
    {
        public EmisorRequest Emisor { get; set; }
        public int CodigoEstablecimiento { get; set; }
        public int CodigoPuntoEmision { get; set; }
        public string FechaEmision { get; set; }
        public ClienteRequest Cliente { get; set; }
        public InfoFacturaRequest InfoFactura { get; set; }
        public List<DetalleRequest> Detalles { get; set; }
        public List<CampoAdicional> InfoAdicional { get; set; }
        public int Secuencial { get; set; }
        public string EnumTipoEmision { get; set; }

        public string CertificadoP12Base64 { get; set; }
        public string ContrasenaP12 { get; set; }
        public string LogoBase64 { get; set; } 
    }

    public class InfoFacturaRequest
    {
        public decimal TotalSinImpuestos { get; set; }
        public decimal TotalDescuento { get; set; }
        public decimal ImporteTotal { get; set; }
        public List<ImpuestoVentaRequest> TotalConImpuestos { get; set; }
        public List<PagoRequest> Pagos { get; set; }
    }
}