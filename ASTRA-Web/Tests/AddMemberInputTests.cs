using System.ComponentModel.DataAnnotations;
using Web.Models;

namespace Tests;

public class AddMemberInputTests
{
    [Theory]
    [InlineData("FirstName", "")]
    [InlineData("LastName", " ")]
    [InlineData("Username", "")]
    [InlineData("Role", "")]
    [InlineData("Role", "GroundControl")]
    public void InvalidRequiredFieldsAreRejected(string field, string value)
    {
        var input = ValidInput();
        typeof(AddMemberInput).GetProperty(field)!.SetValue(input, value);
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), errors, validateAllProperties: true));
        Assert.Contains(errors, error => error.MemberNames.Contains(field));
    }

    [Theory]
    [InlineData("Crew")]
    [InlineData("Medic")]
    public void MiddleNameIsOptionalForBothRoles(string role)
    {
        var input = ValidInput();
        input.Role = role;
        Assert.True(Validator.TryValidateObject(input, new ValidationContext(input), [], validateAllProperties: true));
        Assert.Null(input.MiddleName);
    }

    private static AddMemberInput ValidInput() => new()
    {
        FirstName = "Alex", LastName = "Morgan", Username = "alex", Role = "Crew"
    };
}
