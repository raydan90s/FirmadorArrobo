# Historial: Desacoplamiento de integración Frappe

## Contexto general

El proyecto FirmadorArrobo es un servicio de firma electrónica para el SRI (Ecuador). Originalmente fue desarrollado acoplado a **Frappe** como ERP para gestionar credenciales, certificados, logos y almacenamiento de documentos. Sin embargo, el backend de producción real **no es Frappe** — el usuario administra credenciales y certificados desde su propia UI.

## Línea de tiempo

### Antes del commit `8072440` (antes de Feb 2026)

`FacturacionController` (el controller principal) dependía completamente de Frappe:

```
1. FrappeCredentialsService → obtener apiKey/apiSecret por emisor
2. FrappeCertificateService → descargar .p12 desde Frappe
3. FrappeLogoService → descargar logo PNG desde Frappe
4. (Firmar XML, enviar SRI, generar PDF)
5. FrappeUploader → subir PDF y XML de vuelta a Frappe (folders Home/Facturacion/PDF, Home/Facturacion/XML)
```

El response incluía `respuestaFrappePDF`, `respuestaFrappeXML`, `credencialesUsadas`.

### Commit `8072440` (11 Feb 2026, autor: raymich, "cambios a partir de 501")

**Primera desvinculación total de Frappe en `FacturacionController`.** Cambios:

| Área | Antes | Después |
|------|-------|---------|
| Certificado | `FrappeCertificateService` descarga desde Frappe | `request.CertificadoP12Base64` (producción) o archivo local `devPruebasCertificados/signature.p12` (desarrollo) |
| Contraseña | Viene de Frappe junto con el cert | `request.ContrasenaP12` (producción) o hardcodeada `Compus1234` (desarrollo) |
| Logo | `FrappeLogoService` descarga desde Frappe | `request.LogoBase64` (producción) o archivo local `devPruebasCertificados/logo.png` (desarrollo) |
| Upload resultados | `FrappeUploader` sube a Frappe | Retorna `pdfBase64` + `xmlBase64` en el response |
| Response | `respuestaFrappePDF`, `respuestaFrappeXML`, `credencialesUsadas` | `pdfBase64`, `xmlBase64`, `success`, `claveAcceso`, `numeroAutorizacion` |

Cambios adicionales introducidos en este commit:
- `FechaEmision` pasó de `DateTime` a `string` con `DateTime.TryParse` + fallback a zona horaria Ecuador
- `CorregirFecha`: reescribe `//fechaEmision` en el XML a formato `dd/MM/yyyy`
- Logging SRI extensivo: serialización JSON de respuestas, inspección de `GetType()`, desglose de comprobantes/mensajes, `InnerException`
- Detección de estado DEVUELTA (`estadoEnvio == "DEVUELTA"`)
- Manejo de estado SIN_RESPUESTA con mensaje descriptivo
- Captura separada de excepciones para envío y autorización

### Archivos modificados en `8072440`

```
Yachasoft.Sri.FacturacionElectronica/Controllers/FacturacionController.cs  (re-escritura completa)
Yachasoft.Sri.FacturacionElectronica/Models/FacturaRequest.cs              (+3 campos: CertificadoP12Base64, ContrasenaP12, LogoBase64)
Yachasoft.Sri.FacturacionElectronica/Helper/MapperHelper.cs                (-logs de debug)
Yachasoft.Sri.FacturacionElectronica/Startup.cs                            (-comentarios, sin cambios funcionales)
```

### Commit `cbc2c80` y `e9ec1ae` (mejoras incrementales)

- Ajustes para entorno servidor
- Corrección de fallos intermitentes en comunicación con SRI

### Commit `879c03a` (mejora date parsing)

- Corrección adicional del parseo de fechas para facturación nocturna

### Commit `9a0c8c8` (paths portables, actual)

- Reemplazo de paths hardcodeados `devPruebasCertificados` → `CertificadosDev/`
- El directorio `devPruebasCertificados` nunca existió en Docker — se reemplazó por el portable `CertificadosDev/`

### Commits `b8f9255` y `2ce8abc` (rama actual `feat/nota-debito-credito`)

- **`b8f9255`**: Migración de `NotaCreditoController` al mismo patrón de `FacturacionController` (sin Frappe, dual cert/logo, base64 output, SriAutorizacionHelper)
- **`2ce8abc`**: Migración de `NotaDebitoController` al mismo patrón

---

## Por qué se tomó esta decisión

1. **El backend real no es Frappe.** La UI del usuario administra credenciales y certificados directamente. Frappe era una dependencia externa innecesaria.
2. **Portabilidad.** Al recibir certificado/logo en el request body, el servicio funciona con cualquier backend, no solo Frappe.
3. **Menos puntos de fallo.** Cada request eliminó 3-5 llamadas HTTP a Frappe (credenciales, certificado, logo, upload PDF, upload XML).
4. **Simplicidad.** El controller se volvió autocontenido: recibe datos → firma → envía SRI → retorna PDF+XML.

La inyección de servicios Frappe se conserva en el constructor de todos los controllers (por consistencia DI), pero **ningún controller en esta rama los llama**.
