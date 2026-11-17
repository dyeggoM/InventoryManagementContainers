using IMCAPI.DB;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Controllers;

[ApiController]
[Route("api/organization")]
[Authorize]
public sealed class OrganizationController(ApplicationContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var organizations = await context.Constants.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Group == "Organization" && row.TenantId == 0 && row.IsActive)
            .OrderBy(row => row.Id).Select(row => new { id = row.Id, label = row.Label })
            .ToListAsync(cancellationToken);
        return Ok(organizations);
    }
}
