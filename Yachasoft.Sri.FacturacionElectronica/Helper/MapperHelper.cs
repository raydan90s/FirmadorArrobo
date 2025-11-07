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
                    Console.WriteLine($"Procesando impuesto detalle con código: {(imp is ImpuestoCreditoRequest icr ? icr.CodigoPorcentaje : "N/A")}");

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

                    Console.WriteLine($"Impuesto detalle mapeado correctamente - Base: {imp.BaseImponible}, Código: {codigoPorcentaje}");
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
                    Console.WriteLine($"Procesando detalle con subsidio: {det.Descripcion}");

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
                        
                        // ✅ USAR EL NUEVO MÉTODO:
                        Impuestos = MapearImpuestosDetalleDesdeImpuestoRequest(det.Impuestos),
                        
                        DetallesAdicionales = det.DetallesAdicionales
                    };

                    detalles.Add(detalle);
                    Console.WriteLine($"✅ Detalle con subsidio mapeado correctamente: {det.CodigoPrincipal} - {det.Descripcion}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error al mapear detalle con subsidio: {ex.Message}");
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
                    Console.WriteLine($"Procesando detalle: {det.Descripcion}");

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
                        
                        // ✅ USAR EL NUEVO MÉTODO:
                        Impuestos = MapearImpuestosDetalleDesdeImpuestoRequest(det.Impuestos),
                        
                        DetallesAdicionales = det.DetallesAdicionales
                    };

                    detalles.Add(detalle);
                    Console.WriteLine($"✅ Detalle mapeado correctamente: {det.CodigoPrincipal} - {det.Descripcion}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error al mapear detalle: {ex.Message}");
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
                    Console.WriteLine($"Procesando pago con forma: {pago.FormaPago}");

                    var formaPago = EnumParserHelper.ParseFormaPago(pago.FormaPago);

                    pagos.Add(new Pago
                    {
                        FormaPago = formaPago,
                        Total = pago.Total,
                        Plazo = pago.Plazo,
                        UnidadTiempo = pago.UnidadTiempo
                    });

                    Console.WriteLine($"Pago mapeado correctamente: {formaPago} - ${pago.Total}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al mapear pago: {ex.Message}");
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
                    Console.WriteLine($"Procesando impuesto venta con código: {imp.CodigoPorcentaje}");

                    var codigoPorcentaje = EnumParserHelper.ParseCodigoIVA(imp.CodigoPorcentaje);
                    Console.WriteLine($"Código parseado: {codigoPorcentaje}");

                    impuestos.Add(new ImpuestoVentaIVA
                    {
                        BaseImponible = imp.BaseImponible,
                        Tarifa = imp.Tarifa,
                        Valor = imp.Valor,
                        CodigoPorcentaje = codigoPorcentaje
                    });

                    Console.WriteLine($"Impuesto venta mapeado correctamente - Base: {imp.BaseImponible}, Código: {codigoPorcentaje}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al mapear impuesto venta: {ex.Message}");
                    throw new ArgumentException($"Error al mapear impuesto con código {imp.CodigoPorcentaje}: {ex.Message}", ex);
                }
            }

            return impuestos;
        }

        public static List<ImpuestoVenta> MapearImpuestosVentaDesdeDetalles(List<DetalleDocumentoItemPrecio> detalles)
        {
            if (detalles == null || !detalles.Any())
                throw new ArgumentException("Debe proporcionar al menos un detalle para calcular los impuestos");

            Console.WriteLine($"📊 Calculando TotalConImpuestos desde {detalles.Count} detalles (Liquidación)...");

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

            Console.WriteLine($"📊 Calculando TotalConImpuestos desde {detalles.Count} detalles (Factura)...");

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
                    Console.WriteLine($"Procesando ImpuestoRequest con código: {imp.CodigoPorcentaje}");

                    var codigoPorcentaje = EnumParserHelper.ParseCodigoIVA(imp.CodigoPorcentaje);

                    impuestos.Add(new ImpuestoIVA
                    {
                        BaseImponible = imp.BaseImponible,
                        Tarifa = imp.Tarifa,
                        Valor = imp.Valor,
                        CodigoPorcentaje = codigoPorcentaje
                    });

                    Console.WriteLine($"✅ ImpuestoRequest mapeado - Base: {imp.BaseImponible}, Código: {codigoPorcentaje}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error al mapear ImpuestoRequest: {ex.Message}");
                    throw new ArgumentException($"Error al mapear impuesto: {ex.Message}", ex);
                }
            }

            return impuestos;
        }

    }
}