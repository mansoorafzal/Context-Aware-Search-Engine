using Microsoft.AspNetCore.Mvc;
using shared;
using System.Net.Mime;

namespace inter_services_pub_sub.Controllers {
    [ApiController]
    [Route("api/v1")]
    public class HomeController(ILogger<HomeController> logger) : ControllerBase {
        private readonly ILogger<HomeController> _logger = logger;

        [HttpPost("push")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(string), StatusCodes.Status503ServiceUnavailable)]
        public IActionResult Push([FromBody] string? item) {
            _logger.LogDebug("Item: {@Item}", item);

            try {
                if (item == null) {
                    return StatusCode(StatusCodes.Status400BadRequest, "Item is invalid!");
                }

                MessageQueue<string>.Push(item);
                return StatusCode(StatusCodes.Status202Accepted);
            }
            catch (InvalidOperationException) {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Queue capacity reached!" });
            }
            catch (Exception exception) {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "{message}, {stackTrace}", exception.Message, exception.StackTrace });
            }
        }

        [HttpGet("pull")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        public ActionResult<string> Pull(CancellationToken cancellationToken) {
            try {
                string? item = MessageQueue<string>.Pull(cancellationToken);

                if (item == null) {
                    return StatusCode(StatusCodes.Status204NoContent);
                }

                return StatusCode(StatusCodes.Status200OK, item);
            }
            catch (Exception exception) {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "{message}, {stackTrace}", exception.Message, exception.StackTrace });
            }
        }
    }
}
