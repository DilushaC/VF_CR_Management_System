using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace VF_CR_Management_System.Presentation.Filters
{
    public class SessionCheckAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var controllerName = context.ActionDescriptor.RouteValues["controller"];
            if (controllerName != null && controllerName.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                base.OnActionExecuting(context);
                return;
            }

            context.HttpContext.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.HttpContext.Response.Headers["Pragma"] = "no-cache";
            context.HttpContext.Response.Headers["Expires"] = "0";

            var session = context.HttpContext.Session.GetString("UserName");

            if (string.IsNullOrEmpty(session))
            {
                context.HttpContext.Session.Clear();

                var isAjax = context.HttpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest";

                if (isAjax)
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Result = new JsonResult(new
                    {
                        sessionExpired = true,
                        redirectUrl = "/User/Login"
                    });
                }
                else
                {
                    context.Result = new RedirectToActionResult("Login", "User", null);
                }

                return;
            }

            base.OnActionExecuting(context);
        }
    }
}