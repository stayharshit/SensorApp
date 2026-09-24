using Microsoft.AspNetCore.Mvc;
using SensorApp.Mdl;
using SensorApp.Mgr;

namespace SensorApp.Controllers
{
    [ApiController]
    [Route("api")]
    public class SC : ControllerBase
    {
        [HttpGet("data")]
        public IActionResult G([FromQuery] string tp = "0", [FromQuery] string did = "0",
            [FromQuery] string df = "", [FromQuery] string dt = "")
        {
            return Ok(SM.GetAll(tp, did, df, dt));
        }

        [HttpPost("data")]
        public IActionResult P([FromBody] D d)
        {
            if (d == null || d.Did <= 0 || double.IsNaN(d.V) || double.IsInfinity(d.V))
                return BadRequest("A valid device ID and sensor value are required.");

            return Ok(new { ok = SM.Save(d) });
        }

        [HttpGet("dev")]
        public IActionResult GD([FromQuery] string st = "0")
        {
            return Ok(SM.GetDevs(st));
        }

        [HttpPost("dev")]
        public IActionResult PD([FromBody] D d)
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Nm) || d.Nm.Length > 100 || d.Loc?.Length > 200 || d.Cfg?.Length > 500)
                return BadRequest("A device name and valid field lengths are required.");

            return Ok(new { ok = SM.SaveDev(d) });
        }

        [HttpGet("calc")]
        public IActionResult C([FromQuery] int did = 1)
        {
            if (did <= 0) return BadRequest("did must be greater than zero.");

            return Ok(SM.Calc(did));
        }

        [HttpGet("log")]
        public IActionResult GL([FromQuery] string did = "0", [FromQuery] string flg = "-1")
        {
            return Ok(SM.GetLog(did, flg));
        }

        [HttpGet("stats")]
        public IActionResult GS([FromQuery] int did = 1)
        {
            if (did <= 0) return BadRequest("did must be greater than zero.");

            return Ok(SM.Stats(did));
        }
    }
}
