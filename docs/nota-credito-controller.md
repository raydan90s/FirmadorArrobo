# NotaCreditoController — Migración al estándar FacturacionController

## Cambios realizados

### `Models/NotaCreditoRequest.cs`

| Campo | Antes | Después |
|-------|-------|---------|
| `FechaEmision` | `DateTime` | `string` (parsing delegado al controller) |
| `CertificadoP12Base64` | No existía | `string` (nuevo) |
| `ContrasenaP12` | No existía | `string` (nuevo) |
| `LogoBase64` | No existía | `string` (nuevo) |

### `Controllers/NotaCreditoController.cs`

#### Bloques eliminados
- **Test endpoint** (`GET /api/notacredito/Test`) — FacturacionController no tiene test endpoint.
- **Frappe credentials** — Ya no se obtienen credenciales vía `_frappeCredentialsService`.
- **Frappe certificate** — Ya no se verifica/descarga certificado vía `_frappeCertService`.
- **Frappe logo** — Ya no se descarga logo vía `_frappeLogoService`.
- **Frappe upload** — Ya no se suben PDF/XML a Frappe vía `_frappeUploader`.

#### Bloques agregados
- **Certificado dual** — Si `request.CertificadoP12Base64` viene vacío, carga desde `CertificadosDev/signature.p12`. Si no, usa el base64 del request. Contraseña por defecto `Compus1234` para desarrollo.
- **Logo dual** — Si `request.LogoBase64` viene vacío, carga `CertificadosDev/logo.png`. Si no, guarda el base64 como archivo temporal.
- **FechaEmision con offset** — `DateTimeOffset.TryParse` primero (respeta zona horaria), fallback a `DateTime.TryParse`, último fallback a zona horaria Ecuador (`SA Pacific Standard Time`).
- **Debug clave de acceso** — Vuelco a consola de fecha usada, secuencial y clave generada.
- **CorregirFecha** — Reemplaza `//fechaEmision` en el XML a formato `dd/MM/yyyy`.
- **CorregirDecimales** — Aplica formato `F2` a nodos monetarios antes de firmar:
  - `//totalSinImpuestos`, `//valorModificacion`, `//baseImponible`, `//valor`
  - `//precioUnitario`, `//precioTotalSinImpuesto`, `//descuento`, `//cantidad`
- **XML dump** — Vuelco del XML firmado completo a consola antes del envío al SRI.
- **Logging SRI extensivo** — Serialización JSON de la respuesta, inspección de `GetType()`, desglose de comprobantes y mensajes, `InnerException` con tipo y stack trace.
- **DEVUELTA** — Verifica `estadoEnvio == "DEVUELTA"` además de `!envio.Ok`. Extrae mensajes del comprobante.
- **Autorización con backoff** — Usa `SriAutorizacionHelper.ConsultarAutorizacionConReintentosAsync` (4 intentos, backoff exponencial con base 2s).
- **SIN_RESPUESTA** — Mensaje descriptivo indicando que el comprobante está en procesamiento, con la clave de acceso para consulta manual.
- **Base64 output** — Retorna `pdfBase64` + `xmlBase64` en vez de resultados de upload a Frappe.

#### Bloques conservados (sin cambios)
- Constructor con servicios Frappe inyectados (igual que FacturacionController).
- `LimpiarArchivosTemporales` (idéntico).
- `using Yachasoft.Sri.FacturacionElectronica.Helper` (import de `SriAutorizacionHelper`).
- Construcción del modelo dominio: `Emisor`, `Establecimiento`, `PuntoEmision`, `DocumentoModificado`, `Detalles`, `NotaCredito_1_0_0Modelo.NotaCredito`.
- Generación de clave de acceso.
- Serialización XML + firma XAdES.
- Generación de PDF vía `_rideService.NotaCredito_1_0_0`.

## Flujo completo (nuevo)

```
1. Certificado: request body O CertificadosDev/signature.p12
2. Logo:        request body O CertificadosDev/logo.png
3. Construir modelo dominio (Emisor, DocumentoModificado, Detalles, NotaCredito)
4. DateTimeOffset.TryParse(FechaEmision) → DateTime con offset -05
5. Serializar XML vía NotaCredito_1_0_0Mapper
6. CorregirFecha: //fechaEmision = dd/MM/yyyy
7. CorregirDecimales: F2 en 8 campos monetarios
8. FirmarDocumento (XAdES)
9. Dump XML firmado a consola
10. Enviar SRI: try/catch con logging extensivo (JSON + GetType + comprobantes + mensajes + InnerException)
11. Verificar estado envío: DEVUELTA u Ok
12. SriAutorizacionHelper.ConsultarAutorizacionConReintentosAsync (4 intentos, backoff 2s)
13. Verificar autorización: AUTORIZADO, NO AUTORIZADO, SIN_RESPUESTA
14. Generar PDF vía rideService
15. Retornar pdfBase64 + xmlBase64
16. Finally: limpiar archivos temporales
```

