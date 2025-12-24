using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftcodeUnicontaMiddleware.UnicontaService;

namespace SoftcodeUnicontaMiddleware.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/uniconta/products")]
    public class ProductsController : ControllerBase
    {
        private readonly UnicontaServiceClientFactory _factory;

        public ProductsController(UnicontaServiceClientFactory factory)
        {
            _factory = factory;
        }

        [HttpGet]
        public async Task<IActionResult> Get(
            int offset = 0,
            int limit = 100,
            bool includeDynamic = false)
        {
            var client = await _factory.CreateAsync();

            return Ok(
                client.GetProductsPaged(offset, limit, includeDynamic)
            );
        }

        [HttpGet("{sku}")]
        public async Task<IActionResult> GetOne(
            string sku,
            bool includeDynamic = false)
        {
            var client = await _factory.CreateAsync();

            var product = client.GetProductBySku(sku, includeDynamic);

            return product == null
                ? NotFound()
                : Ok(product);
        }
    }
}
