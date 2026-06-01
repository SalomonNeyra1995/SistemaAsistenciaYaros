using System.Web.Mvc;

namespace ControlAsistenciaFinal.Controllers
{
    public class SharedController : Controller
    {
        public ActionResult AccessDenied()
        {
            return View();
        }
    }
}