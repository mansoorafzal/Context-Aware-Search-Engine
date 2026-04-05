using index_retrieve.Orchestrators;
using Microsoft.AspNetCore.Mvc;
using shared;
using System.Net.Mime;

namespace index_retrieve.Controllers {
    [ApiController]
    [Route("api/v1")]
    public class HomeController(ILogger<HomeController> logger, IOrchestrator orchestrator) : ControllerBase {
        private readonly ILogger<HomeController> _logger = logger;
        private readonly IOrchestrator _orchestrator = orchestrator;

        [HttpPost("start")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status208AlreadyReported)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> StartAsync() {
            var status = await _orchestrator.StartAsync();

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

        [HttpGet("search")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(typeof(IEnumerable<RankedDocument>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(string), StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<IEnumerable<RankedDocument>?>> SearchAsync([FromQuery] string input) {
            if (string.IsNullOrEmpty(input) || string.IsNullOrWhiteSpace(input)) {
                return StatusCode(StatusCodes.Status400BadRequest, "Input is invalid!");
            }

            var (status, data) = await _orchestrator.SearchAsync(input);

            if (status == 1) {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Service is not available. Please try again later!");
            }

            if (status == -1) {
                return StatusCode(StatusCodes.Status500InternalServerError, "An error has occurred. Please try again later!");
            }

            return StatusCode(StatusCodes.Status200OK, data ?? []);
        }

        [HttpGet("load-test")]
        [ProducesResponseType(typeof(IndexRetrieveData), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> LoadTestAsync([FromQuery] string input) {
            if (string.IsNullOrEmpty(input) || string.IsNullOrWhiteSpace(input)) {
                return StatusCode(StatusCodes.Status400BadRequest, "Input is invalid!");
            }

            await _orchestrator.LoadTestAsync(input);
            return StatusCode(StatusCodes.Status200OK, "Load test is initiated successfully!");
        }
    }
}
