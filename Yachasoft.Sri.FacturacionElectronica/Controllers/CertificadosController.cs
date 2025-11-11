using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Yachasoft.Sri.FacturacionElectronica.Services;


namespace Yachasoft.Sri.FacturacionElectronica.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CertificadosController : ControllerBase
    {
        private readonly FrappeCertificateService _frappeCertService;

        public CertificadosController(FrappeCertificateService frappeCertService)
        {
            _frappeCertService = frappeCertService;
        }

        [HttpGet("test/{businessName}")]
        public async Task<IActionResult> Test(string businessName)
        {
            var result = await _frappeCertService.DownloadCertificateAsync(businessName);
            return Ok(result);
        }
    }
}
