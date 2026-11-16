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
    }
}
