using Microsoft.AspNetCore.Identity;

namespace Entities.Identity;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public bool IsGroundControl { get; set; }
    public string? PinLookup { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProtectedPin { get; set; }
}
