using System.Web.Mvc;
using System.Web.Routing;

namespace ControlAsistenciaFinal.Filters
{
    public class AdminAuthorizeAttribute : AuthorizeAttribute
    {
        protected override bool AuthorizeCore(System.Web.HttpContextBase httpContext)
        {
            var session = httpContext.Session;
            if (session != null && session["Rol"] != null)
            {
                return session["Rol"].ToString() == "Admin";
            }
            return false;
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            var session = filterContext.HttpContext.Session;

            if (session != null && session["Rol"] != null)
            {
                // Si está logueado pero no es Admin
                filterContext.Result = new RedirectToRouteResult(
                    new RouteValueDictionary(new { controller = "Shared", action = "AccessDenied" })
                );
            }
            else
            {
                // Si no está logueado
                filterContext.Result = new RedirectToRouteResult(
                    new RouteValueDictionary(new { controller = "Account", action = "Login" })
                );
            }
        }
    }
}