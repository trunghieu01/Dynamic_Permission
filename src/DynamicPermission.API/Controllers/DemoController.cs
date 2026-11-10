using DynamicPermission.API.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace DynamicPermission.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class DemoController : ControllerBase
{
    [HttpGet("customers")]
    [HasPermission("customer.view")]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetCustomers()
    {
        return Ok(new[] { "Customer A", "Customer B" });
    }

    [HttpPost("customers")]
    [HasPermission("customer.create")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult CreateCustomer()
    {
        return Ok("Created");
    }

    [HttpDelete("customers/{id:guid}")]
    [HasPermission("customer.delete")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult DeleteCustomer(Guid id)
    {
        return Ok($"Deleted {id}");
    }
}
