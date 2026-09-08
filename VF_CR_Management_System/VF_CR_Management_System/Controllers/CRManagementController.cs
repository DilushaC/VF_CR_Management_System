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

        [HttpPost]
        public async Task<IActionResult> Approve(int id, int ApproverID)
        {
            try
            {
                var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty; // adjust to however you resolve the logged-in EmpId
                var success = await _changeRequestService.ApproveChangeRequestAsync(id, ApproverID, empNo);

                if (!success)
                    return BadRequest(new { message = "Failed to approve the Change Request." });

                return Ok(new { message = "Change Request approved successfully." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "An unexpected error occurred while approving the CR." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Reject(int id, string RejectReason)
        {
            try
            {
                var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;
                var success = await _changeRequestService.RejectChangeRequestAsync(id, RejectReason, empNo);

                if (!success)
                    return BadRequest(new { message = "Failed to reject the Change Request." });

                return Ok(new { message = "Change Request rejected." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "An unexpected error occurred while rejecting the CR." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;
                var success = await _changeRequestService.DeleteChangeRequestAsync(id, empNo);

                if (!success)
                    return BadRequest(new { message = "Failed to delete the Change Request." });

                return Ok(new { message = "Change Request deleted." });
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "An unexpected error occurred while deleting the CR." });
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
        public async Task<IActionResult> Index(string filter = "all")
        {
            var users = await _userService.GetAllUsersAsync();
            ViewBag.Users = users;
            var empNo = HttpContext.Session.GetString("EmpNo");
            var changeRequests = await _changeRequestService.GetAllChangeRequestsAsync(empNo, filter);
            ViewBag.CurrentFilter = filter;
            return View(changeRequests);
        }
    }
}