## LINO vs FacturacionController

| Aspecto | FacturacionController | NotaCreditoController |
|---------|----------------------|-----------------------|
| Modelo documento | `Factura_1_0_0Modelo` | `NotaCredito_1_0_0Modelo` |
| Mapper | `Factura_1_0_0Mapper` | `NotaCredito_1_0_0Mapper` |
| TipoDocumento | `Factura_1_0_0Modelo.TipoDocumento` | `NotaCredito_1_0_0Modelo.TipoDocumento` |
| Info documento | `InfoFactura` (TotalDescuento, ImporteTotal, TotalConImpuestos, Pagos) | `InfoNotaCredito` (TotalSinImpuestos, ValorModificacion, Moneda, TotalConImpuestos, Motivo) |
| DocumentoModificado | No tiene | `DocumentoSustento` (CódDocumento, NumDocumento, FechaEmisionDocumento) |
| Detalles | Con subsidio (`MapearDetallesConSubsidio`) | Sin subsidio (directo) |
| CorregirDecimales extras | `//importeTotal`, `//propina`, `//total` | `//valorModificacion` |
| Nombre archivos | `FACTURA_...` | `NOTACREDITO_...` |
| RIDE generator | `_rideService.Factura_1_0_0` | `_rideService.NotaCredito_1_0_0` |

El patrón de certificado, logo, fecha, decimales, logging SRI, autorización y base64 output es **idéntico** en ambos controllers.

---

## Guía para el backend — Ejemplo de request

**Endpoint:** `POST /api/notacredito/GenerarNotaCredito`

### JSON de ejemplo

```json
{
  "emisor": {
    "razonSocial": "EMPRESA EJEMPLO S.A.",
    "ruc": "0990012345001",
    "nombreComercial": "Ejemplo Store",
    "direccionMatriz": "Av. Principal 123 y Calle Secundaria",
    "direccionEstablecimiento": "Av. Principal 123 y Calle Secundaria",
    "obligadoContabilidad": true,
    "regimenMicroEmpresas": false,
    "contribuyenteEspecial": "123",
    "agenteRetencion": "1",
    "enumTipoAmbiente": "Prueba"
  },
  "codigoEstablecimiento": 1,
  "codigoPuntoEmision": 1,
  "secuencial": 123,
  "fechaEmision": "2024-12-31T15:30:00-05:00",
  "enumTipoEmision": "Normal",
  "cliente": {
    "identificacion": "1712345678001",
    "razonSocial": "CLIENTE EJEMPLO CÍA. LTDA.",
    "tipoIdentificador": "RUC"
  },
  "infoNotaCredito": {
    "totalSinImpuestos": 100.00,
    "valorModificacion": 112.00,
    "moneda": "DOLAR",
    "totalConImpuestos": [
      {
        "codigo": "2",
        "codigoPorcentaje": "2",
        "baseImponible": 100.00,
        "tarifa": 12.00,
        "valor": 12.00
      }
    ]
  },
  "documentoModificado": {
    "codDocumento": "01",
    "numDocumento": "001-001-000000123",
    "fechaEmisionDocumento": "2024-12-25T00:00:00-05:00"
  },
  "motivo": "Devolución parcial por producto defectuoso",
  "detalles": [
    {
      "item": {
        "codigoPrincipal": "PROD-001",
        "codigoAuxiliar": "AUX-001",
        "descripcion": "Producto Ejemplo Unidad"
      },
      "cantidad": 2,
      "precioUnitario": 50.00,
      "descuento": 0.00,
      "precioTotalSinImpuesto": 100.00,
      "impuestos": [
        {
          "codigo": "2",
          "codigoPorcentaje": "2",
          "baseImponible": 100.00,
          "tarifa": 12.00,
          "valor": 12.00
        }
      ],
      "detallesAdicionales": []
    }
  ],
  "infoAdicional": [
    { "nombre": "Direccion", "valor": "Quito, Av. Ejemplo 456" },
    { "nombre": "Telefono", "valor": "022345678" }
  ],
  "certificadoP12Base64": "MIIK... (base64 del archivo .p12)",
  "contrasenaP12": "password_del_certificado",
  "logoBase64": "iVBOR... (base64 de la imagen PNG del logo)"
}
```

### Campos obligatorios y opcionales

