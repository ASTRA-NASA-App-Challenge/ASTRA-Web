using System.Security.Claims;
using Common.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Web.Identity;

public static class MedicalPolicies
{
    public const string ReadRecord = "ReadMedicalRecord";
}

// The resource is the Identity user ID of the patient whose record is requested.
public sealed class ReadMedicalRecordRequirement : IAuthorizationRequirement;

public sealed class MedicalRecordAuthorizationHandler
    : AuthorizationHandler<ReadMedicalRecordRequirement, string>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        ReadMedicalRecordRequirement requirement, string patientUserId)
    {
        if (context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(patientUserId)
            && (context.User.IsInRole(AppRoles.Medic)
                || context.User.IsInRole(AppRoles.GroundControl)
                || (context.User.IsInRole(AppRoles.Crew)
                    && context.User.FindFirstValue(ClaimTypes.NameIdentifier) == patientUserId)))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
