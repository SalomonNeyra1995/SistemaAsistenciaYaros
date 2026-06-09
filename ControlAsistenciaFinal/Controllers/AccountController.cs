using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using System.Web.Security;
using ControlAsistenciaFinal.Models;

namespace ControlAsistenciaFinal.Controllers
{
    public class AccountController : Controller
    {
        [HttpGet]
        public ActionResult Login()
        {
            // Configurar la ruta de las imágenes
            ViewBag.vRuta = "Content/images/";
            return View();
        }

        [HttpPost]
        public ActionResult Login(string email, string password)
        {
            try
            {
                string passwordHash = SecurityHelper.HashPassword(password);

                string query = "SELECT Id, NombreCompleto, Rol FROM Usuarios WHERE Email = @Email AND PasswordHash = @PasswordHash AND Activo = 1";

                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@Email", email),
                    new SqlParameter("@PasswordHash", passwordHash)
                };

                DataTable result = DatabaseHelper.ExecuteQuery(query, parameters);

                if (result.Rows.Count > 0)
                {
                    DataRow row = result.Rows[0];

                    Session["UsuarioId"] = row["Id"];
                    Session["Nombre"] = row["NombreCompleto"];
                    Session["Rol"] = row["Rol"];
                    Session["Email"] = email;

                    FormsAuthentication.SetAuthCookie(email, false);

                    if (row["Rol"].ToString() == "Admin")
                        return RedirectToAction("Dashboard", "Admin");
                    else
                        return RedirectToAction("MiJornadaMejorada", "Trabajador"); 
                }

                ViewBag.Error = "Correo o contraseña incorrectos";
                return View();
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Error de conexión: " + ex.Message;
                return View();
            }
        }

        public ActionResult Logout()
        {
            Session.Clear();
            Session.Abandon();
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }
    }
}