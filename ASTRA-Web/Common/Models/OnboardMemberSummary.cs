using Common.Identity;

namespace Common.Models;

public sealed record OnboardMemberSummary(string Id, string Username, string FirstName,
    string? MiddleName, string LastName, string Role, string? Pin)
{
    public string FullName
    {
        get
        {
            var name = string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
            return string.IsNullOrWhiteSpace(name) ? Username : name;
        }
    }

    public string Initials => string.Concat(FirstName.Take(1).Concat(LastName.Take(1))).ToUpperInvariant() is { Length: > 0 } initials ? initials : "?";
    public bool IsMedic => Role == AppRoles.Medic;
}
