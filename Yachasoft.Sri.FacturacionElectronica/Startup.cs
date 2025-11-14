using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Yachasoft.Sri.FacturacionElectronica.Services;

namespace Yachasoft.Sri.FacturacionElectronica
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // Se ejecuta al iniciar la app
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Yachasoft.Sri.FacturacionElectronica",
                    Version = "v1"
                });
            });

            // Registro del cliente HTTP
            services.AddHttpClient();

            // Configuración Frappe
            services.Configure<FrappeSettings>(Configuration.GetSection("Frappe"));

            // Servicios de Frappe
            services.AddHttpClient<FrappeCertificateService>(); // certificado .p12

  

            // Registro del uploader para archivos PDF/XML
            services.AddSingleton<FrappeFileUploader>();

            // Registro del core SRI
            services.AddSRIDocumentosElectronicos(options =>
            {
                options.WebService.TipoAmbiente = Core.Enumerados.EnumTipoAmbiente.Prueba;
                options.WebService.TipoEsquema = Core.Enumerados.EnumTipoEsquema.Offline;
            });
        }

        // Configuración del pipeline HTTP
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Yachasoft.Sri.FacturacionElectronica v1"));
            }

            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