| Campo | Obligatorio | Notas |
|-------|:-----------:|-------|
| `emisor.*` | Sí | Todos los campos del emisor |
| `codigoEstablecimiento` | Sí | Código del establecimiento (001, 002, etc.) |
| `codigoPuntoEmision` | Sí | Código del punto de emisión |
| `secuencial` | Sí | Número secuencial del comprobante |
| `fechaEmision` | Sí | Formato ISO 8601 con o sin offset. Ej: `2024-12-31T15:30:00-05:00`, `2024-12-31`, `2024-12-31T15:30:00` |
| `enumTipoEmision` | Sí | `"Normal"` o `"Contingencia"` |
| `cliente.*` | Sí | Identificación, razón social y tipo de identificador |
| `infoNotaCredito.*` | Sí | Totales e impuestos de la nota de crédito |
| `documentoModificado.*` | Sí | `codDocumento` (ver tabla abajo), número y fecha del documento original |
| `motivo` | Sí | Texto describiendo el motivo de la nota de crédito |
| `detalles[*]` | Sí | Al menos un detalle con item, cantidades y precios |
| `infoAdicional` | No | Lista de campos adicionales (dirección, teléfono, email, etc.) |
| **`certificadoP12Base64`** | **No*** | Base64 del archivo `.p12`. Si se omite, carga desde `CertificadosDev/signature.p12` |
| **`contrasenaP12`** | **No*** | Contraseña del certificado. Si se omite junto con el cert, usa `Compus1234` (solo desarrollo) |
| **`logoBase64`** | **No*** | Base64 del logo PNG. Si se omite, carga desde `CertificadosDev/logo.png` |

> **\*** En producción, estos 3 campos deben enviarse en el request. Si se omiten todos, el controller intenta cargar archivos locales que no existen en el contenedor de producción y devuelve error.

### Códigos de documento (`documentoModificado.codDocumento`)

| Código | Tipo |
|--------|------|
| `"01"` | Factura |
| `"03"` | Liquidación de compra |
| `"04"` | Nota de crédito |
| `"05"` | Nota de débito |
| `"06"` | Guía de remisión |
| `"07"` | Comprobante de retención |

---

## Estructura de la respuesta

### Éxito

```json
{
  "success": true,
  "claveAcceso": "3101202501099001234500120010010000001231234567810",
  "mensaje": "Nota de Crédito autorizada y PDF generado correctamente",
  "numeroAutorizacion": "3101202501099001234500120010010000001231234567810",
  "fechaAutorizacion": "2024-12-31 15:31:00",
  "pdfBase64": "JVBERi0... (PDF completo en base64)",
  "xmlBase64": "PD94bWw... (XML firmado en base64)"
}
```

### Fallo — Nota de Crédito rechazada por el SRI (DEVUELTA)

```json
{
  "success": false,
  "estado": "DEVUELTA",
  "error": "Nota de Crédito rechazada por el SRI",
  "mensajes": [
    {
      "identificador": "43",
      "mensaje_": "Clave de acceso no registrada",
      "tipo": "ERROR",
      "informacionAdicional": "..."
    }
  ]
}
```

### Fallo — No autorizada

```json
{
  "success": false,
  "estado": "NO AUTORIZADO",
  "mensajes": [{ "identificador": "...", "mensaje_": "...", "tipo": "ERROR", "informacionAdicional": "..." }],
  "claveAcceso": "3101202501099001234500120010010000001231234567810",
  "error": "Nota de Crédito no autorizada por el SRI"
}
```

### Fallo — En procesamiento (SIN_RESPUESTA)

```json
{
  "success": false,
  "estado": "SIN_RESPUESTA",
  "mensajes": [],
  "claveAcceso": "3101202501099001234500120010010000001231234567810",
  "error": "La Nota de Crédito aún está en procesamiento. Consulte la autorización más tarde con la clave de acceso."
}
```

### Fallo — Error de conexión o excepción

```json
{
  "success": false,
  "error": "Descripción del error",
  "stackTrace": "...",
  "innerError": "..."
}
```

---

## Notas para el backend

1. **`fechaEmision`** acepta múltiples formatos: ISO 8601 con offset (`2024-12-31T15:30:00-05:00`), sin offset (`2024-12-31T15:30:00`), o solo fecha (`2024-12-31`). Si el parseo falla, usa la hora actual de Ecuador.
2. **`certificadoP12Base64`** debe ser el contenido binario del archivo `.p12` codificado en Base64, sin cabeceras PEM.
3. **`logoBase64`** debe ser una imagen PNG codificada en Base64. Se guarda como archivo temporal en `/tmp/` y se borra al finalizar.
4. La respuesta incluye el PDF y el XML firmado en Base64. El backend debe decodificarlos para almacenarlos o mostrarlos.
5. El controller usa `SriAutorizacionHelper` que realiza **hasta 4 intentos** con espera creciente (2s, 4s, 8s, 16s). El tiempo máximo de espera total es ~30 segundos.
