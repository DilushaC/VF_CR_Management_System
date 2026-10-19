using CR_Management_System.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using VF_CR_Management_System.Business.ChangeRequestHandler;
using VF_CR_Management_System.Models;

namespace CR_Management_System.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IChangeRequestService _changeRequestService;

        public HomeController(ILogger<HomeController> logger, IChangeRequestService changeRequestService)
        {
            _logger = logger;
            _changeRequestService = changeRequestService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                var metrics = await _changeRequestService.GetDashboardMetricsAsync();

                var model = new DashboardViewModel
                {
                    TotalCRs = metrics.TotalCRs,
                    PendingApprovals = metrics.ApprovedCRs,
                    TotalHolds = metrics.DeployedCRs,
                    RejectedCRs = metrics.RejectedCRs
                };

                _logger.LogInformation(
                    "Action: Index (Dashboard) | Total: {Total} | Approved: {Approved} | Deployed: {Deployed} | Rejected: {Rejected} | Time: {Timestamp}",
                    model.TotalCRs, model.PendingApprovals, model.TotalHolds, model.RejectedCRs, DateTime.Now);

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: Index (Dashboard) (error) | Time: {Timestamp}", DateTime.Now);

                return View(new DashboardViewModel
                {
                    TotalCRs = 0,
                    PendingApprovals = 0,
                    TotalHolds = 0,
                    RejectedCRs = 0
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSubmissionTrend(string period)
        {
            try
            {
                var trend = await _changeRequestService.GetSubmissionTrendAsync(period);

                return Json(new
                {
                    success = true,
                    labels = trend.Select(t => t.Label),
                    counts = trend.Select(t => t.Count)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: GetSubmissionTrend (error) | Period: {Period} | Time: {Timestamp}", period, DateTime.Now);
                return Json(new { success = false, message = "Failed to load submission trend." });
            }
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