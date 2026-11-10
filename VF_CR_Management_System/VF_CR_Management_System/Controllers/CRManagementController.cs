using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using Rotativa.AspNetCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.ChangeRequestHandler;
using VF_CR_Management_System.Business.DivisionHandler;
using VF_CR_Management_System.Business.ModuleHandler;
using VF_CR_Management_System.Business.UserHandler;
using VF_CR_Management_System.Business.VendorHandler;
using VF_CR_Management_System.Data.Models;
using VF_CR_Management_System.Presentation.Filters;
using Attachment = VF_CR_Management_System.Data.Models.Attachment;

namespace VF_CR_Management_System.Controllers
{
    [SessionCheck]
    public class CRManagementController : Controller
    {
        private static class SessionKeys
        {
            public const string EmpNo = "EmpNo";
            public const string UserName = "UserName";
        }

        private readonly IChangeRequestService _changeRequestService;
        private readonly IUserService _userService;
        private readonly IModuleService _moduleService;
        private readonly IDivisionService _divisionService;
        private readonly IVendorService _vendorService;
        private readonly ILogger<CRManagementController> _logger;

        public CRManagementController(
            IChangeRequestService changeRequestService,
            IUserService userService,
            IModuleService moduleService,
            IDivisionService divisionService,
            IVendorService vendorService,
            ILogger<CRManagementController> logger)
        {
            _changeRequestService = changeRequestService ?? throw new ArgumentNullException(nameof(changeRequestService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _moduleService = moduleService ?? throw new ArgumentNullException(nameof(moduleService));
            _divisionService = divisionService ?? throw new ArgumentNullException(nameof(divisionService));
            _vendorService = vendorService ?? throw new ArgumentNullException(nameof(vendorService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private string CurrentEmpNo => HttpContext.Session.GetString(SessionKeys.EmpNo) ?? string.Empty;

        private string CurrentUserName => HttpContext.Session.GetString(SessionKeys.UserName) ?? string.Empty;

        private static bool IsValidId(int id) => id > 0;

        private IActionResult HandleUnexpected(Exception ex, string action, object? id = null)
        {
            _logger.LogError(ex,
                "Action: {Action} (error) | CR/Id: {Id} | User: {EmpNo} | Time: {Timestamp}",
                action, id, CurrentEmpNo, DateTime.Now);

            return StatusCode(500, new
            {
                success = false,
                message = "An unexpected error occurred. Please try again or contact support if the problem persists."
            });
        }

        private IActionResult HandleUnexpectedView(Exception ex, string action, object? id = null)
        {
            _logger.LogError(ex,
                "Action: {Action} (error) | CR/Id: {Id} | User: {EmpNo} | Time: {Timestamp}",
                action, id, CurrentEmpNo, DateTime.Now);

            return View("Error");
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            _logger.LogInformation("Action: Create (GET) | User: {EmpNo} | Time: {Timestamp}", CurrentEmpNo, DateTime.Now);

            try
            {
                ViewBag.Users = await _userService.GetAllUsersAsync();
                ViewBag.Modules = await _moduleService.GetAllModulesAsync();
                ViewBag.Divisions = await _divisionService.GetAllDivisionsAsync();

                return View();
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(Create));
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create(IFormCollection collection)
        {
            if (collection == null)
                return Json(new { success = false, message = "Missing form data." });

            try
            {
                var newCrId = await _changeRequestService.CreateChangeRequestAsync(collection, CurrentEmpNo);

                if (newCrId <= 0)
                {
                    _logger.LogWarning("Action: Create (failed) | User: {EmpNo} | Time: {Timestamp}", CurrentEmpNo, DateTime.Now);
                    return Json(new { success = false, message = "Failed to save the Change Request." });
                }

                _logger.LogInformation("Action: Create | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", newCrId, CurrentEmpNo, DateTime.Now);

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
                _logger.LogWarning(ex, "Action: Create (validation error) | User: {EmpNo} | Time: {Timestamp}", CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(Create));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (!IsValidId(id))
                return BadRequest("A valid Change Request id is required.");

            _logger.LogInformation("Action: Edit (GET) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);

            try
            {
                ViewBag.Users = await _userService.GetAllUsersAsync();
                ViewBag.Divisions = await _divisionService.GetAllDivisionsAsync();
                ViewBag.Modules = await _moduleService.GetAllModulesAsync();
                ViewBag.CRID = id;

                return View("Create");
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(Edit), id);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetChangeRequestData(int id)
        {
            if (!IsValidId(id))
                return BadRequest("A valid Change Request id is required.");

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: GetChangeRequestData (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound();
                }

                var approverUserName = await _changeRequestService.GetAssignedApproverUserNameAsync(id);

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
                    approverUserName = approverUserName,
                    fixedAssets = changeRequest.FixedAssets,
                    activitiesTasks = changeRequest.ActivitiesTasks
                });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(GetChangeRequestData), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, IFormCollection collection)
        {
            if (!IsValidId(id))
                return Json(new { success = false, message = "A valid Change Request id is required." });

            try
            {
                bool updated = await _changeRequestService.UpdateChangeRequestAsync(id, collection, CurrentUserName, CurrentEmpNo);
                if (updated)
                {
                    _logger.LogInformation("Action: Edit | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return Json(new
                    {
                        success = true,
                        message = "Change Request updated successfully",
                        redirectUrl = Url.Action("DraftTable", "CRManagement")
                    });
                }

                _logger.LogWarning("Action: Edit (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = "Failed to update Change Request" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: Edit (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(Edit), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Approve(int id, int ApproverID, int qaUserId)
        {
            if (!IsValidId(id))
                return BadRequest(new { success = false, message = "A valid Change Request id is required." });

            try
            {
                var success = await _changeRequestService.ApproveChangeRequestAsync(id, ApproverID, qaUserId, CurrentEmpNo);

                if (!success)
                {
                    _logger.LogWarning("Action: Approve (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to approve the Change Request." });
                }

                _logger.LogInformation("Action: Approve | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { success = true, message = "Change Request approved successfully." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: Approve (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(Approve), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Reject(int id, string RejectReason)
        {
            if (!IsValidId(id))
                return BadRequest(new { message = "A valid Change Request id is required." });

            if (string.IsNullOrWhiteSpace(RejectReason))
                return BadRequest(new { message = "A rejection reason is required." });

            try
            {
                var success = await _changeRequestService.RejectChangeRequestAsync(id, RejectReason, CurrentEmpNo);

                if (!success)
                {
                    _logger.LogWarning("Action: Reject (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to reject the Change Request." });
                }

                _logger.LogInformation("Action: Reject | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { message = "Change Request rejected." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: Reject (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(Reject), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Submit(int id)
        {
            if (!IsValidId(id))
                return BadRequest(new { message = "A valid Change Request id is required." });

            var empNo = CurrentEmpNo;

            try
            {
                var success = await _changeRequestService.SubmitChangeRequestAsync(id, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: Submit (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to submit the Change Request." });
                }

                _logger.LogInformation("Action: Submit | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { message = "Change Request submitted." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: Submit (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(Submit), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            if (!IsValidId(id))
                return BadRequest(new { message = "A valid Change Request id is required." });

            try
            {
                var success = await _changeRequestService.DeleteChangeRequestAsync(id, CurrentEmpNo);

                if (!success)
                {
                    _logger.LogWarning("Action: Delete (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to delete the Change Request." });
                }

                _logger.LogInformation("Action: Delete | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { message = "Change Request deleted." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: Delete (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(Delete), id);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetModulesByDivision(int divisionId)
        {
            if (divisionId <= 0)
                return Json(new List<object>());

            try
            {
                var modules = await _moduleService.GetModulesByDivisionAsync(divisionId);

                return Json(modules.Select(m => new
                {
                    id = m.Id,
                    name = m.ModuleName
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: GetModulesByDivision (error) | Division: {DivisionId} | User: {EmpNo} | Time: {Timestamp}", divisionId, CurrentEmpNo, DateTime.Now);
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Assessment(int? id)
        {
            if (id == null)
                return RedirectToAction("Index");

            if (!IsValidId(id.Value))
                return BadRequest("A valid Change Request id is required.");

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: Assessment (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound();
                }

                ViewBag.Vendors = await _vendorService.GetAllVendorsAsync();
                ViewBag.ApproverUserName = await _changeRequestService.GetAssignedApproverUserNameAsync(id.Value);

                ViewBag.EffortEstimateDays = changeRequest.DueDate.HasValue
                    ? Math.Max(0, (changeRequest.DueDate.Value.Date - DateTime.Now.Date).TotalDays)
                    : 0;

                ViewBag.Attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id.Value);
                ViewBag.Users = await _userService.GetAllUsersAsync();

                _logger.LogInformation("Action: Assessment (GET) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return View("Assessment", changeRequest);
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(Assessment), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateAssessment(IFormCollection collection)
        {
            if (collection == null || !int.TryParse(collection["id"], out int id) || !IsValidId(id))
            {
                _logger.LogWarning("Action: CreateAssessment (invalid ID) | User: {EmpNo} | Time: {Timestamp}", CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = "Invalid or missing ID in form payload." });
            }

            try
            {
                bool updated = await _changeRequestService.CreateAssessmentAsync(id, collection, CurrentUserName, CurrentEmpNo);
                if (updated)
                {
                    _logger.LogInformation("Action: CreateAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return Json(new
                    {
                        success = true,
                        message = "Assessment updated successfully",
                        redirectUrl = Url.Action("AssignedToMeTable", "CRManagement")
                    });
                }

                _logger.LogWarning("Action: CreateAssessment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = "Failed to update Change Request" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: CreateAssessment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(CreateAssessment), id);
            }
        }

        [HttpGet]
        public async Task<IActionResult> AssesmentSecurity(int? id)
        {
            if (id == null)
                return RedirectToAction("Index");

            if (!IsValidId(id.Value))
                return BadRequest("A valid Change Request id is required.");

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: AssesmentSecurity (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound();
                }

                ViewBag.EffortEstimateDays = changeRequest.DueDate.HasValue
                    ? Math.Max(0, (changeRequest.DueDate.Value.Date - DateTime.Now.Date).TotalDays)
                    : 0;

                ViewBag.Attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id.Value);
                ViewBag.Users = await _userService.GetAllUsersAsync();
                ViewBag.ChangeImpacts = await _changeRequestService.GetChangeImpactsAsync();

                _logger.LogInformation("Action: AssesmentSecurity (GET) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return View("AssesmentSecurity", changeRequest);
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(AssesmentSecurity), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateSecurityAssessment(IFormCollection collection)
        {
            if (collection == null || !int.TryParse(collection["id"], out int id) || !IsValidId(id))
            {
                _logger.LogWarning("Action: CreateSecurityAssessment (invalid ID) | User: {EmpNo} | Time: {Timestamp}", CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = "Invalid or missing ID in form payload." });
            }

            try
            {
                bool isSubmit = collection["Status"].ToString() == "2";

                bool updated = await _changeRequestService.CreateAssessmentSecurityAsync(id, collection, CurrentUserName, CurrentEmpNo);
                if (updated)
                {
                    _logger.LogInformation("Action: CreateSecurityAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return Json(new
                    {
                        success = true,
                        message = isSubmit
                            ? "Security assessment submitted successfully"
                            : "Security assessment saved successfully",
                        redirectUrl = Url.Action("AssignedToMeTable", "CRManagement")
                    });
                }

                _logger.LogWarning("Action: CreateSecurityAssessment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = "Failed to update Change Request" });
            }
            catch (ArgumentException ex)
            {
                // Validation messages from the service are shown as-is
                _logger.LogWarning(ex, "Action: CreateSecurityAssessment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(CreateSecurityAssessment), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> RejectSecurityAssessment(IFormCollection collection)
        {
            if (collection == null || !int.TryParse(collection["id"], out int id) || !IsValidId(id))
            {
                _logger.LogWarning("Action: RejectSecurityAssessment (invalid ID) | User: {EmpNo} | Time: {Timestamp}", CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = "Invalid or missing ID in form payload." });
            }

            try
            {
                bool rejected = await _changeRequestService.RejectAssessmentSecurityAsync(id, collection, CurrentUserName, CurrentEmpNo);
                if (rejected)
                {
                    _logger.LogInformation("Action: RejectSecurityAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return Json(new
                    {
                        success = true,
                        message = "Security assessment rejected and returned to the developer.",
                        redirectUrl = Url.Action("AssignedToMeTable", "CRManagement")
                    });
                }

                _logger.LogWarning("Action: RejectSecurityAssessment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = "Failed to reject Change Request" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: RejectSecurityAssessment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(RejectSecurityAssessment), id);
            }
        }

        [HttpGet]
        public IActionResult ReleaseDeployment() => View();

        [HttpGet]
        public IActionResult ReviewClosure() => View();

        public async Task<IActionResult> DraftTable()
        {
            try
            {
                var crs = await _changeRequestService.GetAllChangeRequestsDraftsAsync(CurrentEmpNo);
                return View(crs);
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(DraftTable));
            }
        }

        public async Task<IActionResult> SubmissionTable()
        {
            try
            {
                ViewBag.CurrentEmpNo = CurrentEmpNo;
                var crs = await _changeRequestService.GetAllChangeRequestsSubmissionsAsync(CurrentEmpNo);
                return View(crs);
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(SubmissionTable));
            }
        }

        public async Task<IActionResult> RejectionsTable()
        {
            try
            {
                ViewBag.CurrentEmpNo = CurrentEmpNo;
                var crs = await _changeRequestService.GetAllChangeRequestsRejectionsAsync(CurrentEmpNo);
                return View(crs);
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(RejectionsTable));
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditAssessment(int? id)
        {
            if (id == null)
                return RedirectToAction("AssignedToMeTable");

            if (!IsValidId(id.Value))
                return BadRequest("A valid Change Request id is required.");

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: EditAssessment (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound();
                }

                ViewBag.Vendors = await _vendorService.GetAllVendorsAsync();
                ViewBag.ApproverUserName = await _changeRequestService.GetAssignedApproverUserNameAsync(id.Value);

                ViewBag.EffortEstimateDays = changeRequest.DueDate.HasValue
                    ? Math.Max(0, (changeRequest.DueDate.Value.Date - DateTime.Now.Date).TotalDays)
                    : 0;

                ViewBag.Attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id.Value);
                ViewBag.Users = await _userService.GetAllUsersAsync();

                _logger.LogInformation("Action: EditAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return View("Assessment", changeRequest);
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(EditAssessment), id);
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadAttachment(int id)
        {
            if (!IsValidId(id))
                return BadRequest("A valid attachment id is required.");

            try
            {
                var attachment = await _changeRequestService.GetAttachmentByIdAsync(id);

                if (attachment == null)
                {
                    _logger.LogWarning("Action: DownloadAttachment (not found) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound();
                }

                if (!System.IO.File.Exists(attachment.FilePath))
                {
                    _logger.LogWarning("Action: DownloadAttachment (file missing) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound("The file could not be found on the server.");
                }

                var provider = new FileExtensionContentTypeProvider();
                if (!provider.TryGetContentType(attachment.FilePath, out var contentType))
                    contentType = "application/octet-stream";

                var fileBytes = await System.IO.File.ReadAllBytesAsync(attachment.FilePath);

                _logger.LogInformation("Action: DownloadAttachment | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return File(fileBytes, contentType, attachment.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: DownloadAttachment (error) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return StatusCode(500, "An error occurred while downloading the file.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            if (!IsValidId(id))
                return Json(new { success = false, message = "A valid attachment id is required." });

            try
            {
                var success = await _changeRequestService.DeleteAttachmentAsync(id, CurrentUserName);

                if (!success)
                {
                    _logger.LogWarning("Action: DeleteAttachment (not found) | Attachment: {AttachmentId} | User: {UserName} | Time: {Timestamp}", id, CurrentUserName, DateTime.Now);
                    return Json(new { success = false, message = "Attachment not found or already removed." });
                }

                _logger.LogInformation("Action: DeleteAttachment | Attachment: {AttachmentId} | User: {UserName} | Time: {Timestamp}", id, CurrentUserName, DateTime.Now);
                return Json(new { success = true, message = "Attachment removed." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: DeleteAttachment (error) | Attachment: {AttachmentId} | User: {UserName} | Time: {Timestamp}", id, CurrentUserName, DateTime.Now);
                return Json(new { success = false, message = "An unexpected error occurred while removing the attachment." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PreviewAttachment(int id)
        {
            if (!IsValidId(id))
                return BadRequest("A valid attachment id is required.");

            try
            {
                var attachment = await _changeRequestService.GetAttachmentByIdAsync(id);

                if (attachment == null)
                {
                    _logger.LogWarning("Action: PreviewAttachment (not found) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound();
                }

                if (!System.IO.File.Exists(attachment.FilePath))
                {
                    _logger.LogWarning("Action: PreviewAttachment (file missing) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound("The file could not be found on the server.");
                }

                var previewableExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".pdf" };
                var extension = Path.GetExtension(attachment.FilePath).ToLowerInvariant();

                if (!previewableExtensions.Contains(extension))
                {
                    _logger.LogWarning("Action: PreviewAttachment (unsupported type) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest("Preview is only available for image or PDF files.");
                }

                var provider = new FileExtensionContentTypeProvider();
                if (!provider.TryGetContentType(attachment.FilePath, out var contentType))
                    contentType = "application/octet-stream";

                var fileBytes = await System.IO.File.ReadAllBytesAsync(attachment.FilePath);

                _logger.LogInformation("Action: PreviewAttachment | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);

                return File(fileBytes, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: PreviewAttachment (error) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return StatusCode(500, "An error occurred while loading the preview.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditSecurity(int? id)
        {
            if (id == null)
                return RedirectToAction("SecurityTable");

            if (!IsValidId(id.Value))
                return BadRequest("A valid Change Request id is required.");

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: EditSecurity (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound();
                }

                ViewBag.ApproverUserName = await _changeRequestService.GetAssignedApproverUserNameAsync(id.Value);
                ViewBag.Attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id.Value);
                ViewBag.Users = await _userService.GetAllUsersAsync();
                ViewBag.ChangeImpacts = await _changeRequestService.GetChangeImpactsAsync();

                _logger.LogInformation("Action: EditSecurity | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return View("AssesmentSecurity", changeRequest);
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(EditSecurity), id);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Testing(int? id)
        {
            if (id == null)
                return RedirectToAction("Index");

            if (!IsValidId(id.Value))
                return BadRequest("A valid Change Request id is required.");

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: Testing (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound();
                }

                ViewBag.Attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id.Value);
                ViewBag.Users = await _userService.GetAllUsersAsync();
                ViewBag.ChangeImpacts = await _changeRequestService.GetChangeImpactsAsync();

                _logger.LogInformation("Action: Testing (GET) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return View("Testing", changeRequest);
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(Testing), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateTesting(IFormCollection collection)
        {
            if (collection == null || !int.TryParse(collection["Id"], out int id) || !IsValidId(id))
            {
                _logger.LogWarning("Action: CreateTesting (invalid ID) | User: {EmpNo} | Time: {Timestamp}", CurrentEmpNo, DateTime.Now);
                return Json(new { success = false, message = "Invalid or missing ID in form payload." });
            }

            var userName = CurrentUserName;
            var empNo = CurrentEmpNo;

            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(empNo))
            {
                _logger.LogWarning("Action: CreateTesting (session expired) | CR: {CRId} | Time: {Timestamp}", id, DateTime.Now);
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            try
            {
                bool isSubmit = collection["Status"].ToString() == "2";

                bool updated = await _changeRequestService.CreateTestingAsync(id, collection, userName, empNo);

                if (updated)
                {
                    _logger.LogInformation("Action: CreateTesting | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return Json(new
                    {
                        success = true,
                        message = isSubmit
                            ? "QA test results submitted successfully"
                            : "QA test saved as draft successfully",
                        redirectUrl = Url.Action("AssignedToMeTable", "CRManagement")
                    });
                }

                _logger.LogWarning("Action: CreateTesting (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Json(new { success = false, message = "Failed to update Change Request" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: CreateTesting (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(CreateTesting), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> AssignFinalApprover(int id, int finalApproverId)
        {
            if (!IsValidId(id))
                return BadRequest(new { message = "A valid Change Request id is required." });

            try
            {
                var success = await _changeRequestService.AssignFinalApproverAsync(id, finalApproverId, CurrentEmpNo);

                if (!success)
                {
                    _logger.LogWarning("Action: AssignFinalApprover (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to approve the Change Request." });
                }

                _logger.LogInformation("Action: AssignFinalApprover | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { message = "Change Request approved successfully." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: AssignFinalApprover (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(AssignFinalApprover), id);
            }
        }

        [HttpGet]
        public async Task<IActionResult> CRDetailsPartial(int id)
        {
            if (!IsValidId(id))
                return BadRequest("A valid Change Request id is required.");

            try
            {
                var cr = await _changeRequestService.GetChangeRequestByIdAsync(id);
                if (cr == null)
                {
                    _logger.LogWarning("Action: CRDetailsPartial (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound();
                }

                var attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id);
                cr.Attachments = attachments?.ToList() ?? new List<Attachment>();

                ViewBag.CurrentEmpNo = CurrentEmpNo;
                ViewBag.CurrentUserName = CurrentEmpNo;
                ViewBag.CurrentDisplayName = CurrentUserName;
                ViewBag.Users = await _userService.GetAllUsersAsync();

                _logger.LogInformation("Action: CRDetailsPartial | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return PartialView("_CRDetailsPartial", cr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: CRDetailsPartial (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return StatusCode(500, "An error occurred while loading the Change Request details.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> AcceptDevAssessment(int id, string securityOfficerId)
        {
            if (!IsValidId(id))
                return BadRequest(new { success = false, message = "A valid Change Request id is required." });

            try
            {
                var success = await _changeRequestService.AcceptDevAssessment(id, CurrentEmpNo, securityOfficerId);

                if (!success)
                {
                    _logger.LogWarning("Action: AcceptDevAssessment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to accept the Change Request." });
                }

                _logger.LogInformation("Action: AcceptDevAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { success = true, message = "Assessment accepted and assigned to the IS Officer." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: AcceptDevAssessment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Action: AcceptDevAssessment (invalid operation) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(AcceptDevAssessment), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> ReturnDevAssessment(int id)
        {
            if (!IsValidId(id))
                return BadRequest(new { success = false, message = "A valid Change Request id is required." });

            try
            {
                var success = await _changeRequestService.ReturnDevAssessment(id, CurrentEmpNo);

                if (!success)
                {
                    _logger.LogWarning("Action: ReturnDevAssessment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to return the Change Request." });
                }

                _logger.LogInformation("Action: ReturnDevAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { success = true, message = "Change Request returned to the developer." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: ReturnDevAssessment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Action: ReturnDevAssessment (invalid operation) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(ReturnDevAssessment), id);
            }
        }

        [HttpGet]
        public async Task<IActionResult> AssignedToMeTable()
        {
            try
            {
                var empNo = CurrentEmpNo;
                ViewBag.CurrentEmpNo = empNo;

                var crs = await _changeRequestService.GetAllChangeRequestsAssignedToMeAsync(empNo);
                var list = await _changeRequestService.GetCrIdsPendingApprovalForUserAsync(empNo);

                foreach (var item in list)
                {
                    var crPendingList = await _changeRequestService.GetCRPendingApprovals(item.CRID);
                    if (crPendingList.Count > 1)
                    {
                        foreach (var cr in crPendingList)
                        {
                            if (item.ApprovalID == cr.ApprovalID && item.StepName == "Testing")
                            {
                                item.IsApproved = true;
                            }
                        }
                    }
                }

                var pendingItems = list.Where(a => a.IsApproved == null).ToList();

                ViewBag.PendingApprovalCrIds = pendingItems.Select(a => a.CRID).ToHashSet();
                ViewBag.PendingStepByCrId = pendingItems
                    .GroupBy(a => a.CRID)
                    .ToDictionary(g => g.Key, g => g.First().StepName);

                return View(crs);
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(AssignedToMeTable));
            }
        }

        [HttpPost]
        public async Task<IActionResult> ApproveToDevelopment(int id)
        {
            if (!IsValidId(id))
                return BadRequest(new { success = false, message = "A valid Change Request id is required." });

            try
            {
                var success = await _changeRequestService.ApproveToDevelopmentAsync(id, CurrentEmpNo);

                if (!success)
                {
                    _logger.LogWarning("Action: ApproveToDevelopment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to approve the Change Request." });
                }

                _logger.LogInformation("Action: ApproveToDevelopment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { success = true, message = "Change Request approved successfully." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: ApproveToDevelopment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(ApproveToDevelopment), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> ProceedToDevelopment(int id, string uatLink)
        {
            if (!IsValidId(id))
                return BadRequest(new { message = "A valid Change Request id is required." });

            try
            {
                var success = await _changeRequestService.ProceedToDevelopmentAsync(id, uatLink, CurrentEmpNo);

                if (!success)
                {
                    _logger.LogWarning("Action: ProceedToDevelopment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to send UAT Link." });
                }

                _logger.LogInformation("Action: ProceedToDevelopment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { message = "UAT handover to QA." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: ProceedToDevelopment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(ProceedToDevelopment), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> ReturnToQA(int id, string reason)
        {
            if (!IsValidId(id))
                return BadRequest(new { success = false, message = "A valid Change Request id is required." });

            try
            {
                var success = await _changeRequestService.ReturnToQAAsync(id, reason, CurrentEmpNo);

                if (!success)
                {
                    _logger.LogWarning("Action: ReturnToQA (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to return the Change Request to QA." });
                }

                _logger.LogInformation("Action: ReturnToQA | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { success = true, message = "Change Request returned to QA." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: ReturnToQA (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Action: ReturnToQA (invalid operation) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(ReturnToQA), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> FinalApproval(int id)
        {
            if (!IsValidId(id))
                return BadRequest(new { success = false, message = "A valid Change Request id is required." });

            try
            {
                var success = await _changeRequestService.FinalApproval(id, CurrentEmpNo);

                if (!success)
                {
                    _logger.LogWarning("Action: FinalApproval (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to approve the CR ." });
                }

                _logger.LogInformation("Action: FinalApproval | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { success = true, message = "CR Final Approve Success." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: FinalApproval (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Action: FinalApproval (invalid operation) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(FinalApproval), id);
            }
        }

        [HttpPost]
        public async Task<IActionResult> SubmitLiveLink(int id, string liveLink)
        {
            if (!IsValidId(id))
                return BadRequest(new { message = "A valid Change Request id is required." });

            try
            {
                var success = await _changeRequestService.SubmitLiveLink(id, liveLink, CurrentEmpNo);

                if (!success)
                {
                    _logger.LogWarning("Action: SubmitLiveLink (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to send LIVE Link." });
                }

                _logger.LogInformation("Action: SubmitLiveLink | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return Ok(new { message = "Live link Deployed" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: SubmitLiveLink (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return HandleUnexpected(ex, nameof(SubmitLiveLink), id);
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadCRForm(int id)
        {
            if (!IsValidId(id))
                return BadRequest("A valid Change Request id is required.");

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id);

                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: DownloadCRForm (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);
                    return NotFound();
                }

                var attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id);
                changeRequest.Attachments = attachments?.ToList() ?? new List<Attachment>();

                ViewBag.Users = await _userService.GetAllUsersAsync();

                var identities = await _changeRequestService.GetCRFormIdentitiesAsync(changeRequest);

                double? effortEstimateDays = changeRequest.DueDate.HasValue
                    ? Math.Max(0, (changeRequest.DueDate.Value.Date - DateTime.Now.Date).TotalDays)
                    : null;

                var viewModel = new ReportViewModel
                {
                    ChangeRequest = changeRequest,
                    Testing = new Testing(),
                    IsPdfExport = true,
                    Requester = identities.Requester,
                    Implementer = identities.Implementer,
                    DeptHeadApprover = identities.DeptHeadApprover,
                    SecurityOfficer = identities.SecurityOfficer,
                    Tester = identities.Tester,
                    EffortEstimateDays = effortEstimateDays
                };

                _logger.LogInformation("Action: DownloadCRForm | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, CurrentEmpNo, DateTime.Now);

                return new Rotativa.AspNetCore.ViewAsPdf("CRFormPrint", viewModel)
                {
                    FileName = $"CR_{changeRequest.CRNumber}.pdf",
                    PageSize = Rotativa.AspNetCore.Options.Size.A4,
                    PageMargins = new Rotativa.AspNetCore.Options.Margins(15, 10, 15, 10),
                    CustomSwitches = "--disable-smart-shrinking --disable-javascript",
                };
            }
            catch (Exception ex)
            {
                return HandleUnexpectedView(ex, nameof(DownloadCRForm), id);
            }
        }
    }
}
