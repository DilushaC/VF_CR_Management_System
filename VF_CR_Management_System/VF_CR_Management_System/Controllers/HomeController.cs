using CR_Management_System.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using VF_CR_Management_System.Models;

namespace CR_Management_System.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var model = new DashboardViewModel
            {
                TotalCRs = 24,
                PendingApprovals = 5,
                TotalHolds = 1,
                RejectedCRs = 3
            };

            return View(model);
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
