using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VF_CR_Management_System.Business.ChangeRequestHandler;
using VF_CR_Management_System.Business.ModuleHandler;
using VF_CR_Management_System.Business.UserHandler;

namespace VF_CR_Management_System.Controllers
{
    public class CRManagementController : Controller
    {
        private readonly IChangeRequestService _changeRequestService;
        private readonly IUserService _userService;
        private readonly IModuleService _moduleService;

        public CRManagementController(IChangeRequestService changeRequestService,IUserService userService, IModuleService moduleService)
        {
            _changeRequestService = changeRequestService;
            _userService = userService;
            _moduleService = moduleService;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var users = await _userService.GetAllUsersAsync();
            ViewBag.Users = users;

            var modules = await _moduleService.GetAllModulesAsync();
            ViewBag.Modules = modules;

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(IFormCollection collection)
        {
            try
            {
                var userName = HttpContext.Session.GetString("UserName");
                var empNo = HttpContext.Session.GetString("EmpNo");

                bool created = await _changeRequestService.CreateChangeRequestAsync(collection, userName, empNo);
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
        public async Task<IActionResult> Index()
        {
            var changeRequests = await _changeRequestService.GetAllChangeRequestsAsync();
            return View(changeRequests);
        }
    }
}
