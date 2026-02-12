using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Yachasoft.Sri.Core.Atributos;
using Yachasoft.Sri.Core.Enumerados;
using Yachasoft.Sri.Modelos;
using Yachasoft.Sri.Modelos.Base;
using Yachasoft.Sri.Modelos.Enumerados;
using Yachasoft.Sri.Core.Extensions;
using Yachasoft.Sri.Xsd.Map;
using Yachasoft.Sri.FacturacionElectronica.Models.Request;
using Yachasoft.Sri.FacturacionElectronica.Services;

namespace Yachasoft.Sri.FacturacionElectronica.Services
{
    public static class MapperHelper
    {
        public static List<Impuesto> MapearImpuestosDetalle<T>(List<T> impuestosDto) where T : Impuesto
        {
            var impuestos = new List<Impuesto>();

            if (impuestosDto == null || !impuestosDto.Any())
                return impuestos;

            foreach (var imp in impuestosDto)
            {
                try
                {
                    EnumTipoImpuestoIVA? codigoPorcentaje = null;

                    if (imp is ImpuestoCreditoRequest icr2)
                    {
                        codigoPorcentaje = EnumParserHelper.ParseCodigoIVA(icr2.CodigoPorcentaje);
                    }

                    impuestos.Add(new ImpuestoIVA
                    {
                        BaseImponible = imp.BaseImponible,
                        Tarifa = imp.Tarifa,
                        Valor = imp.Valor,
                        CodigoPorcentaje = (EnumTipoImpuestoIVA)codigoPorcentaje
                    });

                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Error al mapear impuesto: {ex.Message}", ex);
                }
            }

            return impuestos;
        }

       public static List<DetalleDocumentoItemPrecioSubsidio> MapearDetallesConSubsidio(List<DetalleRequest> detallesDto)
        {
            var detalles = new List<DetalleDocumentoItemPrecioSubsidio>();

            if (detallesDto == null || !detallesDto.Any())
                throw new ArgumentException("Debe proporcionar al menos un detalle en el documento");

            foreach (var det in detallesDto)
            {
                try
                {
                    var detalle = new DetalleDocumentoItemPrecioSubsidio
                    {
                        Item = new Item
                        {
                            CodigoPrincipal = det.CodigoPrincipal,
                            CodigoAuxiliar = det.CodigoAuxiliar,
                            Descripcion = det.Descripcion
                        },
                        Cantidad = (int)det.Cantidad,
                        PrecioUnitario = det.PrecioUnitario,
                        Descuento = det.Descuento,
                        PrecioTotalSinImpuesto = det.PrecioTotalSinImpuesto,
                        
                        Impuestos = MapearImpuestosDetalleDesdeImpuestoRequest(det.Impuestos),
                        
                        DetallesAdicionales = det.DetallesAdicionales
                    };

                    detalles.Add(detalle);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Error al mapear detalle {det.Descripcion}: {ex.Message}", ex);
                }
            }

            return detalles;
        }

        public static List<DetalleDocumentoItemPrecio> MapearDetalles(List<DetalleRequest> detallesDto)
        {
            var detalles = new List<DetalleDocumentoItemPrecio>();

            if (detallesDto == null || !detallesDto.Any())
                throw new ArgumentException("Debe proporcionar al menos un detalle en el documento");

            foreach (var det in detallesDto)
            {
                try
                {
                    var detalle = new DetalleDocumentoItemPrecio
                    {
                        Item = new Item
                        {
                            CodigoPrincipal = det.CodigoPrincipal,
                            CodigoAuxiliar = det.CodigoAuxiliar,
                            Descripcion = det.Descripcion
                        },
                        Cantidad = (int)det.Cantidad,
                        PrecioUnitario = det.PrecioUnitario,
                        Descuento = det.Descuento,
                        PrecioTotalSinImpuesto = det.PrecioTotalSinImpuesto,
                        
                        Impuestos = MapearImpuestosDetalleDesdeImpuestoRequest(det.Impuestos),
                        
                        DetallesAdicionales = det.DetallesAdicionales
                    };

                    detalles.Add(detalle);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Error al mapear detalle {det.Descripcion}: {ex.Message}", ex);
                }
            }

            return detalles;
        }

        public static List<List<Pago>> MapearPagosParaDocumento(List<PagoRequest> pagosDto)
        {
            return new List<List<Pago>>
            {
                MapearPagos(pagosDto)
            };
        }

        public static List<Pago> MapearPagos(List<PagoRequest> pagosDto)
        {
            var pagos = new List<Pago>();

            if (pagosDto == null || !pagosDto.Any())
                throw new ArgumentException("Debe proporcionar al menos una forma de pago");

            foreach (var pago in pagosDto)
            {
                try
                {
                    var formaPago = EnumParserHelper.ParseFormaPago(pago.FormaPago);

                    pagos.Add(new Pago
                    {
                        FormaPago = formaPago,
                        Total = pago.Total,
                        Plazo = pago.Plazo,
                        UnidadTiempo = pago.UnidadTiempo
                    });
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Error al mapear forma de pago {pago.FormaPago}: {ex.Message}", ex);
                }
            }

            return pagos;
        }

