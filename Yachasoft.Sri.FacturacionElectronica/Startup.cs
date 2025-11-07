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

            services.AddHttpClient();

            // Swagger
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Yachasoft.Sri.FacturacionElectronica", Version = "v1" });
            });

            // Registro de servicios SRI
            services.AddSRIDocumentosElectronicos(options =>
            {
                options.WebService.TipoAmbiente = Core.Enumerados.EnumTipoAmbiente.Prueba;
                options.WebService.TipoEsquema = Core.Enumerados.EnumTipoEsquema.Offline;
            });

            // 👇 Aquí registras tu FrappeFileUploader
            services.AddSingleton<FrappeFileUploader>();

            // Si prefieres que se cree uno nuevo por request:
            // services.AddScoped<FrappeFileUploader>();
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
