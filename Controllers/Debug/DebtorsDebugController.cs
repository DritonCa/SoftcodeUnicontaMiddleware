using Microsoft.AspNetCore.Mvc;
using SoftcodeUnicontaMiddleware.UnicontaService;
using System;

namespace SoftcodeUnicontaMiddleware.Controllers.Debug
{
    [ApiController]
    [Route("api/debug/uniconta/debtors")]
    public class DebtorsDebugController : ControllerBase
    {
        private readonly UnicontaSessionService _sessionService;

        public DebtorsDebugController(UnicontaSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        [HttpGet]
        public IActionResult DumpDebtorFields()
        {
            if (!_sessionService.IsInitialized)
                return Unauthorized("Uniconta session not initialized");

            try
            {
                var dump = _sessionService.Client.DebugDumpFirstDebtor();

                if (dump == null)
                    return NotFound("No debtors found");

                return Ok(dump);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
