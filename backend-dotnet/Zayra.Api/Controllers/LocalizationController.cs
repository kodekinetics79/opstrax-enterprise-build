using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Infrastructure.Localization;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/localization")]
[Authorize]
public class LocalizationController : ControllerBase
{
    private readonly IHijriDateService _hijri;

    public LocalizationController(IHijriDateService hijri)
    {
        _hijri = hijri;
    }

    [HttpGet("hijri")]
    public ActionResult<DateConversionDto> ToHijri([FromQuery] DateOnly date)
    {
        return Ok(_hijri.FromGregorian(date));
    }
}
