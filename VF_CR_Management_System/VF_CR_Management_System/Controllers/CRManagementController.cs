using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VF_CR_Management_System.Business.ChangeRequestHandler;

namespace VF_CR_Management_System.Controllers
{
    public class CRManagementController : Controller
    {
        private readonly IChangeRequestService _changeRequestService;

        public CRManagementController(IChangeRequestService changeRequestService)
        {
            _changeRequestService = changeRequestService;
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(IFormCollection collection)
        {
            try
            {
                var userName = HttpContext.Session.GetString("UserName");

                bool created = await _changeRequestService.CreateChangeRequestAsync(collection, userName);
                if (created)
                {
                    return Json(new
                    {
                        success = true,
                        message = "Change Request created successfully",
                        redirectUrl = Url.Action("Index", "CRManagement")
                    });
                }
                else
                {
                    return Json(new
                    {
                        success = false,
                        message = "Failed to create Change Request"
                    });
                }
            }
            catch (Exception ex)
            {
                // Return error response
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult Assesment()
        {
            return View();
        }

        [HttpGet]
        public IActionResult AssesmentSecurity()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Testing()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ReleaseDeployment()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ReviewClosure()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }
    }
}
