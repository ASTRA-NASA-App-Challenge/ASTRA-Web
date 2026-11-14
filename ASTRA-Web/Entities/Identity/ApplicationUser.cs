using Microsoft.AspNetCore.Identity;

namespace Entities.Identity;

public class ApplicationUser : IdentityUser
{
    public bool IsGroundControl { get; set; }
    public string? PinLookup { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProtectedPin { get; set; }
}
