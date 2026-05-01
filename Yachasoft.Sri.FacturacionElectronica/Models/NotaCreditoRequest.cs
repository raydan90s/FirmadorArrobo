using System;
using System.Collections.Generic;
using Yachasoft.Sri.Modelos.Base;

namespace Yachasoft.Sri.FacturacionElectronica.Models.Request
{
    public class NotaCreditoRequest
    {
        public EmisorRequest Emisor { get; set; }
        public int CodigoEstablecimiento { get; set; }
        public int CodigoPuntoEmision { get; set; }
        public string FechaEmision { get; set; }
        public ClienteRequest Cliente { get; set; }
        public InfoNotaCreditoRequest InfoNotaCredito { get; set; }
        public DocumentoModificadoRequest DocumentoModificado { get; set; }
        public List<DetalleNotaCreditoRequest> Detalles { get; set; }
        public List<CampoAdicional> InfoAdicional { get; set; }
        public int Secuencial { get; set; }
        public string EnumTipoEmision { get; set; }
        public string Motivo { get; set; }
        public string CertificadoP12Base64 { get; set; }
        public string ContrasenaP12 { get; set; }
        public string LogoBase64 { get; set; }
    }

    public class InfoNotaCreditoRequest
    {
        public decimal TotalSinImpuestos { get; set; }
        public decimal ValorModificacion { get; set; }
        public string Moneda { get; set; } = "DOLAR";
        public List<ImpuestoVentaRequest> TotalConImpuestos { get; set; }
    }

    public class DetalleNotaCreditoRequest
    {
        public ItemRequest Item { get; set; }
        public int Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Descuento { get; set; }
        public decimal PrecioTotalSinImpuesto { get; set; }
        public List<ImpuestoCreditoRequest> Impuestos { get; set; }
        public List<CampoAdicional> DetallesAdicionales { get; set; }
    }

    public class ItemRequest
    {
        public string CodigoPrincipal { get; set; }
        public string CodigoAuxiliar { get; set; }
        public string Descripcion { get; set; }
    }

    public class ImpuestoCreditoRequest : Impuesto
    {
        public string CodigoPorcentaje { get; set; } 
        public string Codigo { get; set; }
    }
}