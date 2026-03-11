# Guía de Actualización de Controladores SRI (Plantilla de Refactorización)

Este documento contiene las reglas de negocio y los pasos técnicos que aplicamos al `FacturacionController`, `NotaCreditoController` y `NotaDebitoController`. 
Úsalo como contexto para aplicarlo a cualquier otro repositorio o controlador del SRI.

## 1. Actualización de Modelos de Request (DTOs)
Para mantener consistencia, todos los modelos de peticiones (ej. `GuiaRemisionRequest`, `RetencionRequest`, etc.) necesitan:
- Cambiar la propiedad `FechaEmision` (si existe) de `DateTime` a `string` para manejar el parseo manual a la zona horaria correcta.
- Añadir las propiedades para envío de certificado directo en la petición:
  ```csharp
  public string CertificadoP12Base64 { get; set; }
  public string ContrasenaP12 { get; set; }
  public string LogoBase64 { get; set; }
  ```

## 2. Lógica del Controlador
El controlador debe refactorizarse copiando el flujo exacto que dejamos en `FacturacionController.cs`, con las siguientes directrices:

### A. Gestión Dual de Certificados (Desarrollo vs Producción)
- Se omite la llamada estricta a `_frappeCertService` para descargar certificados.
- **Si el Body trae el certificado** en Base64 (`request.CertificadoP12Base64`), se instancian sus datos desde ahí (Modo Producción).
- **Si el Body NO trae el certificado** (Modo Desarrollo), se busca en disco dentro del directorio relativo `devPruebasCertificados`:
  ```csharp
  var rutaCertificadoLocal = Path.Combine(Directory.GetCurrentDirectory(), "devPruebasCertificados", "signature.p12");
  var rutaLogoLocal = Path.Combine(Directory.GetCurrentDirectory(), "devPruebasCertificados", "logo.png");
  var contrasenaP12 = "Compus1234"; // O la contraseña acordada para pruebas
  ```

### B. Corrección de Decimales XML (F2)
Antes de firmar digitalmente el documento XML (`xmlFirmado = _certificadoService.FirmarDocumento(xmlDoc);`), se debe forzar que todos los nodos numéricos críticos cuenten exactamente con 2 decimales para evitar rechazos de estructura por parte del SRI.
```csharp
void CorregirDecimales(XmlDocument doc, string xpath, string formato)
{
    var nodos = doc.SelectNodes(xpath);
    if (nodos == null) return;
    foreach (XmlNode nodo in nodos)
    {
        if (decimal.TryParse(nodo.InnerText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal valor))
        {
            nodo.InnerText = valor.ToString(formato, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

// Ejemplos de uso (dependerá de los nodos de cada comprobante):
CorregirDecimales(xmlDoc, "//totalSinImpuestos", "F2");
CorregirDecimales(xmlDoc, "//baseImponible", "F2");
CorregirDecimales(xmlDoc, "//valor", "F2");
CorregirDecimales(xmlDoc, "//precioUnitario", "F2");
```

### C. Logging e Inspección Detallada
El sistema debe realizar "Console.WriteLine" extensos y detallados de variables clave.
Específicamente:
- La clave de acceso antes del envío.
- Imprimir en consola el XML generado en formato indentado antes de mandarlo al servicio del SRI.
- Parsear y mostrar toda la jerarquía de la respuesta `envio.Data` y `autorizacion.Data` usando bloques `try-catch` internos para que fallos locales no interrumpan el proceso general de lectura de respuesta de errores.

### D. Eliminación de Dependencias Rígidas de Subida al ERP
- NO subir directamente el PDF/XML procesado al ERP dentro de la misma llamada (`_frappeUploader...`).
- En su lugar, el `IActionResult` devuelto debe incluir (`pdfBase64`, `xmlBase64`) para que el sistema que consume la API decida qué hacer con ellos.
