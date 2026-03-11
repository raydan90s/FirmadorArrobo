using System;
using System.Collections.Generic;
using Yachasoft.Sri.Modelos.Base;

namespace Yachasoft.Sri.FacturacionElectronica.Models.Request
{
    public class NotaDebitoRequest
    {
        public EmisorRequest Emisor { get; set; }
        public int CodigoEstablecimiento { get; set; }
        public int CodigoPuntoEmision { get; set; }
        public string FechaEmision { get; set; }
        public ClienteRequest Cliente { get; set; }
        public InfoNotaDebitoRequest InfoNotaDebito { get; set; }
        public DocumentoModificadoRequest DocumentoModificado { get; set; }
        public List<MotivoRequest> Motivos { get; set; }
        public List<CampoAdicional> InfoAdicional { get; set; }
        public int Secuencial { get; set; }
        public string EnumTipoEmision { get; set; }
        public string CertificadoP12Base64 { get; set; }
        public string ContrasenaP12 { get; set; }
        public string LogoBase64 { get; set; }
    }

    public class InfoNotaDebitoRequest
    {
        public decimal TotalSinImpuestos { get; set; }
        public decimal ValorTotal { get; set; }
        public List<ImpuestoVentaRequest> Impuestos { get; set; }
        public List<PagoRequest> Pagos { get; set; }
    }

    public class DocumentoModificadoRequest
    {
        public string CodDocumento { get; set; }           
        public string NumDocumento { get; set; }
        public DateTime FechaEmisionDocumento { get; set; }
    }

    public class MotivoRequest
    {
        public string Razon { get; set; }
        public decimal Valor { get; set; }
    }
}