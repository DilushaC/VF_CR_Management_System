using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VF_CR_Management_System.Business.ChangeRequestHandler;
using VF_CR_Management_System.Business.DivisionHandler;
using VF_CR_Management_System.Business.ModuleHandler;
using VF_CR_Management_System.Business.UserHandler;

namespace VF_CR_Management_System.Controllers
{
    public class CRManagementController : Controller
    {
        private readonly IChangeRequestService _changeRequestService;
        private readonly IUserService _userService;
        private readonly IModuleService _moduleService;
        private readonly IDivisionService _divisionService;

        public CRManagementController(IChangeRequestService changeRequestService, IUserService userService, IModuleService moduleService, IDivisionService divisionService)
        {
            _changeRequestService = changeRequestService;
            _userService = userService;
            _moduleService = moduleService;
            _divisionService = divisionService;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var users = await _userService.GetAllUsersAsync();
            ViewBag.Users = users;

            var modules = await _moduleService.GetAllModulesAsync();
            ViewBag.Modules = modules;

            var divisions = await _divisionService.GetAllDivisionsAsync();
            ViewBag.Divisions = divisions;

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(IFormCollection collection)
        {
            try
            {
                var empNo = HttpContext.Session.GetString("EmpNo");
                var newCrId = await _changeRequestService.CreateChangeRequestAsync(collection, empNo);

                if (newCrId <= 0)
                    return Json(new { success = false, message = "Failed to save the Change Request." });

                return Json(new
                {
                    success = true,
                    message = "Saved successfully.",
                    crid = newCrId,
                    redirectUrl = Url.Action("DraftTable")
                });
            }
            catch (ArgumentException ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var users = await _userService.GetAllUsersAsync();
            ViewBag.Users = users;

            var divisions = await _divisionService.GetAllDivisionsAsync();
            ViewBag.Divisions = divisions;

            var modules = await _moduleService.GetAllModulesAsync();
            ViewBag.Modules = modules;

            ViewBag.CRID = id;

            // Reuses the Create view (no model passed — the view reads ViewBag.CRID and
            // fetches the rest via AJAX).
            return View("Create");
        }

        [HttpGet]
        public async Task<IActionResult> GetChangeRequestData(int id)
        {
            var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id);
            if (changeRequest == null)
            {
                return NotFound();
            }

            var approverUserName = await _changeRequestService.GetAssignedApproverUserNameAsync(id);

            // Shape matches populateFormFromData() in Create.cshtml exactly — field names
            // are already camelCase here, so no ASP.NET Core JSON casing surprises.
            return Json(new
            {
                crid = changeRequest.CRID,
                summary = changeRequest.Summary,
                changeTitle = changeRequest.ChangeTitle,
                changeTypeID = changeRequest.ChangeTypeID,
                otherType = changeRequest.OtherChangeType,
                priorityID = changeRequest.PriorityID,
                divisionID = changeRequest.DivisionID,
                moduleID = changeRequest.ModuleID,
                statusID = changeRequest.StatusID,
                approverUserName = approverUserName
            });
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, IFormCollection collection)
        {
            try
            {
                var userName = HttpContext.Session.GetString("UserName");
                var empNo = HttpContext.Session.GetString("EmpNo");

                bool updated = await _changeRequestService.UpdateChangeRequestAsync(id, collection, userName, empNo);
                if (updated)
                {
                    return Json(new
                    {
                        success = true,
                        message = "Change Request updated successfully",
                        redirectUrl = Url.Action("DraftTable", "CRManagement")
                    });
                }
                else
                {
                    return Json(new
                    {
                        success = false,
                        message = "Failed to update Change Request"
                    });
                }
            }
            catch (Exception ex)
            {
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
        public async Task<IActionResult> Submit(int id)
        {
            try
            {
                var empId = User?.Identity?.Name ?? string.Empty;
                var success = await _changeRequestService.SubmitChangeRequestAsync(id, empId);

                if (!success)
                    return BadRequest(new { message = "Failed to submit the Change Request." });

                return Ok(new { message = "Change Request submitted." });
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "An unexpected error occurred while submitting the CR." });
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
        public async Task<IActionResult> GetModulesByDivision(int divisionId)
        {
            if (divisionId <= 0)
            {
                return Json(new List<object>());
            }

            var modules = await _moduleService.GetModulesByDivisionAsync(divisionId);

            return Json(modules.Select(m => new
            {
                id = m.Id,
                name = m.ModuleName
            }));
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

        //[HttpGet]
        //public async Task<IActionResult> Index(string filter = "all")
        //{
        //    var users = await _userService.GetAllUsersAsync();
        //    ViewBag.Users = users;
        //    var empNo = HttpContext.Session.GetString("EmpNo");
        //    var changeRequests = await _changeRequestService.GetAllChangeRequestsAsync(empNo, filter);
        //    ViewBag.CurrentFilter = filter;
        //    return View(changeRequests);
        //}

        public async Task<IActionResult> DraftTable()
        {
            var empNo = HttpContext.Session.GetString("EmpNo");
            var crs = await _changeRequestService.GetAllChangeRequestsDraftsAsync(empNo);
            return View(crs);
        }

        public async Task<IActionResult> SubmissionTable()
        {
            var empNo = HttpContext.Session.GetString("EmpNo");

            ViewBag.CurrentEmpNo = empNo;
            var crs = await _changeRequestService.GetAllChangeRequestsSubmissionsAsync(empNo);
            return View(crs);
        }
    }
}
