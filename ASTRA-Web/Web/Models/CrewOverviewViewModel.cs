using System.ComponentModel.DataAnnotations;
using Common.Models;

namespace Web.Models;

public sealed class CrewOverviewViewModel
{
    public bool IsMedicView { get; init; }
    public IReadOnlyList<OnboardMemberSummary> Members { get; init; } = [];
    public IReadOnlyDictionary<string, CrewDashboardModel> Health { get; init; } = new Dictionary<string, CrewDashboardModel>();
    public AddMemberInput NewMember { get; init; } = new();
    public bool OpenAddMember { get; init; }
    public EditMemberInput EditedMember { get; init; } = new();
    public bool OpenEditMember { get; init; }
    public DeleteMemberInput DeletedMember { get; init; } = new();
    public string? DeleteMemberName { get; init; }
    public bool OpenDeleteMember { get; init; }
}

public sealed class EditMemberInput : MemberDetailsInput
{
    [Required]
    public string Id { get; set; } = string.Empty;
}

public sealed class DeleteMemberInput
{
    [Required]
    public string Id { get; set; } = string.Empty;
}

public class MemberDetailsInput
{
    [Required(ErrorMessage = "Enter a first name.")]
    public string FirstName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    [Required(ErrorMessage = "Enter a last name.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a username.")]
    [StringLength(256, ErrorMessage = "Username must be 256 characters or fewer.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a role.")]
    [RegularExpression("^(Crew|Medic)$", ErrorMessage = "Choose Crew or Medic.")]
    public string Role { get; set; } = string.Empty;
}

public sealed class AddMemberInput : MemberDetailsInput
{
}
