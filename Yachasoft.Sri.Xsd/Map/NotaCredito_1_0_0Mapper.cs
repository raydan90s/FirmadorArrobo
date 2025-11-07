// Decompiled with JetBrains decompiler
// Type: Yachasoft.Sri.Xsd.Map.NotaCredito_1_0_0Mapper
// Assembly: Yachasoft.Sri.Xsd, Version=1.1.5.0, Culture=neutral, PublicKeyToken=null
// MVID: F09EA61F-E573-4F61-BA4F-32EF8FF0528A
// Assembly location: C:\Users\Joseph\.nuget\packages\infoware.sri.xsds\1.1.5\lib\net5.0\Yachasoft.Sri.Xsd.dll

using Yachasoft.Sri.Core.Helpers;
using Yachasoft.Sri.Modelos;
using Yachasoft.Sri.Modelos.Base;
using Yachasoft.Sri.Modelos.Enumerados;
using Yachasoft.Sri.Xsd.Contratos.NotaCredito_1_0_0;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Yachasoft.Sri.Xsd.Map
{
  public static class NotaCredito_1_0_0Mapper
  {
    public static notaCredito Map(NotaCredito_1_0_0Modelo.NotaCredito documento) => new notaCredito()
    {
      id = notaCreditoID.comprobante,
      version = "1.0.0",
      infoTributaria = new infoTributaria()
      {
        ambiente = documento.PuntoEmision.Establecimiento.Emisor.EnumTipoAmbiente.ObtenerSRICodigo(),
        tipoEmision = documento.InfoTributaria.EnumTipoEmision.ObtenerSRICodigo(),
        razonSocial = documento.PuntoEmision.Establecimiento.Emisor.RazonSocial,
        nombreComercial = documento.PuntoEmision.Establecimiento.Emisor.NombreComercial,
        ruc = documento.PuntoEmision.Establecimiento.Emisor.RUC,
        claveAcceso = documento.InfoTributaria.ClaveAcceso,
        codDoc = documento.TipoDocumento.ObtenerSRICodigo(),
        estab = documento.PuntoEmision.Establecimiento.Codigo.ToString("D3"),
        ptoEmi = documento.PuntoEmision.Codigo.ToString("D3"),
        secuencial = documento.InfoTributaria.Secuencial.ToString("D9"),
        dirMatriz = documento.PuntoEmision.Establecimiento.Emisor.DireccionMatriz,
        regimenMicroempresas = documento.PuntoEmision.Establecimiento.Emisor.RegimenMicroEmpresas ? "CONTRIBUYENTE RÉGIMEN MICROEMPRESAS" : (string) null,
        agenteRetencion = documento.PuntoEmision.Establecimiento.Emisor.AgenteRetencion
      },
      infoNotaCredito = new notaCreditoInfoNotaCredito()
      {
        fechaEmision = documento.FechaEmision.ToSRIFecha(),
        dirEstablecimiento = documento.PuntoEmision.Establecimiento.DireccionEstablecimiento,
        contribuyenteEspecial = documento.PuntoEmision.Establecimiento.Emisor.ContribuyenteEspecial,
        obligadoContabilidad = documento.PuntoEmision.Establecimiento.Emisor.ObligadoContabilidad ? obligadoContabilidad.SI : obligadoContabilidad.NO,
        obligadoContabilidadSpecified = true,
        tipoIdentificacionComprador = documento.Sujeto.TipoIdentificador.ObtenerSRICodigo(),
        razonSocialComprador = documento.Sujeto.RazonSocial,
        identificacionComprador = documento.Sujeto.Identificacion,
        codDocModificado = documento.InfoNotaCredito.DocumentoModificado.CodDocumento.ObtenerSRICodigo(),
        numDocModificado = documento.InfoNotaCredito.DocumentoModificado.NumDocumento,
        fechaEmisionDocSustento = documento.InfoNotaCredito.DocumentoModificado.FechaEmisionDocumento.ToSRIFecha(),
        totalSinImpuestos = documento.InfoNotaCredito.TotalSinImpuestos,
        valorModificacion = documento.InfoNotaCredito.ValorModificacion,
        moneda = documento.InfoNotaCredito.Moneda,
        totalConImpuestos = NotaCredito_1_0_0Mapper.Map(documento.InfoNotaCredito.TotalConImpuestos),
        motivo = documento.InfoNotaCredito.Motivo
      },
      detalles = NotaCredito_1_0_0Mapper.Map2(documento.Detalles),
      infoAdicional = NotaCredito_1_0_0Mapper.Map(documento.InfoAdicional)
    };

    private static notaCreditoDetalle[] Map2(
      List<DetalleDocumentoItemPrecio> detalles)
    {
      return detalles.ConvertAll<notaCreditoDetalle>((Converter<DetalleDocumentoItemPrecio, notaCreditoDetalle>) (facturaDetalle => new notaCreditoDetalle()
      {
        codigoInterno = facturaDetalle.Item.CodigoPrincipal,
        codigoAdicional = facturaDetalle.Item.CodigoAuxiliar,
        descripcion = facturaDetalle.Item.Descripcion,
        cantidad = (Decimal) facturaDetalle.Cantidad,
        precioUnitario = facturaDetalle.PrecioUnitario,
        descuento = facturaDetalle.Descuento,
        descuentoSpecified = facturaDetalle.Descuento > 0M,
        precioTotalSinImpuesto = facturaDetalle.PrecioTotalSinImpuesto,
        detallesAdicionales = NotaCredito_1_0_0Mapper.Map2(facturaDetalle.DetallesAdicionales),
        impuestos = NotaCredito_1_0_0Mapper.Map2(facturaDetalle.Impuestos)
      })).ToArray();
    }

    internal static notaCreditoCampoAdicional[] Map(
      List<CampoAdicional> detallesAdicionales)
    {
      return detallesAdicionales == null || !detallesAdicionales.Any<CampoAdicional>() ? (notaCreditoCampoAdicional[]) null : detallesAdicionales.ConvertAll<notaCreditoCampoAdicional>((Converter<CampoAdicional, notaCreditoCampoAdicional>) (detalle => new notaCreditoCampoAdicional()
      {
        nombre = detalle.Nombre,
        Value = detalle.Valor
      })).ToArray();
    }

    internal static impuesto[] Map2(List<Impuesto> impuestos) => impuestos.ConvertAll<impuesto>((Converter<Impuesto, impuesto>) (impuesto =>
    {
      impuesto impuesto1 = new impuesto()
      {
        baseImponible = impuesto.BaseImponible,
        tarifa = impuesto.Tarifa,
        tarifaSpecified = impuesto.Tarifa > 0M,
        valor = impuesto.Valor
      };
      if (impuesto is Yachasoft.Sri.Modelos.Base.ImpuestoIVA impuestoIva)
      {
        impuesto1.codigo = impuestoIva.Codigo.ObtenerSRICodigo();
        impuesto1.codigoPorcentaje = impuestoIva.CodigoPorcentaje.ObtenerSRICodigo();
      }
      if (impuesto is ImpuestoICE impuestoIce)
      {
        impuesto1.codigo = impuestoIce.Codigo.ObtenerSRICodigo();
        impuesto1.codigoPorcentaje = impuestoIce.CodigoPorcentaje.ObtenerSRICodigo();
      }
      return impuesto1;
    })).ToArray();

    internal static notaCreditoDetalleDetAdicional[] Map2(List<CampoAdicional> detallesAdicionales)
    {
        if (detallesAdicionales == null || detallesAdicionales.Count == 0)
            return null;

        return detallesAdicionales.ConvertAll<notaCreditoDetalleDetAdicional>(
            detalle => new notaCreditoDetalleDetAdicional()
            {
                nombre = detalle.Nombre,
                valor = detalle.Valor
            }
        ).ToArray();
    }

    internal static totalConImpuestosTotalImpuesto[] Map(
      List<ImpuestoVenta> impuestos)
    {
      return impuestos.ConvertAll<totalConImpuestosTotalImpuesto>((Converter<ImpuestoVenta, totalConImpuestosTotalImpuesto>) (impuesto =>
      {
        totalConImpuestosTotalImpuesto impuestosTotalImpuesto = new totalConImpuestosTotalImpuesto()
        {
          baseImponible = impuesto.BaseImponible,
          valor = impuesto.Valor
        };
        if (impuesto is ImpuestoVentaIVA impuestoVentaIva)
        {
          impuestosTotalImpuesto.codigo = impuestoVentaIva.Codigo.ObtenerSRICodigo();
          impuestosTotalImpuesto.codigoPorcentaje = impuestoVentaIva.CodigoPorcentaje.ObtenerSRICodigo();
        }
        if (impuesto is ImpuestoVentaICE impuestoVentaIce)
        {
          impuestosTotalImpuesto.codigo = impuestoVentaIce.Codigo.ObtenerSRICodigo();
          impuestosTotalImpuesto.codigoPorcentaje = impuestoVentaIce.CodigoPorcentaje.ObtenerSRICodigo();
        }
        return impuestosTotalImpuesto;
      })).ToArray();
    }
  }
}
