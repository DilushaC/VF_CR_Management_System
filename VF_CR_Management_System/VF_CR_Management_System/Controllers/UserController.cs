using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Text.Json;
using VF_CR_Management_System.Business.UserHandler;
using VF_CR_Management_System.Data.Models;
using VF_CR_Management_System.Presentation.Filters;

namespace VF_CR_Management_System.Presentation.Controllers
{
    [SessionCheck]
    public class UserController : Controller
    {
        private readonly IUserService _userService;
        private readonly IConfiguration _configuration;

        public UserController(IUserService userService, IConfiguration configuration)
        {
            _userService = userService;
            _configuration = configuration;
        }
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public ActionResult Login()
        {
            HttpContext.Session.Clear();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password)
        {
            int allowedProductId = _configuration.GetValue<int>("AllowedProducts:ProductId");
            try
            {
                var user = await _userService.ValidateUserAsync(username, password, allowedProductId);

                if (user == null)
                {
                    return Json(new { success = false, message = "Invalid login" });
                }

                if (user.ProductIds == null || !user.ProductIds.Contains(allowedProductId))
                    return Json(new { success = false, message = "Unauthorized product access" });

                // Session storage
                HttpContext.Session.SetString("UserName", user.DisplayName);
                HttpContext.Session.SetString("EmpNo", user.UserName);
                HttpContext.Session.SetString("Designation", user.DisplayDesignation);
                HttpContext.Session.SetString("Department", user.DisplayDepartment);
                HttpContext.Session.SetString("Email", user.Email);
                HttpContext.Session.SetString("UserId", user.Id.ToString());

                // Store PageUrls
                var pageUrlsJson = JsonSerializer.Serialize(user.PageUrls ?? new List<string>());
                HttpContext.Session.SetString("PageUrls", pageUrlsJson);

                // Store MenuItems
                var menuJson = JsonSerializer.Serialize(user.MenuItems ?? new List<MenuItem>());
                HttpContext.Session.SetString("MenuItems", menuJson);

                return Json(new
                {
                    success = true,
                    redirectUrl = Url.Action("Index", "Home"),
                    loggedUser = user.DisplayName
                });
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}
