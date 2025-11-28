using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Yachasoft.Sri.FacturacionElectronica.Services;
using System.Net;

namespace Yachasoft.Sri.FacturacionElectronica
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            
            // 🔥 CONFIGURACIÓN TLS GLOBAL - Se ejecuta al iniciar la aplicación
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            
 
        }

        public IConfiguration Configuration { get; }

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

            // ✅ Servicios Frappe con HttpClient
            services.AddHttpClient<FrappeLogoService>();
            services.AddHttpClient<FrappeCertificateService>();
            services.AddHttpClient<IFrappeCredentialsService, FrappeCredentialsService>();
            services.AddHttpClient<IFrappeFileUploader, FrappeFileUploader>();

            
        }

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