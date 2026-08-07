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

        public IActionResult Assesment()
        {
            return View();
        }

        public IActionResult AssesmentSecurity()
        {
            return View();
        }

        public IActionResult Testing()
        {
            return View();
        }

        public IActionResult ReleaseDeployment()
        {
            return View();
        }

        public IActionResult ReviewClosure()
        {
            return View();
        }
    }
}
