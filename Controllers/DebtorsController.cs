using Microsoft.AspNetCore.Mvc;
using SoftcodeUnicontaMiddleware.UnicontaService;
using System;

namespace SoftcodeUnicontaMiddleware.Controllers
{
    [ApiController]
    [Route("api/uniconta/debtors")]
    public class DebtorsController : ControllerBase
    {
        private readonly UnicontaSessionService _session;

        public DebtorsController(UnicontaSessionService session)
        {
            _session = session;
        }

        // LIST / BATCH
        [HttpGet]
        public IActionResult Get(
            int offset = 0,
            int limit = 100,
            bool includeDynamic = false)
        {
            if (!_session.IsInitialized)
                return Unauthorized("Uniconta session not initialized");

            return Ok(
                _session.Client.GetDebtorsPaged(offset, limit, includeDynamic)
            );
        }

        // SINGLE
        [HttpGet("{account}")]
        public IActionResult GetOne(
            string account,
            bool includeDynamic = false)
        {
            if (!_session.IsInitialized)
                return Unauthorized("Uniconta session not initialized");

            var debtor = _session.Client.GetDebtorByAccount(account, includeDynamic);

            return debtor == null
                ? NotFound()
                : Ok(debtor);
        }
    }
}
