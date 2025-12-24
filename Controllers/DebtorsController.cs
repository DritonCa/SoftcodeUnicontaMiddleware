using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftcodeUnicontaMiddleware.UnicontaService;

namespace SoftcodeUnicontaMiddleware.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/uniconta/debtors")]
    public class DebtorsController : ControllerBase
    {
        private readonly UnicontaServiceClientFactory _factory;

        public DebtorsController(UnicontaServiceClientFactory factory)
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
                client.GetDebtorsPaged(offset, limit, includeDynamic)
            );
        }

        [HttpGet("{account}")]
        public async Task<IActionResult> GetOne(
            string account,
            bool includeDynamic = false)
        {
            var client = await _factory.CreateAsync();

            var debtor = client.GetDebtorByAccount(account, includeDynamic);

            return debtor == null
                ? NotFound()
                : Ok(debtor);
        }
    }
}
