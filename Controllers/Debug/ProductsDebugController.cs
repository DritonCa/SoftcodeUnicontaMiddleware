using Microsoft.AspNetCore.Mvc;
using SoftcodeUnicontaMiddleware.UnicontaService;
using System;

namespace SoftcodeUnicontaMiddleware.Controllers
{
    [ApiController]
    [Route("api/debug/uniconta/products")]
    public class ProductsDebugController : ControllerBase
    {
        private readonly UnicontaSessionService _sessionService;

        public ProductsDebugController(UnicontaSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        [HttpGet("prod")]
        public IActionResult DumpProdItems()
        {
            if (!_sessionService.IsInitialized)
                return Unauthorized("Uniconta session not initialized");

            try
            {
                return Ok(_sessionService.Client.DebugDumpProdItems());
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("inv")]
        public IActionResult DumpInvItems()
        {
            if (!_sessionService.IsInitialized)
                return Unauthorized("Uniconta session not initialized");

            try
            {
                return Ok(_sessionService.Client.DebugDumpInvItems());
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
