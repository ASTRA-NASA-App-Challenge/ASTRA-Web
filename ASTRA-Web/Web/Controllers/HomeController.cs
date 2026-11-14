using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Web.Models;
using Services.Personnel;
using Common.Identity;

namespace Web.Controllers
{
    public class HomeController(IOnboardPersonnelService personnel) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            if (User.Identity?.IsAuthenticated == true
                && (User.IsInRole(AppRoles.Crew) || User.IsInRole(AppRoles.Medic)))
                return RedirectToAction(nameof(Dashboard));

            return View(await personnel.GetCountsAsync(cancellationToken));
        }

        [Microsoft.AspNetCore.Authorization.Authorize(Roles = AppRoles.Crew + "," + AppRoles.Medic)]
        public IActionResult Dashboard()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
