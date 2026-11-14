using System.Security.Cryptography;
using System.Text;
using Data;
using Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Services.Identity
{
    public sealed class PinLookupService(IConfiguration configuration)
    {
        public static bool IsPin(string value) => value.Length == 6 && value.All(c => c is >= '0' and <= '9');

        public string Compute(string pin)
        {
            var key = configuration["Identity:PinLookupKey"];
            if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
                throw new InvalidOperationException("Set Identity:PinLookupKey to a stable secret of at least 32 bytes before provisioning PIN accounts.");
            return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(pin)));
        }
    }

    public sealed class AccountPasswordValidator(ApplicationDbContext db, PinLookupService pins)
        : IPasswordValidator<ApplicationUser>
    {
        private readonly PasswordValidator<ApplicationUser> groundValidator = new();

        public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager,
            ApplicationUser user, string? password)
        {
            if (user.IsGroundControl)
                return await groundValidator.ValidateAsync(manager, user, password);

            if (password is null || !PinLookupService.IsPin(password))
                return IdentityResult.Failed(new IdentityError { Code = "InvalidPin", Description = "The password must be exactly six digits (0–9)." });

            var lookup = pins.Compute(password);
            if (await db.Users.AnyAsync(other => other.Id != user.Id && other.PinLookup == lookup))
                return IdentityResult.Failed(new IdentityError { Code = "PinUnavailable", Description = "This PIN is unavailable. Choose another six-digit PIN." });

            return IdentityResult.Success;
        }
    }

    public sealed class AccountPasswordHasher(PinLookupService pins, PinProtectionService protection) : PasswordHasher<ApplicationUser>
    {
        public override string HashPassword(ApplicationUser user, string password)
        {
            user.PinLookup = user.IsGroundControl ? null : pins.Compute(password);
            user.ProtectedPin = user.IsGroundControl ? null : protection.Protect(user.Id, password);
            return base.HashPassword(user, password);
        }
    }

    public sealed class PinProtectionService(IDataProtectionProvider provider)
    {
        private IDataProtector ForUser(string userId) => provider.CreateProtector("ASTRA.MemberPin.v1", userId);
        public string Protect(string userId, string pin) => ForUser(userId).Protect(pin);
        public string Reveal(string userId, string protectedPin) => ForUser(userId).Unprotect(protectedPin);
    }
}
