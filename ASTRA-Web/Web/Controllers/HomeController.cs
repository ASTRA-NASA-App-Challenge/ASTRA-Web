using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Web.Models;
using Services.Personnel;
using Common.Identity;

namespace Web.Controllers
{
    public class HomeController(IOnboardPersonnelService personnel, Microsoft.AspNetCore.Identity.UserManager<Entities.Identity.ApplicationUser> users,
        Services.Identity.OnboardMemberService members, Web.Monitoring.MonitoringClient monitoring) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            if (User.Identity?.IsAuthenticated == true && User.IsInRole(AppRoles.GroundControl))
                return RedirectToAction("Index", "Crew");

            if (User.Identity?.IsAuthenticated == true
                && (User.IsInRole(AppRoles.Crew) || User.IsInRole(AppRoles.Medic)))
                return RedirectToAction(nameof(Dashboard));

            return View(await personnel.GetCountsAsync(cancellationToken));
        }

        [Microsoft.AspNetCore.Authorization.Authorize(Roles = AppRoles.Crew + "," + AppRoles.Medic)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Dashboard(CancellationToken cancellationToken, int days = 7)
        {
            var user = await users.GetUserAsync(User);
            if (user is null) return Challenge();
            days = days == 30 ? 30 : 7;
            string? pin = null;
            string? error = null;
            var record = new Web.Monitoring.MonitoringRecord([], []);
            try
            {
                pin = await members.GetPinAsync(User, user.Id);
                if (pin is null) error = "Your monitoring identity is unavailable. Contact ground control.";
                else record = await monitoring.ReadWindowAsync(pin, cancellationToken, days);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException)
            { error = "Monitoring is unavailable. Readings could not be loaded. Please retry or contact the onboard medic."; }
            return View(new CrewDashboardModel { Days = days, Name = string.Join(" ", new[] { user.FirstName, user.MiddleName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), Pin = pin, Record = record, Error = error });
        }

        [Microsoft.AspNetCore.Authorization.Authorize(Roles = AppRoles.Crew + "," + AppRoles.Medic)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> DashboardPhoto(string id, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 200) return BadRequest();
            var user = await users.GetUserAsync(User);
            if (user is null) return Challenge();
            try
            {
                var pin = await members.GetPinAsync(User, user.Id);
                if (pin is null) return NotFound();
                var photo = await monitoring.PhotoAsync(pin, id, cancellationToken);
                return photo is null ? NotFound() : File(photo, "image/jpeg");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Security.Cryptography.CryptographicException)
            { return NotFound(); }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}


