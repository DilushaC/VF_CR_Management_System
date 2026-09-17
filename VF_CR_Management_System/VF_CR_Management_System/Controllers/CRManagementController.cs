using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System;
using System.Net.Mail;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.ChangeRequestHandler;
using VF_CR_Management_System.Business.DivisionHandler;
using VF_CR_Management_System.Business.ModuleHandler;
using VF_CR_Management_System.Business.UserHandler;
using VF_CR_Management_System.Business.VendorHandler;

namespace VF_CR_Management_System.Controllers
{
    public class CRManagementController : Controller
    {
        private readonly IChangeRequestService _changeRequestService;
        private readonly IUserService _userService;
        private readonly IModuleService _moduleService;
        private readonly IDivisionService _divisionService;
        private readonly IVendorService _vendorService;

        public CRManagementController(IChangeRequestService changeRequestService, IUserService userService, IModuleService moduleService, IDivisionService divisionService, IVendorService vendorService)
        {
            _changeRequestService = changeRequestService;
            _userService = userService;
            _moduleService = moduleService;
            _divisionService = divisionService;
            _vendorService = vendorService;
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
                var empNo = HttpContext.Session.GetString("EmpNo") ?? string.Empty; 
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
        public async Task<IActionResult> Assessment(int? id)
        {
            if (id == null)
            {
                return RedirectToAction("Index");
            }

            try
            {
                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
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

                return View("Assessment", changeRequest);
            }
            catch (Exception ex)
            {
                return View("Error");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateAssessment(IFormCollection collection)
        {
            try
            {
                if (!int.TryParse(collection["id"], out int id))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Invalid or missing ID in form payload."
                    });
                }

                var userName = HttpContext.Session.GetString("UserName");
                var empNo = HttpContext.Session.GetString("EmpNo");

                bool updated = await _changeRequestService.CreateAssessmentAsync(id, collection, userName, empNo);
                if (updated)
                {
                    return Json(new
                    {
                        success = true,
                        message = "Assessment updated successfully",
                        redirectUrl = Url.Action("AssessmentsTable", "CRManagement")
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

        public async Task<IActionResult> DraftTable()
        {
            var empNo = HttpContext.Session.GetString("EmpNo");
            var crs = await _changeRequestService.GetAllChangeRequestsDraftsAsync(empNo);
            return View(crs);
        }

        public async Task<IActionResult> SubmissionTable()
        {
            var empNo = HttpContext.Session.GetString("EmpNo");
            var userName = HttpContext.Session.GetString("UserName");
            ViewBag.CurrentEmpNo = empNo;
            ViewBag.UserName = userName;
            var users = await _userService.GetAllUsersAsync();
            ViewBag.Users = users;
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

        public async Task<IActionResult> AssessmentsTable()
        {
            var empNo = HttpContext.Session.GetString("EmpNo");
            ViewBag.CurrentEmpNo = empNo;
            var crs = await _changeRequestService.GetAllChangeRequestsAssessmentsAsync(empNo);
            return View(crs);
        }

        public async Task<IActionResult> SecurityTable()
        {
            var empNo = HttpContext.Session.GetString("EmpNo");
            ViewBag.CurrentEmpNo = empNo;
            var crs = await _changeRequestService.GetAllChangeRequestsSecurityAsync(empNo);
            return View(crs);
        }


        [HttpGet]
        public async Task<IActionResult> EditAssessment(int? id)
        {
            if (id == null)
            {
                return RedirectToAction("AssessmentsTable");
            }

            try
            {
                var userName = HttpContext.Session.GetString("UserName");
                var empNo = HttpContext.Session.GetString("EmpNo");

                var changeRequest = await _changeRequestService.GetChangeRequestByIdAsync(id.Value);
                if (changeRequest == null)
                {
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

                return View("Assessment", changeRequest);
            }
            catch (Exception ex)
            {
                return View("Error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadAttachment(int id)
        {
            try
            {
                var attachment = await _changeRequestService.GetAttachmentByIdAsync(id);

                if (attachment == null)
                    return NotFound();

                if (!System.IO.File.Exists(attachment.FilePath))
                    return NotFound("The file could not be found on the server.");

                var provider = new FileExtensionContentTypeProvider();
                if (!provider.TryGetContentType(attachment.FilePath, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(attachment.FilePath);

                return File(fileBytes, contentType, attachment.FileName);
            }
            catch (Exception)
            {
                return StatusCode(500, "An error occurred while downloading the file.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            try
            {
                var userName = HttpContext.Session.GetString("UserName");

                var success = await _changeRequestService.DeleteAttachmentAsync(id, userName);

                if (!success)
                {
                    return Json(new { success = false, message = "Attachment not found or already removed." });
                }

                return Json(new { success = true, message = "Attachment removed." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PreviewAttachment(int id)
        {
            try
            {
                var attachment = await _changeRequestService.GetAttachmentByIdAsync(id);

                if (attachment == null)
                    return NotFound();

                if (!System.IO.File.Exists(attachment.FilePath))
                    return NotFound("The file could not be found on the server.");

                // Only allow inline preview for images and PDFs — anything else falls
                // back to a normal download rather than being rendered in the browser.
                var previewableExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".pdf" };
                var extension = Path.GetExtension(attachment.FilePath).ToLowerInvariant();

                if (!previewableExtensions.Contains(extension))
                {
                    return BadRequest("Preview is only available for image or PDF files.");
                }

                var provider = new FileExtensionContentTypeProvider();
                if (!provider.TryGetContentType(attachment.FilePath, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(attachment.FilePath);

                // No fileDownloadName here — this is what keeps the response "inline"
                // instead of forcing Content-Disposition: attachment.
                return File(fileBytes, contentType);
            }
            catch (Exception)
            {
                return StatusCode(500, "An error occurred while loading the preview.");
            }
        }
    }
}
