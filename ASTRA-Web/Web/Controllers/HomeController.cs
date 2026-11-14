using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Web.Models;
using Services.Personnel;

namespace Web.Controllers
{
    public class HomeController(IOnboardPersonnelService personnel) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            return View(await personnel.GetCountsAsync(cancellationToken));
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
