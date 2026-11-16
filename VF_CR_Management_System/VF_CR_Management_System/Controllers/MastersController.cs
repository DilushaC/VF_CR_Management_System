using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.MastersHandler;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Controllers
{
    public class MastersController : Controller
    {
        private readonly IMastersService _mastersService;

        public MastersController(IMastersService mastersService)
        {
            _mastersService = mastersService;
        }

        public IActionResult ChangeImpactMaster()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> VendorMaster()
        {
            var vendors = await _mastersService.GetAllVendorsAsync();
            return View(vendors);
        }


        [HttpGet]
        public async Task<IActionResult> DivisionMaster()
        {
            var divisions = await _mastersService.GetAllDivisionsAsync();
            return View(divisions);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddVendor(string VendorName, string Description, bool Active)
        {
            try
            {
                var vendor = new Vendor
                {
                    VendorName = VendorName,
                    Description = Description,
                    Active = Active
                };

                var newVendorId = await _mastersService.AddVendorAsync(vendor);

                if (newVendorId <= 0)
                {
                    return BadRequest(new { message = "Failed to add the vendor. Please try again." });
                }

                return Json(new { vendorId = newVendorId });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditVendor(int VendorID, string VendorName, string Description, bool Active)
        {
            try
            {
                var vendor = new Vendor
                {
                    VendorID = VendorID,
                    VendorName = VendorName,
                    Description = Description,
                    Active = Active
                };

                var success = await _mastersService.UpdateVendorAsync(vendor);

                if (!success)
                {
                    return NotFound(new { message = "Vendor not found or could not be updated." });
                }

                return Json(new { vendorId = VendorID });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteVendor(int id)
        {
            try
            {
                var success = await _mastersService.DeleteVendorAsync(id);

                if (!success)
                {
                    return NotFound(new { message = "Vendor not found." });
                }

                return Ok();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDivision(string DivisionName, string Description, bool Active)
        {
            try
            {
                var division = new Division
                {
                    DivisionName = DivisionName,
                    Description = Description,
                    Active = Active
                };

                var newId = await _mastersService.AddDivisionAsync(division);

                if (newId <= 0)
                {
                    return BadRequest(new { message = "Failed to add the division. Please try again." });
                }

                return Json(new { id = newId });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDivision(int Id, string DivisionName, string Description, bool Active)
        {
            try
            {
                var division = new Division
                {
                    DivisionID = Id,
                    DivisionName = DivisionName,
                    Description = Description,
                    Active = Active
                };

                var success = await _mastersService.UpdateDivisionAsync(division);

                if (!success)
                {
                    return NotFound(new { message = "Division not found or could not be updated." });
                }

                return Json(new { id = Id });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDivision(int id)
        {
            try
            {
                var success = await _mastersService.DeleteDivisionAsync(id);

                if (!success)
                {
                    return NotFound(new { message = "Division not found." });
                }

                return Ok();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
