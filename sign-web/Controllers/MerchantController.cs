using Microsoft.AspNetCore.Mvc;
using VMSign.Shared.Services;
using VMSign.Web.Services;

namespace VMSign.Web.Controllers;

/// <summary>
/// Returns the list of available merchants from SignSDK (not hardcoded).
/// </summary>
public class MerchantController : Controller
{
    private readonly WebSigningService _signingService;

    public MerchantController(WebSigningService signingService)
    {
        _signingService = signingService;
    }

    /// <summary>
    /// Returns merchants registered in SignSDK with display metadata.
    /// </summary>
    [HttpGet]
    public IActionResult List()
    {
        var merchantIds = _signingService.GetRegisteredMerchants();
        var merchants = MerchantRegistry.GetDisplayInfoForIds(merchantIds);

        return Json(merchants.Select(m => new
        {
            m.Id,
            m.Name,
            m.Description,
            m.Tag,
            m.SwitchNote,
            certMode = m.CertMode.ToString()
        }));
    }
}