        public static List<ImpuestoVenta> MapearImpuestosVenta(List<ImpuestoVentaRequest> impuestosDto)
        {
            var impuestos = new List<ImpuestoVenta>();

            if (impuestosDto == null || !impuestosDto.Any())
                throw new ArgumentException("Debe proporcionar al menos un impuesto en TotalConImpuestos");

            foreach (var imp in impuestosDto)
            {
                try
                {
                    var codigoPorcentaje = EnumParserHelper.ParseCodigoIVA(imp.CodigoPorcentaje);

                    impuestos.Add(new ImpuestoVentaIVA
                    {
                        BaseImponible = imp.BaseImponible,
                        Tarifa = imp.Tarifa,
                        Valor = imp.Valor,
                        CodigoPorcentaje = codigoPorcentaje
                    });

                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Error al mapear impuesto con código {imp.CodigoPorcentaje}: {ex.Message}", ex);
                }
            }

            return impuestos;
        }

        public static List<ImpuestoVenta> MapearImpuestosVentaDesdeDetalles(List<DetalleDocumentoItemPrecio> detalles)
        {
            if (detalles == null || !detalles.Any())
                throw new ArgumentException("Debe proporcionar al menos un detalle para calcular los impuestos");

            var impuestosAgrupados = detalles
                .SelectMany(d => d.Impuestos.OfType<ImpuestoIVA>())
                .GroupBy(i => i.CodigoPorcentaje)
                .Select(g => new ImpuestoVentaIVA
                {
                    CodigoPorcentaje = g.Key,
                    Tarifa = g.Key switch
                    {
                        EnumTipoImpuestoIVA._0 => 0m,
                        EnumTipoImpuestoIVA._15 => 15m,
                        EnumTipoImpuestoIVA._12 => 12m,
                        EnumTipoImpuestoIVA._14 => 14m,
                        _ => 0m
                    },
                    BaseImponible = g.Sum(x => x.BaseImponible),
                    Valor = g.Sum(x => x.Valor),
                    Codigo = g.First().Codigo
                })
                .Cast<ImpuestoVenta>()
                .ToList();

            return impuestosAgrupados;
        }

        public static List<Impuesto> MapearImpuestosDetalleDesdeRequest(List<ImpuestoVentaRequest> impuestosDto)
        {
            var impuestos = new List<Impuesto>();

            if (impuestosDto == null || !impuestosDto.Any())
                return impuestos;

            foreach (var imp in impuestosDto)
            {
                var codigoPorcentaje = EnumParserHelper.ParseCodigoIVA(imp.CodigoPorcentaje);

                impuestos.Add(new ImpuestoIVA
                {
                    BaseImponible = imp.BaseImponible,
                    Tarifa = imp.Tarifa,
                    Valor = imp.Valor,
                    CodigoPorcentaje = codigoPorcentaje
                });
            }

            return impuestos;
        }


        public static List<ImpuestoVenta> MapearImpuestosVentaDesdeDetallesConSubsidio(List<DetalleDocumentoItemPrecioSubsidio> detalles)
        {
            if (detalles == null || !detalles.Any())
                throw new ArgumentException("Debe proporcionar al menos un detalle para calcular los impuestos");

            var impuestosAgrupados = detalles
                .SelectMany(d => d.Impuestos.OfType<ImpuestoIVA>())
                .GroupBy(i => i.CodigoPorcentaje)
                .Select(g => new ImpuestoVentaIVA
                {
                    CodigoPorcentaje = g.Key,
                    Tarifa = g.Key switch
                    {
                        EnumTipoImpuestoIVA._0 => 0m,
                        EnumTipoImpuestoIVA._15 => 15m,
                        EnumTipoImpuestoIVA._12 => 12m,
                        EnumTipoImpuestoIVA._14 => 14m,
                        _ => 0m
                    },
                    BaseImponible = g.Sum(x => x.BaseImponible),
                    Valor = g.Sum(x => x.Valor),
                    Codigo = g.First().Codigo
                })
                .Cast<ImpuestoVenta>()
                .ToList();
            return impuestosAgrupados;
        }

        public static List<Impuesto> MapearImpuestosDetalleDesdeImpuestoRequest(List<ImpuestoRequest> impuestosDto)
        {
            var impuestos = new List<Impuesto>();

            if (impuestosDto == null || !impuestosDto.Any())
                return impuestos;

            foreach (var imp in impuestosDto)
            {
                try
                {
                    var codigoPorcentaje = EnumParserHelper.ParseCodigoIVA(imp.CodigoPorcentaje);

                    impuestos.Add(new ImpuestoIVA
                    {
                        BaseImponible = imp.BaseImponible,
                        Tarifa = imp.Tarifa,
                        Valor = imp.Valor,
                        CodigoPorcentaje = codigoPorcentaje
                    });

                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Error al mapear impuesto: {ex.Message}", ex);
                }
            }

            return impuestos;
        }

    }
}