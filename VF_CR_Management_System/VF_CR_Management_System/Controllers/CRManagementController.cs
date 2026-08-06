using Microsoft.AspNetCore.Mvc;

namespace CR_Management_System.Presentation.Controllers
{
    public class CRManagementController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Create()
        {
            return View();
        }
    }
}
