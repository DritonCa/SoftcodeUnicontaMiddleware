using Microsoft.AspNetCore.Mvc;
using SoftcodeUnicontaMiddleware.UnicontaService;

namespace SoftcodeUnicontaMiddleware.Controllers
{
    [ApiController]
    [Route("api/uniconta/products")]
    public class ProductsController : ControllerBase
    {
        private readonly UnicontaSessionService _session;

        public ProductsController(UnicontaSessionService session)
        {
            _session = session;
        }

        [HttpGet]
        public IActionResult Get(
            int offset = 0,
            int limit = 100,
            bool includeDynamic = false)
        {
            if (!_session.IsInitialized)
                return Unauthorized("Uniconta session not initialized");

            return Ok(
                _session.Client.GetProductsPaged(offset, limit, includeDynamic)
            );
        }

        [HttpGet("{sku}")]
        public IActionResult GetOne(
            string sku,
            bool includeDynamic = false)
        {
            if (!_session.IsInitialized)
                return Unauthorized("Uniconta session not initialized");

            var product = _session.Client.GetProductBySku(sku, includeDynamic);

            return product == null
                ? NotFound()
                : Ok(product);
        }
    }
}
