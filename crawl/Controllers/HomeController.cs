using crawl.Orchestrators;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace crawl.Controllers {
    [ApiController]
    [Route("api/v1")]
    public class HomeController(ILogger<HomeController> logger, IOptions<Configurations> options, IOrchestrator orchestrator) : ControllerBase {
        private readonly ILogger<HomeController> _logger = logger;
        private readonly Configurations _configurations = options.Value;
        private readonly IOrchestrator _orchestrator = orchestrator;

        [HttpPost("start")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status208AlreadyReported)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> StartAsync() {
            if (_configurations.Seeds == null || _configurations.Seeds.Length == 0) {
                return StatusCode(StatusCodes.Status400BadRequest, "Seed(s) are invalid!");
            }
            
            var status = await _orchestrator.StartAsync(_configurations.Seeds);

            if (status == 1) {
                return StatusCode(StatusCodes.Status208AlreadyReported, "Pipeline is already running!");
            }
            
            if (status == 0) {
                return StatusCode(StatusCodes.Status200OK, "Pipeline is started successfully!");
            }

            return StatusCode(StatusCodes.Status500InternalServerError, "An error has occurred. Please try again later!");
        }

        [HttpPost("stop")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status208AlreadyReported)]
        public IActionResult Stop() {
            var status = _orchestrator.Stop();

            if (status == 1) {
                return StatusCode(StatusCodes.Status208AlreadyReported, "Pipeline is already stopped!");
            }

            return StatusCode(StatusCodes.Status200OK, "Pipeline is stopped successfully!");
        }

        [HttpGet("status")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> StatusAsync() {
            var data = await _orchestrator.StatusAsync();

            if (data == null) {
                return StatusCode(StatusCodes.Status500InternalServerError, "An error has occurred. Failed to compute the status!");
            }

            return StatusCode(StatusCodes.Status200OK, data);
        }
    }
}
