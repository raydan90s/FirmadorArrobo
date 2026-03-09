# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**FirmadorArrobo** is a .NET 7.0 ASP.NET Web API for Ecuador's electronic invoicing (facturación electrónica). It generates, digitally signs (XAdES), and submits electronic documents to Ecuador's SRI (Servicio de Rentas Internas) via SOAP web services. It integrates with Frappe ERP for certificate/credential management.

## Build & Run Commands

```bash
# Restore and build the entire solution
dotnet build Yachasoft.Sri.sln

# Run the web API
dotnet run --project Yachasoft.Sri.FacturacionElectronica

# Build for release
dotnet build Yachasoft.Sri.sln -c Release

# Docker
docker build -t firmador-arrobo -f Yachasoft.Sri.FacturacionElectronica/Dockerfile .
docker run -p 5000:80 firmador-arrobo
```

**Note:** The Dockerfile references .NET 5.0 runtime while the csproj targets `net7.0`. Keep this in mind when containerizing.

There is no formal test framework (xUnit/NUnit). The only test is a manual integration test controller at `Yachasoft.Sri.FacturacionElectronica/Controllers/tests/LiquidacionCompraControllerTest.cs` accessible via `GET /api/liquidacioncompracontrollertest/GenerarLiquidacion`.

## Architecture

### Solution Projects (11 projects in `Yachasoft.Sri.sln`)

```
API Layer
  └─ Yachasoft.Sri.FacturacionElectronica  → ASP.NET Web API (controllers, Frappe services, helpers)

DI Configuration
  └─ Yachasoft.Sri.DocumentosElectronicos  → Service registration via builder pattern

Core Services
  ├─ Yachasoft.Sri.Signer     → ICertificadoService: certificate loading (P12, Base64, store) + XAdES signing
  ├─ Yachasoft.Sri.WebService  → ISriWebService: SOAP client for SRI receipt validation/authorization
  └─ Yachasoft.Sri.Ride        → IRIDEService: RIDE PDF generation (printable receipts)

Domain
  ├─ Yachasoft.Sri.Modelos  → Document models (Factura, Retencion, NotaCredito, etc.), enums, XML models
  ├─ Yachasoft.Sri.Xsd     → XML schema validation, document contracts, XML mapping
  └─ Yachasoft.Sri.Core    → Shared enums (TipoAmbiente, TipoDocumento, TipoEsquema), helpers, options

Cryptography
  ├─ FirmaXadesNet      → XAdES digital signature library (BouncyCastle-based)
  └─ Microsoft.Xades    → Microsoft XAdES implementation

Utilities
  ├─ Yachasoft.Core  → Generic utilities and extensions
  └─ Yachasoft.Pdf   → PDF generation (PdfSharpCore, QuestPDF)
```

### Request Flow

1. **Controller** receives request (e.g., `FacturacionController`) with document data + Base64 certificate + password
2. **MapperHelper** maps request DTO → domain model (e.g., `Factura_1_0_0Modelo`)
3. **ICertificadoService** loads the P12 certificate and signs the XML (XAdES format)
4. **ISriWebService** sends signed XML to SRI via SOAP (Prueba or Producción environment)
5. **IRIDEService** generates the RIDE PDF receipt
6. **FrappeFileUploader** uploads results back to Frappe ERP

### Key Configuration

- **Environments:** `Prueba` (test, SRI celcer endpoint) / `Producción` (production, SRI cel endpoint) — controlled by `EnumTipoAmbiente`
- **Modes:** `Online` (submit to SRI) / `Offline` (sign locally, auto-authorize) — controlled by `EnumTipoEsquema`
- **Frappe ERP integration** configured in `appsettings.json` under `"Frappe"` section
- **SRI endpoints** configured in `appsettings.json` under `"SriWebService"` with separate URLs for Pruebas/Producción

### API Endpoints

| Route | Controller | Document Type |
|-------|-----------|---------------|
| `/api/factura` | FacturacionController | Invoice |
| `/api/retencion` | ComprobanteRetencionController | Retention certificate |
| `/api/notacredito` | NotaCreditoController | Credit note |
| `/api/notadebito` | NotaDebitoController | Debit note |
| `/api/liquidacion` | LiquidacionCompraController | Purchase liquidation |
| `/api/certificados` | CertificadosController | Certificate verification |
| `/api/consulta` | ConsultaRucController | RUC inquiry |

### DI Registration Pattern

Services are registered via the builder pattern in `Yachasoft.Sri.DocumentosElectronicos`:
- `SRIDocumentosElectronicosServiceCollectionExtensions.AddSRIDocumentosElectronicos()` returns a builder
- Builder exposes `AddDefaultServices()` and `AddPlataformaServices()` to register `ICertificadoService`, `ISriWebService`, `IRIDEService` as transient services

### Supported Document Types (EnumTipoDocumento)

Factura, NotaDebito, NotaCredito, ComprobanteRetencion, GuiaRemision, LiquidacionCompra — each has its own controller, request model, domain model, and RIDE generator method.

## Important Patterns

- All controllers follow the same pattern: receive request → map to domain model → sign XML → submit to SRI → generate RIDE → upload to Frappe
- Certificate loading supports multiple strategies: `CargarDesdeP12`, `CargarDesdeBase64String`, `CargarDesdeAlmacen`, `CargarDesdeDialogo`
- The `SriWebService.Using(tipoAmbiente, tipoEsquema)` method switches between test/production and online/offline modes
- Frappe services (`FrappeCertificateService`, `FrappeCredentialsService`, `FrappeLogoService`) handle all ERP integration
- The codebase is in Spanish — model properties, enums, and variable names use Spanish naming
