using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Rotativa.AspNetCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
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
            _changeRequestService = changeRequestService;
            _userService = userService;
            _moduleService = moduleService;
            _divisionService = divisionService;
            _vendorService = vendorService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;
            _logger.LogInformation("Action: Create (GET) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);

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
            var empNo = HttpContext.Session.GetString("EmpNo");

            try
            {
                var newCrId = await _changeRequestService.CreateChangeRequestAsync(collection, empNo);

                if (newCrId <= 0)
                {
                    _logger.LogWarning("Action: Create (failed) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                    return Json(new { success = false, message = "Failed to save the Change Request." });
                }

                _logger.LogInformation("Action: Create | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", newCrId, empNo, DateTime.Now);

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
                _logger.LogWarning(ex, "Action: Create (validation error) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;
            _logger.LogInformation("Action: Edit (GET) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);

            var users = await _userService.GetAllUsersAsync();
            ViewBag.Users = users;

            var divisions = await _divisionService.GetAllDivisionsAsync();
            ViewBag.Divisions = divisions;

            var modules = await _moduleService.GetAllModulesAsync();
            ViewBag.Modules = modules;

            ViewBag.CRID = id;

            return View("Create");
        }

        [HttpGet]
        public async Task<IActionResult> GetChangeRequestData(int id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;
            _logger.LogInformation("Action: GetChangeRequestData | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);

            var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id);
            if (changeRequest == null)
            {
                _logger.LogWarning("Action: GetChangeRequestData (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
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

        [HttpPost]
        public async Task<IActionResult> Edit(int id, IFormCollection collection)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var empNo = HttpContext.Session.GetString("EmpNo");

            try
            {
                bool updated = await _changeRequestService.UpdateChangeRequestAsync(id, collection, userName, empNo);
                if (updated)
                {
                    _logger.LogInformation("Action: Edit | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return Json(new
                    {
                        success = true,
                        message = "Change Request updated successfully",
                        redirectUrl = Url.Action("DraftTable", "CRManagement")
                    });
                }
                else
                {
                    _logger.LogWarning("Action: Edit (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return Json(new
                    {
                        success = false,
                        message = "Failed to update Change Request"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: Edit (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Approve(int id, int ApproverID, int qaUserId)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.ApproveChangeRequestAsync(id, ApproverID, qaUserId, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: Approve (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to approve the Change Request." });
                }

                _logger.LogInformation("Action: Approve | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { success = true, message = "Change Request approved successfully." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: Approve (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: Approve (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, new { success = false, message = "An unexpected error occurred while approving the CR." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Reject(int id, string RejectReason)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.RejectChangeRequestAsync(id, RejectReason, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: Reject (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to reject the Change Request." });
                }

                _logger.LogInformation("Action: Reject | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { message = "Change Request rejected." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: Reject (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: Reject (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, new { message = "An unexpected error occurred while rejecting the CR." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Submit(int id)
        {
            var empId = User?.Identity?.Name ?? string.Empty;

            try
            {
                var success = await _changeRequestService.SubmitChangeRequestAsync(id, empId);

                if (!success)
                {
                    _logger.LogWarning("Action: Submit (failed) | CR: {CRId} | User: {EmpId} | Time: {Timestamp}", id, empId, DateTime.Now);
                    return BadRequest(new { message = "Failed to submit the Change Request." });
                }

                _logger.LogInformation("Action: Submit | CR: {CRId} | User: {EmpId} | Time: {Timestamp}", id, empId, DateTime.Now);
                return Ok(new { message = "Change Request submitted." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: Submit (error) | CR: {CRId} | User: {EmpId} | Time: {Timestamp}", id, empId, DateTime.Now);
                return StatusCode(500, new { message = "An unexpected error occurred while submitting the CR." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.DeleteChangeRequestAsync(id, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: Delete (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to delete the Change Request." });
                }

                _logger.LogInformation("Action: Delete | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { message = "Change Request deleted." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: Delete (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
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
        public async Task<IActionResult> Assessment(int? id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            if (id == null)
            {
                return RedirectToAction("Index");
            }

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: Assessment (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return NotFound();
                }

                var vendors = await _vendorService.GetAllVendorsAsync();
                ViewBag.Vendors = vendors;

                ViewBag.ApproverUserName = await _changeRequestService.GetAssignedApproverUserNameAsync(id.Value);

                double effortEstimateDays = 0;
                if (changeRequest.DueDate.HasValue)
                {
                    effortEstimateDays = Math.Max(0, (changeRequest.DueDate.Value.Date - DateTime.Now.Date).TotalDays);
                }
                ViewBag.EffortEstimateDays = effortEstimateDays;

                var attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id.Value);
                ViewBag.Attachments = attachments;

                var users = await _userService.GetAllUsersAsync();
                ViewBag.Users = users;

                _logger.LogInformation("Action: Assessment (GET) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return View("Assessment", changeRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: Assessment (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return View("Error");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateAssessment(IFormCollection collection)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var empNo = HttpContext.Session.GetString("EmpNo");

            try
            {
                if (!int.TryParse(collection["id"], out int id))
                {
                    _logger.LogWarning("Action: CreateAssessment (invalid ID) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                    return Json(new
                    {
                        success = false,
                        message = "Invalid or missing ID in form payload."
                    });
                }

                bool updated = await _changeRequestService.CreateAssessmentAsync(id, collection, userName, empNo);
                if (updated)
                {
                    _logger.LogInformation("Action: CreateAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return Json(new
                    {
                        success = true,
                        message = "Assessment updated successfully",
                        redirectUrl = Url.Action("AssignedToMeTable", "CRManagement")
                    });
                }
                else
                {
                    _logger.LogWarning("Action: CreateAssessment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return Json(new
                    {
                        success = false,
                        message = "Failed to update Change Request"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: CreateAssessment (error) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> AssesmentSecurity(int? id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            if (id == null)
            {
                return RedirectToAction("Index");
            }

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: AssesmentSecurity (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return NotFound();
                }

                double effortEstimateDays = 0;
                if (changeRequest.DueDate.HasValue)
                {
                    effortEstimateDays = Math.Max(0, (changeRequest.DueDate.Value.Date - DateTime.Now.Date).TotalDays);
                }
                ViewBag.EffortEstimateDays = effortEstimateDays;

                var attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id.Value);
                ViewBag.Attachments = attachments;

                var users = await _userService.GetAllUsersAsync();
                ViewBag.Users = users;

                ViewBag.ChangeImpacts = await _changeRequestService.GetChangeImpactsAsync();

                _logger.LogInformation("Action: AssesmentSecurity (GET) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return View("AssesmentSecurity", changeRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: AssesmentSecurity (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return View("Error");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateSecurityAssessment(IFormCollection collection)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var empNo = HttpContext.Session.GetString("EmpNo");

            try
            {
                if (!int.TryParse(collection["id"], out int id))
                {
                    _logger.LogWarning("Action: CreateSecurityAssessment (invalid ID) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                    return Json(new
                    {
                        success = false,
                        message = "Invalid or missing ID in form payload."
                    });
                }

                // Status flag posted by the button: 1 = Save (draft), 2 = Submit
                // (required-field validation lives in the service and applies to Submit only)
                bool isSubmit = collection["Status"].ToString() == "2";

                bool updated = await _changeRequestService.CreateAssessmentSecurityAsync(id, collection, userName, empNo);
                if (updated)
                {
                    _logger.LogInformation("Action: CreateSecurityAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return Json(new
                    {
                        success = true,
                        message = isSubmit
                            ? "Security assessment submitted successfully"
                            : "Security assessment saved successfully",
                        redirectUrl = Url.Action("AssignedToMeTable", "CRManagement")
                    });
                }
                else
                {
                    _logger.LogWarning("Action: CreateSecurityAssessment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return Json(new
                    {
                        success = false,
                        message = "Failed to update Change Request"
                    });
                }
            }
            catch (ArgumentException ex)
            {
                // Validation messages from the service are shown as-is
                _logger.LogWarning(ex, "Action: CreateSecurityAssessment (validation error) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: CreateSecurityAssessment (error) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RejectSecurityAssessment(IFormCollection collection)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var empNo = HttpContext.Session.GetString("EmpNo");

            try
            {
                if (!int.TryParse(collection["id"], out int id))
                {
                    _logger.LogWarning("Action: RejectSecurityAssessment (invalid ID) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                    return Json(new
                    {
                        success = false,
                        message = "Invalid or missing ID in form payload."
                    });
                }

                bool rejected = await _changeRequestService.RejectAssessmentSecurityAsync(id, collection, userName, empNo);
                if (rejected)
                {
                    _logger.LogInformation("Action: RejectSecurityAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return Json(new
                    {
                        success = true,
                        message = "Security assessment rejected and returned to the developer.",
                        redirectUrl = Url.Action("AssignedToMeTable", "CRManagement")
                    });
                }
                else
                {
                    _logger.LogWarning("Action: RejectSecurityAssessment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return Json(new
                    {
                        success = false,
                        message = "Failed to reject Change Request"
                    });
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: RejectSecurityAssessment (validation error) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: RejectSecurityAssessment (error) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
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

        public async Task<IActionResult> RejectionsTable()
        {
            var empNo = HttpContext.Session.GetString("EmpNo");
            ViewBag.CurrentEmpNo = empNo;
            var crs = await _changeRequestService.GetAllChangeRequestsRejectionsAsync(empNo);
            return View(crs);
        }


        [HttpGet]
        public async Task<IActionResult> EditAssessment(int? id)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var empNo = HttpContext.Session.GetString("EmpNo");

            if (id == null)
            {
                return RedirectToAction("AssignedToMeTable");
            }

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: EditAssessment (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return NotFound();
                }
                var vendors = await _vendorService.GetAllVendorsAsync();
                ViewBag.Vendors = vendors;

                ViewBag.ApproverUserName = await _changeRequestService.GetAssignedApproverUserNameAsync(id.Value);

                double effortEstimateDays = 0;
                if (changeRequest.DueDate.HasValue)
                {
                    effortEstimateDays = Math.Max(0, (changeRequest.DueDate.Value.Date - DateTime.Now.Date).TotalDays);
                }
                ViewBag.EffortEstimateDays = effortEstimateDays;

                var attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id.Value);
                ViewBag.Attachments = attachments;

                var users = await _userService.GetAllUsersAsync();
                ViewBag.Users = users;

                _logger.LogInformation("Action: EditAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return View("Assessment", changeRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: EditAssessment (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return View("Error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadAttachment(int id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var attachment = await _changeRequestService.GetAttachmentByIdAsync(id);

                if (attachment == null)
                {
                    _logger.LogWarning("Action: DownloadAttachment (not found) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return NotFound();
                }

                if (!System.IO.File.Exists(attachment.FilePath))
                {
                    _logger.LogWarning("Action: DownloadAttachment (file missing) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return NotFound("The file could not be found on the server.");
                }

                var provider = new FileExtensionContentTypeProvider();
                if (!provider.TryGetContentType(attachment.FilePath, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(attachment.FilePath);

                _logger.LogInformation("Action: DownloadAttachment | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return File(fileBytes, contentType, attachment.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: DownloadAttachment (error) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, "An error occurred while downloading the file.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var userName = HttpContext.Session.GetString("UserName");

            try
            {
                var success = await _changeRequestService.DeleteAttachmentAsync(id, userName);

                if (!success)
                {
                    _logger.LogWarning("Action: DeleteAttachment (not found) | Attachment: {AttachmentId} | User: {UserName} | Time: {Timestamp}", id, userName, DateTime.Now);
                    return Json(new { success = false, message = "Attachment not found or already removed." });
                }

                _logger.LogInformation("Action: DeleteAttachment | Attachment: {AttachmentId} | User: {UserName} | Time: {Timestamp}", id, userName, DateTime.Now);
                return Json(new { success = true, message = "Attachment removed." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: DeleteAttachment (error) | Attachment: {AttachmentId} | User: {UserName} | Time: {Timestamp}", id, userName, DateTime.Now);
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PreviewAttachment(int id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var attachment = await _changeRequestService.GetAttachmentByIdAsync(id);

                if (attachment == null)
                {
                    _logger.LogWarning("Action: PreviewAttachment (not found) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return NotFound();
                }

                if (!System.IO.File.Exists(attachment.FilePath))
                {
                    _logger.LogWarning("Action: PreviewAttachment (file missing) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return NotFound("The file could not be found on the server.");
                }

                // Only allow inline preview for images and PDFs — anything else falls
                // back to a normal download rather than being rendered in the browser.
                var previewableExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".pdf" };
                var extension = Path.GetExtension(attachment.FilePath).ToLowerInvariant();

                if (!previewableExtensions.Contains(extension))
                {
                    _logger.LogWarning("Action: PreviewAttachment (unsupported type) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest("Preview is only available for image or PDF files.");
                }

                var provider = new FileExtensionContentTypeProvider();
                if (!provider.TryGetContentType(attachment.FilePath, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(attachment.FilePath);

                _logger.LogInformation("Action: PreviewAttachment | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);

                // No fileDownloadName here — this is what keeps the response "inline"
                // instead of forcing Content-Disposition: attachment.
                return File(fileBytes, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: PreviewAttachment (error) | Attachment: {AttachmentId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, "An error occurred while loading the preview.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditSecurity(int? id)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var empNo = HttpContext.Session.GetString("EmpNo");

            if (id == null)
            {
                return RedirectToAction("SecurityTable");
            }

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: EditSecurity (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return NotFound();
                }

                ViewBag.ApproverUserName = await _changeRequestService.GetAssignedApproverUserNameAsync(id.Value);

                var attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id.Value);
                ViewBag.Attachments = attachments;

                var users = await _userService.GetAllUsersAsync();
                ViewBag.Users = users;

                ViewBag.ChangeImpacts = await _changeRequestService.GetChangeImpactsAsync();

                _logger.LogInformation("Action: EditSecurity | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return View("AssesmentSecurity", changeRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: EditSecurity (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return View("Error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Testing(int? id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            if (id == null)
            {
                return RedirectToAction("Index");
            }

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
                    _logger.LogWarning("Action: Testing (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return NotFound();
                }

                var attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id.Value);
                ViewBag.Attachments = attachments;

                var users = await _userService.GetAllUsersAsync();
                ViewBag.Users = users;

                ViewBag.ChangeImpacts = await _changeRequestService.GetChangeImpactsAsync();

                _logger.LogInformation("Action: Testing (GET) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return View("Testing", changeRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: Testing (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return View("Error");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateTesting(IFormCollection collection)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var empNo = HttpContext.Session.GetString("EmpNo");

            try
            {
                if (!int.TryParse(collection["Id"], out int id))
                {
                    _logger.LogWarning("Action: CreateTesting (invalid ID) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                    return Json(new
                    {
                        success = false,
                        message = "Invalid or missing ID in form payload."
                    });
                }

                bool isSubmit = collection["Status"].ToString() == "2";

                if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(empNo))
                {
                    _logger.LogWarning("Action: CreateTesting (session expired) | CR: {CRId} | Time: {Timestamp}", id, DateTime.Now);
                    return Json(new
                    {
                        success = false,
                        message = "Session expired. Please log in again."
                    });
                }

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
                else
                {
                    _logger.LogWarning("Action: CreateTesting (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return Json(new
                    {
                        success = false,
                        message = "Failed to update Change Request"
                    });
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: CreateTesting (validation error) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: CreateTesting (error) | User: {EmpNo} | Time: {Timestamp}", empNo, DateTime.Now);
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AssignFinalApprover(int id, int finalApproverId)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.AssignFinalApproverAsync(id, finalApproverId, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: AssignFinalApprover (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to approve the Change Request." });
                }

                _logger.LogInformation("Action: AssignFinalApprover | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { message = "Change Request approved successfully." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: AssignFinalApprover (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: AssignFinalApprover (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, new { message = "An unexpected error occurred while approving the CR." });
            }
        }


        [HttpGet]
        public async Task<IActionResult> CRDetailsPartial(int id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var cr = await _changeRequestService.GetChangeRequestByIdAsync(id);
                if (cr == null)
                {
                    _logger.LogWarning("Action: CRDetailsPartial (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return NotFound();
                }

                var attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id);
                cr.Attachments = attachments?.ToList() ?? new List<Attachment>();

                ViewBag.CurrentEmpNo = empNo;
                ViewBag.CurrentUserName = empNo;
                ViewBag.CurrentDisplayName = HttpContext.Session.GetString("UserName") ?? string.Empty;
                ViewBag.Users = await _userService.GetAllUsersAsync();

                _logger.LogInformation("Action: CRDetailsPartial | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return PartialView("_CRDetailsPartial", cr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: CRDetailsPartial (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                throw;
            }

        }


        [HttpPost]
        public async Task<IActionResult> AcceptDevAssessment(int id, string securityOfficerId)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.AcceptDevAssessment(id, empNo, securityOfficerId);

                if (!success)
                {
                    _logger.LogWarning("Action: AcceptDevAssessment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to accept the Change Request." });
                }

                _logger.LogInformation("Action: AcceptDevAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { success = true, message = "Assessment accepted and assigned to the IS Officer." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: AcceptDevAssessment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Action: AcceptDevAssessment (invalid operation) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: AcceptDevAssessment (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, new { success = false, message = "An unexpected error occurred while accepting the CR." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ReturnDevAssessment(int id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.ReturnDevAssessment(id, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: ReturnDevAssessment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to return the Change Request." });
                }

                _logger.LogInformation("Action: ReturnDevAssessment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { success = true, message = "Change Request returned to the developer." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: ReturnDevAssessment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Action: ReturnDevAssessment (invalid operation) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: ReturnDevAssessment (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, new { success = false, message = "An unexpected error occurred while returning the CR." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> AssignedByMeTable()
        {
            var empNo = HttpContext.Session.GetString("EmpNo");
            ViewBag.CurrentEmpNo = empNo;
            var crs = await _changeRequestService.GetAllChangeRequestsAssignedByMeAsync(empNo);
            return View(crs);
        }

        [HttpGet]
        public async Task<IActionResult> AssignedToMeTable()
        {
            var empNo = HttpContext.Session.GetString("EmpNo");
            ViewBag.CurrentEmpNo = empNo;
            var crs = await _changeRequestService.GetAllChangeRequestsAssignedToMeAsync(empNo);
            return View(crs);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveToDevelopment(int id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.ApproveToDevelopmentAsync(id, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: ApproveToDevelopment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to approve the Change Request." });
                }

                _logger.LogInformation("Action: ApproveToDevelopment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { success = true, message = "Change Request approved successfully." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: ApproveToDevelopment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: ApproveToDevelopment (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, new { success = false, message = "An unexpected error occurred while approving the CR." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ProceedToDevelopment(int id, string uatLink)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.ProceedToDevelopmentAsync(id, uatLink, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: ProceedToDevelopment (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to send UAT Link." });
                }

                _logger.LogInformation("Action: ProceedToDevelopment | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { message = "UAT handover to QA." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: ProceedToDevelopment (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: ProceedToDevelopment (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, new { message = "An unexpected error occurred while rejecting the CR." });
            }
        }


        [HttpPost]
        public async Task<IActionResult> ReturnToQA(int id, string reason)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.ReturnToQAAsync(id, reason, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: ReturnToQA (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to return the Change Request to QA." });
                }

                _logger.LogInformation("Action: ReturnToQA | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { success = true, message = "Change Request returned to QA." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: ReturnToQA (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Action: ReturnToQA (invalid operation) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: ReturnToQA (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, new { success = false, message = "An unexpected error occurred while returning the CR to QA." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> FinalApproval(int id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.FinalApproval(id, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: FinalApproval (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { success = false, message = "Failed to approve the CR ." });
                }

                _logger.LogInformation("Action: FinalApproval | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { success = true, message = "CR Final Approve Success." });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: FinalApproval (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Action: FinalApproval (invalid operation) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: FinalApproval (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, new { success = false, message = "An unexpected error occurred while accepting the CR." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SubmitLiveLink(int id, string liveLink)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var success = await _changeRequestService.SubmitLiveLink(id, liveLink, empNo);

                if (!success)
                {
                    _logger.LogWarning("Action: SubmitLiveLink (failed) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                    return BadRequest(new { message = "Failed to send LIVE Link." });
                }

                _logger.LogInformation("Action: SubmitLiveLink | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return Ok(new { message = "Live link Deployed" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Action: SubmitLiveLink (validation error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action: SubmitLiveLink (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}", id, empNo, DateTime.Now);
                return StatusCode(500, new { message = "An unexpected error occurred while rejecting the CR." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadCRForm(int id)
        {
            var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty;

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id);

                if (changeRequest == null)
                {
                    _logger.LogWarning(
                        "Action: DownloadCRForm (not found) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}",
                        id, empNo, DateTime.Now);

                    return NotFound();
                }

                var attachments = await _changeRequestService.GetAttachmentsByCrIdAsync(id);
                changeRequest.Attachments = attachments?.ToList() ?? new List<Attachment>();

                ViewBag.Users = await _userService.GetAllUsersAsync();
                ViewBag.ApproverUserName =
                    await _changeRequestService.GetAssignedApproverUserNameAsync(id);

                double effortEstimateDays = 0;

                if (changeRequest.DueDate.HasValue)
                {
                    effortEstimateDays = Math.Max(
                        0,
                        (changeRequest.DueDate.Value.Date - DateTime.Now.Date).TotalDays);
                }

                ViewBag.EffortEstimateDays = effortEstimateDays;

                // Create the ViewModel expected by CRFormPrint.cshtml
                var viewModel = new ReportViewModel
                {
                    ChangeRequest = changeRequest,
                    Testing = new Testing()
                };

                _logger.LogInformation(
                    "Action: DownloadCRForm | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}",
                    id, empNo, DateTime.Now);

                return new Rotativa.AspNetCore.ViewAsPdf("CRFormPrint", viewModel)
                {
                    FileName = $"CR_{changeRequest.CRNumber}.pdf",
                    PageSize = Rotativa.AspNetCore.Options.Size.A4,
                    PageMargins = new Rotativa.AspNetCore.Options.Margins(15, 10, 15, 10),
                    CustomSwitches = "--disable-smart-shrinking"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Action: DownloadCRForm (error) | CR: {CRId} | User: {EmpNo} | Time: {Timestamp}",
                    id, empNo, DateTime.Now);

                return View("Error");
            }
        }
    }
}
