using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Hardware.Plc;

namespace TriviaBattle.Server.Controllers;

/// <summary>Hardware health for the admin page: is the PLC connected, when did it last send a press.</summary>
[ApiController]
[Route("api/hardware")]
public class HardwareController(PlcSettings plc, PlcStatus status) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(new
    {
        plc = new
        {
            enabled = plc.Enabled,
            endpoint = $"{plc.Host}:{plc.Port}",
            status.IsConnected,
            status.ConnectedSince,
            status.LastError,
            status.LastErrorAt,
            status.PressesReceived,
            status.LastPressAt,
        },
    });
}
