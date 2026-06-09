using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using ControlAsistenciaFinal.Models;
using System.Collections.Generic;
using System.Text;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using System.IO;

namespace ControlAsistenciaFinal.Controllers
{
    public class AdminController : Controller
    {
        // ============================================
        // DASHBOARD
        // ============================================

        [HttpGet]
        public ActionResult Dashboard()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return RedirectToAction("Login", "Account");

            ViewBag.Nombre = Session["Nombre"];
            CargarEstadisticas();
            return View();
        }

        private void CargarEstadisticas()
        {
            DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerEstadisticasDashboard", null);

            if (dt.Rows.Count >= 4)
            {
                ViewBag.TotalEmpleados = dt.Rows[0][0];
                ViewBag.PresentesHoy = dt.Rows[1][0];
                ViewBag.TardanzasHoy = dt.Rows[2][0];
                ViewBag.AusentesHoy = dt.Rows[3][0];
            }
        }

        // ============================================
        // GESTIÓN DE USUARIOS
        // ============================================

        [HttpGet]
        public ActionResult Usuarios()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return RedirectToAction("Login", "Account");

            string query = "SELECT Id, NombreCompleto, Email, Rol, Activo, Celular, Banco, CuentaAhorros FROM Usuarios ORDER BY NombreCompleto";
            DataTable usuarios = DatabaseHelper.ExecuteQuery(query, null);
            return View(usuarios);
        }

        // ============================================
        // VERIFICAR Y REINICIAR CICLOS
        // ============================================

        [HttpPost]
        public JsonResult VerificarCiclos()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                DatabaseHelper.ExecuteStoredProcedure("sp_VerificarYReiniciarCiclos", null);
                return Json(new { success = true, message = "Ciclos verificados correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult ObtenerCiclosUsuario(int usuarioId)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { error = "No autorizado" }, JsonRequestBehavior.AllowGet);

            try
            {
                SqlParameter[] parameters = new SqlParameter[] { new SqlParameter("@UsuarioId", usuarioId) };
                DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerCiclosUsuario", parameters);

                var ciclos = new List<object>();
                foreach (DataRow row in dt.Rows)
                {
                    ciclos.Add(new
                    {
                        Id = row["Id"],
                        FechaInicio = Convert.ToDateTime(row["FechaInicio"]).ToString("dd/MM/yyyy"),
                        FechaFin = row["FechaFin"] != DBNull.Value ? Convert.ToDateTime(row["FechaFin"]).ToString("dd/MM/yyyy") : "En curso",
                        HorasAcumuladas = Math.Round(Convert.ToDecimal(row["HorasAcumuladas"]), 2),
                        HorasObjetivo = Convert.ToDecimal(row["HorasObjetivo"]),
                        Estado = row["Estado"].ToString(),
                        PorcentajeAvance = Convert.ToDecimal(row["PorcentajeAvance"])
                    });
                }

                return Json(new { success = true, data = ciclos }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public ActionResult CrearUsuario()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return RedirectToAction("Login", "Account");

            string queryConceptos = @"SELECT Id, ConceptoPago, Tipo, TarifaHora FROM ConfiguracionPagos WHERE Activo = 1 ORDER BY Tipo, ConceptoPago";
            ViewBag.ConceptosPago = DatabaseHelper.ExecuteQuery(queryConceptos, null);
            return View();
        }

        [HttpPost]
        public ActionResult CrearUsuario(CrearUsuarioViewModel model)
        {
            try
            {
                if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                    return RedirectToAction("Login", "Account");

                if (ModelState.IsValid)
                {
                    // Validar DNI (8 dígitos)
                    if (model.DNI.Length != 8)
                    {
                        ModelState.AddModelError("DNI", "El DNI debe tener exactamente 8 dígitos");
                        CargarConceptosPago();
                        return View(model);
                    }

                    // Verificar que el DNI solo contenga números
                    foreach (char c in model.DNI)
                    {
                        if (!char.IsDigit(c))
                        {
                            ModelState.AddModelError("DNI", "El DNI debe contener solo números");
                            CargarConceptosPago();
                            return View(model);
                        }
                    }

                    // Validar contraseña
                    if (string.IsNullOrEmpty(model.Password) || model.Password.Length < 6)
                    {
                        ModelState.AddModelError("Password", "La contraseña debe tener al menos 6 caracteres");
                        CargarConceptosPago();
                        return View(model);
                    }

                    // ============================================
                    // VALIDAR QUE EL DNI NO EXISTA YA EN LA BD
                    // ============================================
                    string checkDniQuery = "SELECT COUNT(*) FROM Usuarios WHERE DNI = @DNI";
                    SqlParameter[] checkDniParams = { new SqlParameter("@DNI", model.DNI) };
                    int dniExiste = Convert.ToInt32(DatabaseHelper.ExecuteScalar(checkDniQuery, checkDniParams));

                    if (dniExiste > 0)
                    {
                        ModelState.AddModelError("DNI", "Ya existe un usuario registrado con este DNI");
                        TempData["Error"] = "No se pudo crear el usuario. Ya existe un usuario con el DNI: " + model.DNI;
                        CargarConceptosPago();
                        return View(model);
                    }

                    // Limpiar caracteres especiales de nombres
                    string nombresLimpios = model.Nombres.Trim();
                    string apellidoPaternoLimpio = model.ApellidoPaterno.Trim();
                    string apellidoMaternoLimpio = string.IsNullOrEmpty(model.ApellidoMaterno) ? "" : model.ApellidoMaterno.Trim();

                    // Generar email automático con el nuevo formato: primeraLetraNombre.apellidoPaterno + primeraLetraApellidoMaterno@asistenciayaros.com
                    string email = GenerarEmailNuevoFormato(nombresLimpios, apellidoPaternoLimpio, apellidoMaternoLimpio);

                    // Generar nombre completo
                    string nombreCompleto = $"{model.Nombres} {model.ApellidoPaterno} {model.ApellidoMaterno}".Trim();

                    string password = model.Password;
                    string passwordHash = SecurityHelper.HashPassword(password);

                    // Verificar si el email ya existe
                    string checkEmailQuery = "SELECT COUNT(*) FROM Usuarios WHERE Email = @Email";
                    SqlParameter[] checkEmailParams = { new SqlParameter("@Email", email) };
                    int existe = Convert.ToInt32(DatabaseHelper.ExecuteScalar(checkEmailQuery, checkEmailParams));

                    Random rand = new Random();
                    while (existe > 0)
                    {
                        email = GenerarEmailNuevoFormato(nombresLimpios, apellidoPaternoLimpio, apellidoMaternoLimpio) + rand.Next(1, 999);
                        checkEmailParams = new SqlParameter[] { new SqlParameter("@Email", email) };
                        existe = Convert.ToInt32(DatabaseHelper.ExecuteScalar(checkEmailQuery, checkEmailParams));
                    }

                    // Obtener datos del concepto de pago
                    string queryConcepto = @"SELECT Id, ConceptoPago, Tipo, TarifaHora, HorasDiarias, HorasMensuales, MontoBase, DiasBase 
                                     FROM ConfiguracionPagos WHERE Id = @ConceptoPagoId AND Activo = 1";
                    SqlParameter[] conceptoParams = new SqlParameter[] { new SqlParameter("@ConceptoPagoId", model.ConceptoPagoId) };
                    DataTable dtConcepto = DatabaseHelper.ExecuteQuery(queryConcepto, conceptoParams);

                    string rolPago = "Facilitador 1";
                    decimal tarifaHora = 5.89m;
                    int horasMensualesObjetivo = 160;

                    if (dtConcepto.Rows.Count > 0)
                    {
                        DataRow rowConcepto = dtConcepto.Rows[0];
                        rolPago = rowConcepto["ConceptoPago"].ToString();
                        string tipo = rowConcepto["Tipo"].ToString();

                        if (tipo == "Facilitador")
                        {
                            tarifaHora = Convert.ToDecimal(rowConcepto["TarifaHora"]);
                            horasMensualesObjetivo = (int)(Convert.ToDecimal(rowConcepto["DiasBase"]) * Convert.ToDecimal(rowConcepto["HorasDiarias"]));
                        }
                        else if (tipo == "Planilla")
                        {
                            tarifaHora = 0;
                            if (rowConcepto["HorasMensuales"] != DBNull.Value)
                            {
                                horasMensualesObjetivo = Convert.ToInt32(rowConcepto["HorasMensuales"]);
                            }
                        }
                    }

                    string query = @"INSERT INTO Usuarios (NombreCompleto, Email, PasswordHash, Rol, HorasMensualesObjetivo, Activo, DNI, Nombres, ApellidoPaterno, ApellidoMaterno, Celular, CuentaAhorros, Direccion, RolPago, TarifaHora, Banco, ConceptoPagoId, FechaInicio)
                             VALUES (@NombreCompleto, @Email, @Password, @Rol, @Horas, 1, @DNI, @Nombres, @ApellidoPaterno, @ApellidoMaterno, @Celular, @CuentaAhorros, @Direccion, @RolPago, @TarifaHora, @Banco, @ConceptoPagoId, @FechaInicio);
                             SELECT SCOPE_IDENTITY();";

                    SqlParameter[] parameters = new SqlParameter[]
                    {
                new SqlParameter("@NombreCompleto", nombreCompleto),
                new SqlParameter("@Email", email),
                new SqlParameter("@Password", passwordHash),
                new SqlParameter("@Rol", model.Rol),
                new SqlParameter("@Horas", horasMensualesObjetivo),
                new SqlParameter("@DNI", model.DNI),
                new SqlParameter("@Nombres", model.Nombres),
                new SqlParameter("@ApellidoPaterno", model.ApellidoPaterno),
                new SqlParameter("@ApellidoMaterno", string.IsNullOrEmpty(model.ApellidoMaterno) ? (object)DBNull.Value : model.ApellidoMaterno),
                new SqlParameter("@Celular", string.IsNullOrEmpty(model.Celular) ? (object)DBNull.Value : model.Celular),
                new SqlParameter("@CuentaAhorros", string.IsNullOrEmpty(model.CuentaAhorros) ? (object)DBNull.Value : model.CuentaAhorros),
                new SqlParameter("@Direccion", string.IsNullOrEmpty(model.Direccion) ? (object)DBNull.Value : model.Direccion),
                new SqlParameter("@RolPago", rolPago),
                new SqlParameter("@TarifaHora", tarifaHora),
                new SqlParameter("@Banco", string.IsNullOrEmpty(model.Banco) ? (object)DBNull.Value : model.Banco),
                new SqlParameter("@ConceptoPagoId", model.ConceptoPagoId),
                new SqlParameter("@FechaInicio", model.FechaInicio)
                    };

                    object result = DatabaseHelper.ExecuteScalar(query, parameters);
                    int nuevoId = result != null ? Convert.ToInt32(result) : 0;

                    if (nuevoId > 0)
                    {
                        TempData["MostrarModal"] = true;
                        TempData["EmailGenerado"] = email;
                        TempData["PasswordGenerado"] = password;
                        TempData["NombreCompleto"] = nombreCompleto;
                        TempData["FechaInicio"] = model.FechaInicio.ToString("dd/MM/yyyy");
                        TempData["Mensaje"] = "Usuario creado exitosamente";

                        return RedirectToAction("Usuarios");
                    }
                    else
                    {
                        TempData["Error"] = "Error al crear el usuario. Intente nuevamente.";
                        CargarConceptosPago();
                        return View(model);
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al crear usuario: " + ex.Message;
                CargarConceptosPago();
                return View(model);
            }

            CargarConceptosPago();
            return View(model);
        }

        // Método para generar email con formato: 
        // PRIMERA LETRA DEL PRIMER NOMBRE + APELLIDO PATERNO + PRIMERA LETRA DEL APELLIDO MATERNO @asistenciayaros.com
        // Ejemplo: Yamiliet Estefany Flores Ortiz → yfloreso@asistenciayaros.com
                private string GenerarEmailNuevoFormato(string nombres, string apellidoPaterno, string apellidoMaterno)
                {
                    // Obtener PRIMERA letra del PRIMER nombre (solo el primer nombre, no el segundo)
                    string[] nombresArray = nombres.Trim().Split(' ');
                    string primerNombre = nombresArray.Length > 0 ? nombresArray[0] : nombres;
                    string primeraLetraNombre = primerNombre.Length > 0 ? primerNombre.Substring(0, 1).ToLower() : "";

                    // Apellido paterno completo (todo en minúsculas)
                    string apellidoPaternoLower = apellidoPaterno.Trim().ToLower();

                    // Primera letra del apellido materno (si existe)
                    string primeraLetraMaterno = !string.IsNullOrEmpty(apellidoMaterno) ? apellidoMaterno.Trim().Substring(0, 1).ToLower() : "";

                    // Eliminar caracteres especiales y tildes
                    primeraLetraNombre = RemoverAcentos(primeraLetraNombre);
                    apellidoPaternoLower = RemoverAcentos(apellidoPaternoLower);
                    primeraLetraMaterno = RemoverAcentos(primeraLetraMaterno);

                    // Formato: yfloreso@asistenciayaros.com
                    string email = $"{primeraLetraNombre}{apellidoPaternoLower}{primeraLetraMaterno}@asistenciayaros.com";

                    return email;
                }

                // Método auxiliar para eliminar tildes y caracteres especiales
                private string RemoverAcentos(string texto)
                {
                    if (string.IsNullOrEmpty(texto)) return texto;

                    var reemplazos = new System.Collections.Generic.Dictionary<string, string>
            {
                { "á", "a" }, { "é", "e" }, { "í", "i" }, { "ó", "o" }, { "ú", "u" },
                { "Á", "a" }, { "É", "e" }, { "Í", "i" }, { "Ó", "o" }, { "Ú", "u" },
                { "ñ", "n" }, { "Ñ", "n" }, { "ü", "u" }, { "Ü", "u" }
            };

                    foreach (var item in reemplazos)
                    {
                        texto = texto.Replace(item.Key, item.Value);
                    }

                    return texto;
                }

        // Método auxiliar para eliminar tildes y caracteres especiales
      
 
        private void CargarConceptosPago()
        {
            string query = "SELECT Id, ConceptoPago, Tipo, TarifaHora FROM ConfiguracionPagos WHERE Activo = 1 ORDER BY ConceptoPago";
            // Pasar null como segundo parámetro cuando no hay parámetros
            ViewBag.ConceptosPago = DatabaseHelper.ExecuteQuery(query, null);
        }

        private string GenerarEmail(string nombres, string apellidoPaterno, string apellidoMaterno)
        {
            string email = $"{nombres.ToLower().Replace(" ", "")}.{apellidoPaterno.ToLower()}";

            if (!string.IsNullOrEmpty(apellidoMaterno) && apellidoMaterno.Length >= 2)
            {
                email += apellidoMaterno.ToLower().Substring(0, Math.Min(2, apellidoMaterno.Length));
            }

            email += "@asistencia.com";
            email = email.Replace("ñ", "n").Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u").Replace(" ", "");

            return email;
        }

                    public ActionResult EditarUsuario(int id)
                    {
                        if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                            return RedirectToAction("Login", "Account");

                        try
                        {
                            // Obtener datos del usuario
                            string queryUsuario = "SELECT * FROM Usuarios WHERE Id = @Id";
                            SqlParameter[] paramUsuario = new SqlParameter[] { new SqlParameter("@Id", id) };
                            DataTable usuarioData = DatabaseHelper.ExecuteQuery(queryUsuario, paramUsuario);

                            if (usuarioData.Rows.Count == 0)
                                return RedirectToAction("Usuarios");

                            // Obtener roles de pago desde ConfiguracionPagos
                            string queryRolesPago = "SELECT Id, ConceptoPago FROM ConfiguracionPagos ORDER BY Id";
                            SqlParameter[] emptyParams = new SqlParameter[0];
                            DataTable rolesPago = DatabaseHelper.ExecuteQuery(queryRolesPago, emptyParams);

                            ViewBag.RolesPago = rolesPago;

                            return View(usuarioData);
                        }
                        catch (Exception ex)
                        {
                            TempData["Error"] = "Error al cargar los datos: " + ex.Message;
                            return RedirectToAction("Usuarios");
                        }
                    }


                [HttpPost]
                public ActionResult EditarUsuario(int id, string nombre, string email, string rol, bool activo,
                                   string celular, string banco, string cuentaAhorros,
                                   int conceptoPagoId, DateTime fechaInicio)
                {
                    if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                        return RedirectToAction("Login", "Account");

                    // Primero obtener la fecha de inicio actual del usuario
                    string queryGetFechaInicio = "SELECT FechaInicio FROM Usuarios WHERE Id = @Id";
                    SqlParameter[] paramFechaInicio = new SqlParameter[] { new SqlParameter("@Id", id) };
                    DataTable usuarioData = DatabaseHelper.ExecuteQuery(queryGetFechaInicio, paramFechaInicio);

                    DateTime fechaInicioActual = DateTime.Now;
                    bool fechaInicioCambio = false;

                    if (usuarioData.Rows.Count > 0)
                    {
                        fechaInicioActual = Convert.ToDateTime(usuarioData.Rows[0]["FechaInicio"]);
                        // Verificar si la fecha de inicio ha cambiado
                        fechaInicioCambio = fechaInicioActual.Date != fechaInicio.Date;
                    }

                    // Primero obtener la descripción del ConceptoPago seleccionado
                    string queryGetConcepto = "SELECT ConceptoPago FROM ConfiguracionPagos WHERE Id = @Id";
                    SqlParameter[] paramConcepto = new SqlParameter[] { new SqlParameter("@Id", conceptoPagoId) };
                    DataTable conceptoData = DatabaseHelper.ExecuteQuery(queryGetConcepto, paramConcepto);

                    string rolPagoDescripcion = "";
                    if (conceptoData.Rows.Count > 0)
                    {
                        rolPagoDescripcion = conceptoData.Rows[0]["ConceptoPago"].ToString();
                    }

                    string query = @"UPDATE Usuarios 
                     SET NombreCompleto = @Nombre, 
                         Email = @Email, 
                         Rol = @Rol, 
                         Activo = @Activo,
                         Celular = @Celular,
                         Banco = @Banco,
                         CuentaAhorros = @CuentaAhorros,
                         ConceptoPagoId = @ConceptoPagoId,
                         RolPago = @RolPago,
                         FechaInicio = @FechaInicio
                     WHERE Id = @Id";

                    SqlParameter[] parameters = new SqlParameter[]
                    {
                new SqlParameter("@Id", id),
                new SqlParameter("@Nombre", nombre),
                new SqlParameter("@Email", email),
                new SqlParameter("@Rol", rol),
                new SqlParameter("@Activo", activo),
                new SqlParameter("@Celular", string.IsNullOrEmpty(celular) ? (object)DBNull.Value : celular),
                new SqlParameter("@Banco", string.IsNullOrEmpty(banco) ? (object)DBNull.Value : banco),
                new SqlParameter("@CuentaAhorros", string.IsNullOrEmpty(cuentaAhorros) ? (object)DBNull.Value : cuentaAhorros),
                new SqlParameter("@ConceptoPagoId", conceptoPagoId),
                new SqlParameter("@RolPago", rolPagoDescripcion),
                new SqlParameter("@FechaInicio", fechaInicio)
                    };

                    try
                    {
                        // Ejecutar la actualización principal del usuario
                        DatabaseHelper.ExecuteNonQuery(query, parameters);

                        // Si la fecha de inicio cambió, ejecutar la actualización de reinicio
                        if (fechaInicioCambio)
                        {
                            string queryResetCiclo = @"UPDATE Usuarios 
                                              SET 
                                                  HorasExcedentesAcumuladas = 0,
                                                  UltimaFechaProcesada = NULL,
                                                  UltimoCalculo = NULL,
                                                  CiclosCompletados = NULL
                                              WHERE Id = @Id";

                            SqlParameter[] resetParams = new SqlParameter[]
                            {
                        new SqlParameter("@Id", id)
                            };

                            DatabaseHelper.ExecuteNonQuery(queryResetCiclo, resetParams);
                            TempData["Mensaje"] = "Usuario actualizado correctamente. Se ha reiniciado el ciclo de horas por cambio de fecha de inicio.";
                        }
                        else
                        {
                            TempData["Mensaje"] = "Usuario actualizado correctamente";
                        }
                    }
                    catch (Exception ex)
                    {
                        TempData["Error"] = "Error al actualizar el usuario: " + ex.Message;
                    }

                    return RedirectToAction("Usuarios");
                }


        [HttpPost]
        public JsonResult VerificarRegistrosUsuario(int id)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                // Verificar registros de asistencia
                string queryAsistencia = "SELECT COUNT(*) FROM RegistrosAsistencia WHERE UsuarioId = @Id";
                SqlParameter[] parameters = { new SqlParameter("@Id", id) };
                int totalAsistencia = Convert.ToInt32(DatabaseHelper.ExecuteScalar(queryAsistencia, parameters));

                // Verificar solicitudes excepcionales
                string querySolicitudes = "SELECT COUNT(*) FROM SolicitudesExcepcionales WHERE UsuarioId = @Id";
                int totalSolicitudes = Convert.ToInt32(DatabaseHelper.ExecuteScalar(querySolicitudes, parameters));

                int totalRegistros = totalAsistencia + totalSolicitudes;

                return Json(new { success = true, tieneRegistros = totalRegistros > 0, totalRegistros = totalRegistros });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult EliminarUsuarioConfirmado(int id)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                // 1. Eliminar registros de asistencia
                string deleteAsistencia = "DELETE FROM RegistrosAsistencia WHERE UsuarioId = @Id";
                DatabaseHelper.ExecuteNonQuery(deleteAsistencia, new SqlParameter[] { new SqlParameter("@Id", id) });

                // 2. Eliminar solicitudes excepcionales
                string deleteSolicitudes = "DELETE FROM SolicitudesExcepcionales WHERE UsuarioId = @Id";
                DatabaseHelper.ExecuteNonQuery(deleteSolicitudes, new SqlParameter[] { new SqlParameter("@Id", id) });

                // 3. Eliminar registros excepcionales (si existe la tabla)
                try
                {
                    string deleteExcepcionales = "DELETE FROM RegistrosExcepcionales WHERE UsuarioId = @Id";
                    DatabaseHelper.ExecuteNonQuery(deleteExcepcionales, new SqlParameter[] { new SqlParameter("@Id", id) });
                }
                catch { }

                // 4. Eliminar usuario
                string query = "DELETE FROM Usuarios WHERE Id = @Id";
                DatabaseHelper.ExecuteNonQuery(query, new SqlParameter[] { new SqlParameter("@Id", id) });

                return Json(new { success = true, message = "Usuario eliminado correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        public ActionResult EliminarUsuario(int id)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return RedirectToAction("Login", "Account");

            // Verificar si tiene registros
            string checkQuery = "SELECT COUNT(*) FROM RegistrosAsistencia WHERE UsuarioId = @Id";
            SqlParameter[] checkParams = { new SqlParameter("@Id", id) };
            int total = Convert.ToInt32(DatabaseHelper.ExecuteScalar(checkQuery, checkParams));

            if (total > 0)
            {
                TempData["Error"] = "No se puede eliminar el usuario porque tiene registros de asistencia asociados.";
                return RedirectToAction("Usuarios");
            }

            // Si no tiene registros, eliminar
            string query = "DELETE FROM Usuarios WHERE Id = @Id";
            DatabaseHelper.ExecuteNonQuery(query, checkParams);

            TempData["Mensaje"] = "Usuario eliminado correctamente";
            return RedirectToAction("Usuarios");
        }

        [HttpPost]
        public ActionResult ConfirmarEliminarUsuario(int id)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                // Primero eliminar registros relacionados (opcional, depende de tu lógica)
                // Si quieres mantener los registros pero solo eliminar el usuario, comenta estas líneas
                // string deleteRegistros = "DELETE FROM RegistrosAsistencia WHERE UsuarioId = @Id";
                // DatabaseHelper.ExecuteNonQuery(deleteRegistros, new SqlParameter[] { new SqlParameter("@Id", id) });

                // Eliminar usuario
                string query = "DELETE FROM Usuarios WHERE Id = @Id";
                SqlParameter[] parameters = { new SqlParameter("@Id", id) };
                DatabaseHelper.ExecuteNonQuery(query, parameters);

                return Json(new { success = true, message = "Usuario eliminado correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ============================================
        // DETALLES DASHBOARD (MODALES)
        // ============================================

        [HttpGet]
        public JsonResult ObtenerDetallePresentes()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { error = "No autorizado" }, JsonRequestBehavior.AllowGet);

            DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerDetallePresentes", null);
            var lista = new List<object>();
            foreach (DataRow row in dt.Rows)
            {
                lista.Add(new
                {
                    NombreCompleto = row["NombreCompleto"].ToString(),
                    Email = row["Email"].ToString(),
                    HoraEntrada = row["HoraEntrada"].ToString(),
                    Comentario = row["Comentario"] != DBNull.Value ? row["Comentario"].ToString() : ""
                });
            }
            return Json(lista, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ObtenerDetalleTardanzas()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { error = "No autorizado" }, JsonRequestBehavior.AllowGet);

            DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerDetalleTardanzas", null);
            var lista = new List<object>();
            foreach (DataRow row in dt.Rows)
            {
                lista.Add(new
                {
                    NombreCompleto = row["NombreCompleto"].ToString(),
                    Email = row["Email"].ToString(),
                    HoraEntrada = row["HoraEntrada"].ToString(),
                    MinutosTarde = Convert.ToInt32(row["MinutosTarde"]),
                    Comentario = row["Comentario"] != DBNull.Value ? row["Comentario"].ToString() : ""
                });
            }
            return Json(lista, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ObtenerDetalleAusentes()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { error = "No autorizado" }, JsonRequestBehavior.AllowGet);

            DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerDetalleAusentes", null);
            var lista = new List<object>();
            foreach (DataRow row in dt.Rows)
            {
                lista.Add(new
                {
                    Id = row["Id"],
                    NombreCompleto = row["NombreCompleto"].ToString(),
                    Email = row["Email"].ToString()
                });
            }
            return Json(lista, JsonRequestBehavior.AllowGet);
        }

        // ============================================
        // GESTIÓN DE PERMISOS EXCEPCIONALES
        // ============================================

        [HttpGet]
        public ActionResult GestionarPermisos()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return RedirectToAction("Login", "Account");

            string query = @"SELECT Id, NombreCompleto, Email, Rol, PermisoMarcacionExcepcional, TipoPermisoExcepcional 
                             FROM Usuarios  WHERE Rol != 'Admin' 
                            ORDER BY NombreCompleto";
            DataTable usuarios = DatabaseHelper.ExecuteQuery(query, null);
            return View(usuarios);
        }

        [HttpPost]
        public JsonResult ActualizarPermisoExcepcional(int usuarioId, bool habilitar, string tipoPermiso)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                if (string.IsNullOrEmpty(tipoPermiso))
                {
                    tipoPermiso = "Todos";
                }

                string query = @"UPDATE Usuarios 
                                 SET PermisoMarcacionExcepcional = @Habilitar, 
                                     TipoPermisoExcepcional = @TipoPermiso
                                 WHERE Id = @UsuarioId";

                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@Habilitar", habilitar),
                    new SqlParameter("@TipoPermiso", tipoPermiso),
                    new SqlParameter("@UsuarioId", usuarioId)
                };

                DatabaseHelper.ExecuteNonQuery(query, parameters);

                return Json(new { success = true, message = "Permiso actualizado correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult ObtenerPermisoExcepcional(int usuarioId)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { error = "No autorizado" }, JsonRequestBehavior.AllowGet);

            try
            {
                string query = @"SELECT PermisoMarcacionExcepcional, TipoPermisoExcepcional 
                                 FROM Usuarios WHERE Id = @UsuarioId";
                SqlParameter[] parameters = new SqlParameter[] { new SqlParameter("@UsuarioId", usuarioId) };
                DataTable dt = DatabaseHelper.ExecuteQuery(query, parameters);

                if (dt.Rows.Count > 0)
                {
                    return Json(new
                    {
                        success = true,
                        habilitado = Convert.ToBoolean(dt.Rows[0]["PermisoMarcacionExcepcional"]),
                        tipoPermiso = dt.Rows[0]["TipoPermisoExcepcional"].ToString()
                    }, JsonRequestBehavior.AllowGet);
                }

                return Json(new { success = false, error = "Usuario no encontrado" }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult RegistrarHoraExcepcional(int usuarioId, string tipoRegistro, DateTime fechaHora, string justificacion)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                int adminId = Convert.ToInt32(Session["UsuarioId"]);

                string deleteQuery = @"DELETE FROM RegistrosAsistencia 
                                       WHERE UsuarioId = @UsuarioId 
                                         AND TipoRegistro = @Tipo 
                                         AND CAST(FechaHora AS DATE) = CAST(@FechaHora AS DATE)";
                SqlParameter[] deleteParams = new SqlParameter[]
                {
                    new SqlParameter("@UsuarioId", usuarioId),
                    new SqlParameter("@Tipo", tipoRegistro),
                    new SqlParameter("@FechaHora", fechaHora)
                };
                DatabaseHelper.ExecuteNonQuery(deleteQuery, deleteParams);

                string insertQuery = @"INSERT INTO RegistrosAsistencia (UsuarioId, TipoRegistro, FechaHora, Comentario)
                                       VALUES (@UsuarioId, @Tipo, @FechaHora, @Comentario)";
                SqlParameter[] insertParams = new SqlParameter[]
                {
                    new SqlParameter("@UsuarioId", usuarioId),
                    new SqlParameter("@Tipo", tipoRegistro),
                    new SqlParameter("@FechaHora", fechaHora),
                    new SqlParameter("@Comentario", $"Registro excepcional autorizado por Admin. Justificación: {justificacion}")
                };
                DatabaseHelper.ExecuteNonQuery(insertQuery, insertParams);

                string excepcionalQuery = @"INSERT INTO RegistrosExcepcionales (UsuarioId, AdminId, TipoRegistro, FechaHora, Justificacion)
                                            VALUES (@UsuarioId, @AdminId, @Tipo, @FechaHora, @Justificacion)";
                SqlParameter[] excepcionalParams = new SqlParameter[]
                {
                    new SqlParameter("@UsuarioId", usuarioId),
                    new SqlParameter("@AdminId", adminId),
                    new SqlParameter("@Tipo", tipoRegistro),
                    new SqlParameter("@FechaHora", fechaHora),
                    new SqlParameter("@Justificacion", justificacion)
                };
                DatabaseHelper.ExecuteNonQuery(excepcionalQuery, excepcionalParams);

                return Json(new { success = true, message = "Hora registrada exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ============================================
        // GESTIÓN DE SOLICITUDES EXCEPCIONALES
        // ============================================

        [HttpGet]
        public ActionResult GestionarSolicitudes()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return RedirectToAction("Login", "Account");

            string query = @"SELECT s.Id, s.UsuarioId, u.NombreCompleto, u.Email, 
                                    s.TipoRegistro, s.FechaHoraSolicitada, s.Justificacion, 
                                    s.Estado, s.FechaSolicitud
                             FROM SolicitudesExcepcionales s
                             INNER JOIN Usuarios u ON s.UsuarioId = u.Id
                             WHERE s.Estado = 'Pendiente'
                             ORDER BY s.FechaSolicitud DESC";

            DataTable solicitudes = DatabaseHelper.ExecuteQuery(query, null);
            return View(solicitudes);
        }

        [HttpPost]
        public JsonResult AprobarSolicitudExcepcional(int solicitudId, bool aprobar, string comentarioAdmin)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                string getQuery = @"SELECT s.UsuarioId, s.TipoRegistro, s.FechaHoraSolicitada, s.Justificacion 
                                   FROM SolicitudesExcepcionales s WHERE s.Id = @Id";
                SqlParameter[] getParams = new SqlParameter[] { new SqlParameter("@Id", solicitudId) };
                DataTable dt = DatabaseHelper.ExecuteQuery(getQuery, getParams);

                if (dt.Rows.Count == 0)
                    return Json(new { success = false, message = "Solicitud no encontrada" });

                DataRow row = dt.Rows[0];
                int usuarioId = Convert.ToInt32(row["UsuarioId"]);
                string tipoRegistro = row["TipoRegistro"].ToString();
                DateTime fechaHora = Convert.ToDateTime(row["FechaHoraSolicitada"]);
                string justificacion = row["Justificacion"].ToString();

                if (aprobar)
                {
                    string deleteQuery = @"DELETE FROM RegistrosAsistencia 
                                           WHERE UsuarioId = @UsuarioId 
                                             AND TipoRegistro = @Tipo 
                                             AND CAST(FechaHora AS DATE) = CAST(@FechaHora AS DATE)";
                    SqlParameter[] deleteParams = new SqlParameter[]
                    {
                        new SqlParameter("@UsuarioId", usuarioId),
                        new SqlParameter("@Tipo", tipoRegistro),
                        new SqlParameter("@FechaHora", fechaHora)
                    };
                    DatabaseHelper.ExecuteNonQuery(deleteQuery, deleteParams);

                    string insertQuery = @"INSERT INTO RegistrosAsistencia (UsuarioId, TipoRegistro, FechaHora, Comentario)
                                           VALUES (@UsuarioId, @Tipo, @FechaHora, @Comentario)";
                    SqlParameter[] insertParams = new SqlParameter[]
                    {
                        new SqlParameter("@UsuarioId", usuarioId),
                        new SqlParameter("@Tipo", tipoRegistro),
                        new SqlParameter("@FechaHora", fechaHora),
                        new SqlParameter("@Comentario", $"Solicitud excepcional aprobada. Justificación original: {justificacion}")
                    };
                    DatabaseHelper.ExecuteNonQuery(insertQuery, insertParams);
                }

                string estado = aprobar ? "Aprobado" : "Rechazado";
                string updateSolicitud = @"UPDATE SolicitudesExcepcionales 
                                           SET Estado = @Estado, 
                                               AdminComentario = @Comentario,
                                               FechaAprobacion = GETDATE()
                                           WHERE Id = @Id";
                SqlParameter[] updateSolicitudParams = new SqlParameter[]
                {
                    new SqlParameter("@Estado", estado),
                    new SqlParameter("@Comentario", string.IsNullOrEmpty(comentarioAdmin) ? (object)DBNull.Value : comentarioAdmin),
                    new SqlParameter("@Id", solicitudId)
                };
                DatabaseHelper.ExecuteNonQuery(updateSolicitud, updateSolicitudParams);

                return Json(new { success = true, message = $"Solicitud {estado.ToLower()} correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult ObtenerNotificacionesAdmin()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No tiene permisos", unauthorized = true }, JsonRequestBehavior.AllowGet);

            try
            {
                // Obtener SOLO las solicitudes PENDIENTES de la tabla SolicitudesExcepcionales
                string query = @"SELECT se.Id, se.UsuarioId, se.TipoRegistro, se.FechaHoraSolicitada, 
                                se.Justificacion, se.FechaSolicitud, se.Estado, u.NombreCompleto, u.Email
                         FROM SolicitudesExcepcionales se
                         INNER JOIN Usuarios u ON se.UsuarioId = u.Id
                         WHERE se.Estado = 'Pendiente'
                         ORDER BY se.FechaSolicitud DESC";

                DataTable dt = DatabaseHelper.ExecuteQuery(query, null);

                var notificaciones = new List<object>();
                foreach (DataRow row in dt.Rows)
                {
                    notificaciones.Add(new
                    {
                        Id = row["Id"],
                        UsuarioId = row["UsuarioId"],
                        NombreCompleto = row["NombreCompleto"].ToString(),
                        Email = row["Email"].ToString(),
                        TipoRegistro = row["TipoRegistro"].ToString(),
                        FechaHoraSolicitada = Convert.ToDateTime(row["FechaHoraSolicitada"]).ToString("dd/MM/yyyy HH:mm"),
                        Justificacion = row["Justificacion"].ToString(),
                        FechaSolicitud = Convert.ToDateTime(row["FechaSolicitud"]).ToString("dd/MM/yyyy HH:mm"),
                        Estado = row["Estado"].ToString()
                    });
                }

                return Json(new { success = true, data = notificaciones }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult ObtenerTotalSolicitudesPendientes()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, unauthorized = true }, JsonRequestBehavior.AllowGet);

            try
            {
                // Contar SOLO las solicitudes con Estado = 'Pendiente'
                string query = "SELECT COUNT(*) FROM SolicitudesExcepcionales WHERE Estado = 'Pendiente'";
                int total = Convert.ToInt32(DatabaseHelper.ExecuteScalar(query, null));

                return Json(new { success = true, count = total }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult MarcarNotificacionLeida(int notificacionId)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                string query = "UPDATE NotificacionesAdmin SET Leido = 1 WHERE Id = @Id";
                SqlParameter[] parameters = new SqlParameter[] { new SqlParameter("@Id", notificacionId) };
                DatabaseHelper.ExecuteNonQuery(query, parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ============================================
        // ALERTAS DE HORAS COMPLETADAS
        // ============================================

        [HttpGet]
        public JsonResult ObtenerAlertasHoras()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No tiene permisos para acceder a esta información", unauthorized = true }, JsonRequestBehavior.AllowGet);

            try
            {
                DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerAlertasHoras", new SqlParameter[] { new SqlParameter("@SoloNoLeidas", 1) });

                var alertas = new List<object>();
                foreach (DataRow row in dt.Rows)
                {
                    alertas.Add(new
                    {
                        Id = row["Id"],
                        UsuarioId = row["UsuarioId"],
                        NombreCompleto = row["NombreCompleto"].ToString(),
                        Email = row["Email"].ToString(),
                        RolPago = row["RolPago"].ToString(),
                        HorasAlcanzadas = Math.Round(Convert.ToDecimal(row["HorasAlcanzadas"]), 2),
                        HorasObjetivo = Convert.ToDecimal(row["HorasObjetivo"]),
                        FechaAlerta = Convert.ToDateTime(row["FechaAlerta"]).ToString("dd/MM/yyyy HH:mm"),
                        Estado = row["Estado"].ToString()
                    });
                }

                return Json(new { success = true, data = alertas, count = alertas.Count }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult MarcarAlertaLeida(int alertaId)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                SqlParameter[] parameters = new SqlParameter[] { new SqlParameter("@AlertaId", alertaId) };
                DatabaseHelper.ExecuteStoredProcedure("sp_MarcarAlertaLeida", parameters);
                return Json(new { success = true, message = "Alerta marcada como leída" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult MarcarTodasAlertasLeidas()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                string query = "UPDATE AlertasHoras SET Leido = 1 WHERE Leido = 0";
                DatabaseHelper.ExecuteNonQuery(query, null);
                return Json(new { success = true, message = "Todas las alertas marcadas como leídas" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult VerificarHorasFacilitadores()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                DatabaseHelper.ExecuteStoredProcedure("sp_VerificarHorasFacilitadores", null);
                return Json(new { success = true, message = "Verificación completada" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ============================================
        // REPORTES AVANZADOS
        // ============================================

        [HttpGet]
        public ActionResult Reportes()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return RedirectToAction("Login", "Account");

            CargarListasReportes();
            return View();
        }

        private void CargarListasReportes()
        {
            string queryUsuarios = "SELECT Id, NombreCompleto FROM Usuarios WHERE Activo = 1 ORDER BY NombreCompleto";
            ViewBag.Usuarios = DatabaseHelper.ExecuteQuery(queryUsuarios, null);

            DataTable roles = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerRoles", null);
            ViewBag.Roles = roles;
        }

        [HttpPost]
        public JsonResult ObtenerReporteHoras(DateTime fechaInicio, DateTime fechaFin, int? usuarioId, string rol)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@FechaInicio", fechaInicio),
                    new SqlParameter("@FechaFin", fechaFin),
                    new SqlParameter("@UsuarioId", usuarioId.HasValue ? (object)usuarioId.Value : DBNull.Value),
                    new SqlParameter("@Rol", string.IsNullOrEmpty(rol) ? (object)DBNull.Value : rol)
                };

                DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerReporteHorasPorUsuario", parameters);

                var reporte = new List<object>();
                foreach (DataRow row in dt.Rows)
                {
                    reporte.Add(new
                    {
                        UsuarioId = row["UsuarioId"],
                        NombreCompleto = row["NombreCompleto"].ToString(),
                        Email = row["Email"].ToString(),
                        Rol = row["Rol"].ToString(),
                        Fecha = row["Fecha"] != DBNull.Value ? Convert.ToDateTime(row["Fecha"]).ToString("dd/MM/yyyy") : "",
                        HoraEntrada = row["HoraEntrada"].ToString(),
                        HoraSalidaAlmuerzo = row["HoraSalidaAlmuerzo"].ToString(),
                        HoraRetornoAlmuerzo = row["HoraRetornoAlmuerzo"].ToString(),
                        HoraSalida = row["HoraSalida"].ToString(),
                        HorasTrabajadasDia = row["HorasTrabajadasDia"] != DBNull.Value ? Convert.ToDecimal(row["HorasTrabajadasDia"]) : 0,
                        EstadoDia = row["EstadoDia"].ToString(),
                        Comentarios = row["Comentarios"].ToString()
                    });
                }

                return Json(new { success = true, data = reporte }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult ObtenerResumenHoras(DateTime fechaInicio, DateTime fechaFin, string usuarioId, string rol)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@FechaInicio", fechaInicio),
            new SqlParameter("@FechaFin", fechaFin),
            new SqlParameter("@UsuarioId", string.IsNullOrEmpty(usuarioId) ? DBNull.Value : (object)Convert.ToInt32(usuarioId)),
            new SqlParameter("@Rol", string.IsNullOrEmpty(rol) ? DBNull.Value : (object)rol)
                };

                DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerResumenHorasPorUsuario", parameters);

                var resumen = new List<object>();
                foreach (DataRow row in dt.Rows)
                {
                    string conceptoPago = row["ConceptoPago"] != DBNull.Value ? row["ConceptoPago"].ToString() : "";
                    decimal horasTrabajadas = row["TotalHorasTrabajadas"] != DBNull.Value ? Convert.ToDecimal(row["TotalHorasTrabajadas"]) : 0;
                    int diasTrabajados = row["DiasTrabajados"] != DBNull.Value ? Convert.ToInt32(row["DiasTrabajados"]) : 0;

                    // Calcular horas esperadas con la nueva lógica
                    decimal horasEsperadas = CalcularHorasEsperadas(conceptoPago, fechaInicio, fechaFin);
                    decimal diferencia = horasTrabajadas - horasEsperadas;

                    resumen.Add(new
                    {
                        NombreCompleto = row["NombreCompleto"].ToString(),
                        Email = row["Email"].ToString(),
                        Rol = row["Rol"].ToString(),
                        ConceptoPago = conceptoPago,
                        DiasTrabajados = diasTrabajados,
                        TotalHorasTrabajadas = Math.Round(horasTrabajadas, 2),
                        HorasEsperadas = Math.Round(horasEsperadas, 2),
                        DiferenciaHoras = Math.Round(diferencia, 2)
                    });
                }

                return Json(new { success = true, data = resumen });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        private decimal CalcularHorasEsperadas(string conceptoPago, DateTime fechaInicio, DateTime fechaFin)
        {
            int diasLaborables = 0;
            int diasSabado = 0;

            for (DateTime fecha = fechaInicio; fecha <= fechaFin; fecha = fecha.AddDays(1))
            {
                // Lunes a Viernes = 1-5, Sábado = 6
                if ((int)fecha.DayOfWeek >= 1 && (int)fecha.DayOfWeek <= 5)
                {
                    diasLaborables++;
                }
                else if ((int)fecha.DayOfWeek == 6) // Sábado
                {
                    diasSabado++;
                }
            }

            switch (conceptoPago)
            {
                case "Facilitador 1":
                    return Math.Min(4 * (diasLaborables + diasSabado), 104);
                case "Facilitador 2":
                    return Math.Min(4 * (diasLaborables + diasSabado), 104);
                case "Facilitador 3":
                    return Math.Min(4 * (diasLaborables + diasSabado), 96);
                case "Planilla Full Time":
                    return Math.Min(8 * (diasLaborables + diasSabado), 192);
                case "Planilla Part Time":
                    // Planilla Part Time: Lunes a Viernes 4 horas, Sábado 3 horas
                    decimal horasEsperadas = (diasLaborables * 4) + (diasSabado * 3);
                    return Math.Min(horasEsperadas, 92);
                default:
                    return Math.Min(4 * (diasLaborables + diasSabado), 104);
            }
        }


        // ============================================
        // REPORTE DE PAGOS
        // ============================================

        [HttpGet]
        public ActionResult ReportePagos()
        {
            DateTime primerDiaMes = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            ViewBag.FechaInicio = primerDiaMes.ToString("yyyy-MM-dd");
            ViewBag.FechaFin = DateTime.Now.ToString("yyyy-MM-dd");

            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return RedirectToAction("Login", "Account");

            CargarListasPagos();
            return View();
        }

        private void CargarListasPagos()
        {
            string queryConceptos = @"SELECT Id, ConceptoPago, Tipo, TarifaHora, HorasDiarias, HorasMensuales 
                                       FROM ConfiguracionPagos 
                                       WHERE Activo = 1 
                                       ORDER BY Tipo, ConceptoPago";
            ViewBag.ConceptosPago = DatabaseHelper.ExecuteQuery(queryConceptos, null);

            string queryUsuarios = "SELECT Id, NombreCompleto, RolPago FROM Usuarios WHERE  NombreCompleto !='admin'and  Activo = 1 ORDER BY NombreCompleto";
            ViewBag.Usuarios = DatabaseHelper.ExecuteQuery(queryUsuarios, null);

            string queryRoles = "SELECT DISTINCT RolPago FROM Usuarios WHERE Activo = 1 AND RolPago IS NOT NULL ORDER BY RolPago";
            ViewBag.RolesPago = DatabaseHelper.ExecuteQuery(queryRoles, null);
        }

        [HttpPost]
        public JsonResult ObtenerReportePagos(DateTime fechaInicio, DateTime fechaFin, int? usuarioId, string rolPago)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@FechaInicio", fechaInicio),
                    new SqlParameter("@FechaFin", fechaFin),
                    new SqlParameter("@UsuarioId", usuarioId.HasValue ? (object)usuarioId.Value : DBNull.Value),
                    new SqlParameter("@RolPago", string.IsNullOrEmpty(rolPago) ? (object)DBNull.Value : rolPago)
                };

                DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerReportePagoHoras", parameters);

                var reporte = new List<object>();
                foreach (DataRow row in dt.Rows)
                {
                    reporte.Add(new
                    {
                        UsuarioId = row["UsuarioId"],
                        NombreCompleto = row["NombreCompleto"].ToString(),
                        Email = row["Email"].ToString(),
                        RolPago = row["RolPago"].ToString(),
                        TarifaHora = Convert.ToDecimal(row["TarifaHora"]),
                        DiasTrabajados = Convert.ToInt32(row["DiasTrabajados"]),
                        TotalHoras = Convert.ToDecimal(row["TotalHoras"]),
                        MontoTotal = Convert.ToDecimal(row["MontoTotal"]),
                        DetalleHoras = row["DetalleHoras"]?.ToString()
                    });
                }

                return Json(new { success = true, data = reporte }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult ObtenerResumenPagosPorRol(DateTime fechaInicio, DateTime fechaFin)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, error = "No autorizado" });

            try
            {
                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@FechaInicio", fechaInicio),
                    new SqlParameter("@FechaFin", fechaFin)
                };

                DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerResumenPagosPorRol", parameters);

                var resumen = new List<object>();
                foreach (DataRow row in dt.Rows)
                {
                    resumen.Add(new
                    {
                        RolPago = row["RolPago"].ToString(),
                        CantidadEmpleados = Convert.ToInt32(row["CantidadEmpleados"]),
                        TotalHoras = Convert.ToDecimal(row["TotalHoras"]),
                        MontoTotal = Convert.ToDecimal(row["MontoTotal"])
                    });
                }

                return Json(new { success = true, data = resumen }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult ObtenerDetallePagoUsuario(int usuarioId, DateTime fechaInicio, DateTime fechaFin)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { error = "No autorizado" }, JsonRequestBehavior.AllowGet);

            try
            {
                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@FechaInicio", fechaInicio),
                    new SqlParameter("@FechaFin", fechaFin),
                    new SqlParameter("@UsuarioId", usuarioId)
                };

                DataTable dt = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerDetallePagoPorUsuario", parameters);

                var detalle = new List<object>();
                foreach (DataRow row in dt.Rows)
                {
                    detalle.Add(new
                    {
                        Fecha = row["Fecha"] != DBNull.Value ? Convert.ToDateTime(row["Fecha"]).ToString("dd/MM/yyyy") : "",
                        HoraEntrada = row["HoraEntrada"]?.ToString(),
                        HoraSalida = row["HoraSalida"]?.ToString(),
                        SalidaAlmuerzo = row["SalidaAlmuerzo"]?.ToString(),
                        RetornoAlmuerzo = row["RetornoAlmuerzo"]?.ToString(),
                        HorasTrabajadas = Convert.ToDecimal(row["HorasTrabajadas"]),
                        TarifaHora = Convert.ToDecimal(row["TarifaHora"]),
                        MontoDia = Convert.ToDecimal(row["MontoDia"]),
                        Comentarios = row["Comentarios"]?.ToString()
                    });
                }

                return Json(new { success = true, data = detalle }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // ============================================
        // EXPORTAR EXCEL
        // ============================================

        [HttpGet]
        public ActionResult ExportarExcel(DateTime fechaInicio, DateTime fechaFin, int? usuarioId, string rol)
        {
            try
            {
                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@FechaInicio", fechaInicio),
            new SqlParameter("@FechaFin", fechaFin),
            new SqlParameter("@UsuarioId", usuarioId.HasValue ? (object)usuarioId.Value : DBNull.Value),
            new SqlParameter("@Rol", string.IsNullOrEmpty(rol) ? (object)DBNull.Value : rol)
                };

                DataTable reporteHoras = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerReporteHorasPorUsuario", parameters);
                DataTable resumenHoras = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerResumenHorasPorUsuario", parameters);

                using (var package = new ExcelPackage())
                {
                    // ============================================
                    // HOJA 1: REPORTE DE ASISTENCIA
                    // ============================================
                    var hojaReporte = package.Workbook.Worksheets.Add("Reporte Asistencia");

                    Color colorCeleste = Color.FromArgb(0, 176, 240);
                    Color colorEncabezado = Color.FromArgb(192, 0, 0);

                    string logoIzquierdoPath = Server.MapPath("~/Content/images/logo-empresa.png");
                    string logoDerechoPath = Server.MapPath("~/Content/images/Logo_Edificio.png");

                    // ============================================
                    // FILA 2: SUBTÍTULO
                    // ============================================
                    hojaReporte.Cells["A2"].Value = $"Período: {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy} - Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";
                    hojaReporte.Cells["A2"].Style.Font.Name = "Calibri";
                    hojaReporte.Cells["A2"].Style.Font.Size = 11;
                    hojaReporte.Cells["A2"].Style.Font.Bold = true;
                    hojaReporte.Cells["A2"].Style.Font.Italic = true;
                    hojaReporte.Cells["A2"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaReporte.Cells["A2"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(211, 211, 211));
                    hojaReporte.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    hojaReporte.Row(2).Height = 17;

                    // ============================================
                    // FILA 4: ENCABEZADOS
                    // ============================================
                    int headerRow = 4;
                    string[] headers = { "N°", "Empleado", "Email", "Rol", "Fecha", "Entrada", "Salida Almuerzo", "Retorno Almuerzo", "Salida", "Horas", "Estado", "Observaciones" };

                    for (int i = 0; i < headers.Length; i++)
                    {
                        hojaReporte.Cells[headerRow, i + 1].Value = headers[i];
                        hojaReporte.Cells[headerRow, i + 1].Style.Font.Bold = true;
                        hojaReporte.Cells[headerRow, i + 1].Style.Font.Color.SetColor(Color.White);
                        hojaReporte.Cells[headerRow, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hojaReporte.Cells[headerRow, i + 1].Style.Fill.BackgroundColor.SetColor(colorEncabezado);
                        hojaReporte.Cells[headerRow, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        hojaReporte.Cells[headerRow, i + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        hojaReporte.Cells[headerRow, i + 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    }
                    hojaReporte.Row(headerRow).Height = 25;

                    // ============================================
                    // DATOS
                    // ============================================
                    int row = headerRow + 1;
                    int numero = 1;

                    foreach (DataRow dr in reporteHoras.Rows)
                    {
                        if (dr["Fecha"] != null && !string.IsNullOrEmpty(dr["Fecha"].ToString()))
                        {
                            hojaReporte.Cells[row, 1].Value = numero++;
                            hojaReporte.Cells[row, 2].Value = dr["NombreCompleto"].ToString();
                            hojaReporte.Cells[row, 3].Value = dr["Email"].ToString();
                            hojaReporte.Cells[row, 4].Value = dr["Rol"].ToString();

                            if (dr["Fecha"] != DBNull.Value && DateTime.TryParse(dr["Fecha"].ToString(), out DateTime fecha))
                            {
                                hojaReporte.Cells[row, 5].Value = fecha.ToString("dd/MM/yyyy");
                            }

                            hojaReporte.Cells[row, 6].Value = dr["HoraEntrada"]?.ToString() ?? "-";
                            hojaReporte.Cells[row, 7].Value = dr["HoraSalidaAlmuerzo"]?.ToString() ?? "-";
                            hojaReporte.Cells[row, 8].Value = dr["HoraRetornoAlmuerzo"]?.ToString() ?? "-";
                            hojaReporte.Cells[row, 9].Value = dr["HoraSalida"]?.ToString() ?? "-";
                            hojaReporte.Cells[row, 10].Value = dr["HorasTrabajadasDia"] != DBNull.Value ? Convert.ToDecimal(dr["HorasTrabajadasDia"]) : 0;
                            hojaReporte.Cells[row, 11].Value = dr["EstadoDia"].ToString();
                            hojaReporte.Cells[row, 12].Value = dr["Comentarios"]?.ToString() ?? "";

                            hojaReporte.Cells[row, 10].Style.Numberformat.Format = "0.00";
                            hojaReporte.Cells[row, 10].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                            string estado = dr["EstadoDia"].ToString();
                            if (estado == "Tardanza")
                            {
                                hojaReporte.Cells[row, 11].Style.Font.Color.SetColor(Color.White);
                                hojaReporte.Cells[row, 11].Style.Font.Bold = true;
                                hojaReporte.Cells[row, 11].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                hojaReporte.Cells[row, 11].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 102, 0));
                                hojaReporte.Cells[row, 11].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                                hojaReporte.Cells[row, 11].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                            }
                            else if (estado == "Ausente")
                            {
                                hojaReporte.Cells[row, 11].Style.Font.Color.SetColor(Color.Black);
                                hojaReporte.Cells[row, 11].Style.Font.Bold = true;
                                hojaReporte.Cells[row, 11].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                hojaReporte.Cells[row, 11].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(191, 191, 191));
                                hojaReporte.Cells[row, 11].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                                hojaReporte.Cells[row, 11].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                            }
                            else if (estado == "Normal")
                            {
                                hojaReporte.Cells[row, 11].Style.Font.Color.SetColor(Color.White);
                                hojaReporte.Cells[row, 11].Style.Font.Bold = true;
                                hojaReporte.Cells[row, 11].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                hojaReporte.Cells[row, 11].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0, 176, 80));
                                hojaReporte.Cells[row, 11].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                                hojaReporte.Cells[row, 11].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                            }
                            row++;
                        }
                    }

                    if (row > headerRow + 1)
                    {
                        hojaReporte.Cells[headerRow, 1, row - 1, 12].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        hojaReporte.Cells[headerRow, 1, row - 1, 12].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        hojaReporte.Cells[headerRow, 1, row - 1, 12].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        hojaReporte.Cells[headerRow, 1, row - 1, 12].Style.Border.Right.Style = ExcelBorderStyle.Thin;
                    }

                    // ============================================
                    // AJUSTAR ANCHO DE COLUMNAS
                    // ============================================
                    for (int i = 1; i <= 12; i++)
                    {
                        hojaReporte.Column(i).AutoFit();
                    }

                    int maxNumero = 0;
                    for (int r = 5; r < row; r++)
                    {
                        if (hojaReporte.Cells[r, 1].Value != null)
                        {
                            int valor = Convert.ToInt32(hojaReporte.Cells[r, 1].Value);
                            if (valor > maxNumero) maxNumero = valor;
                        }
                    }

                    int anchoColumnaA = maxNumero.ToString().Length + 2;
                    if (anchoColumnaA < 4) anchoColumnaA = 4;
                    if (anchoColumnaA > 8) anchoColumnaA = 8;
                    hojaReporte.Column(1).Width = anchoColumnaA;

                    if (hojaReporte.Column(12).Width < 40) hojaReporte.Column(12).Width = 40;
                    if (hojaReporte.Column(2).Width < 25) hojaReporte.Column(2).Width = 25;
                    if (hojaReporte.Column(3).Width < 30) hojaReporte.Column(3).Width = 30;
                    if (hojaReporte.Column(4).Width < 15) hojaReporte.Column(4).Width = 15;
                    if (hojaReporte.Column(10).Width < 8) hojaReporte.Column(10).Width = 8;

                    // ============================================
                    // APLICAR COLOR CELESTE A LA FILA 1
                    // ============================================
                    int ultimaColumna = 12;
                    string rangoFila1 = $"A1:{GetColumnLetter(ultimaColumna)}1";
                    hojaReporte.Cells[rangoFila1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaReporte.Cells[rangoFila1].Style.Fill.BackgroundColor.SetColor(colorCeleste);

                    // TÍTULO CENTRADO
                    string rangoTitulo = $"B1:{GetColumnLetter(ultimaColumna - 1)}1";
                    hojaReporte.Cells[rangoTitulo].Merge = true;
                    hojaReporte.Cells["B1"].Value = "REPORTE DE ASISTENCIA";
                    hojaReporte.Cells["B1"].Style.Font.Name = "Arial";  // Fuente Arial
                    hojaReporte.Cells["B1"].Style.Font.Size = 18;
                    hojaReporte.Cells["B1"].Style.Font.Bold = true;
                    hojaReporte.Cells["B1"].Style.Font.Italic = false;  // Sin cursiva
                    hojaReporte.Cells["B1"].Style.Font.Color.SetColor(Color.Black);
                    hojaReporte.Cells["B1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    hojaReporte.Cells["B1"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    // ============================================
                    // TÍTULO CENTRADO (FILA 1) - DESDE A HASTA L
                    // ============================================
                    // Aplicar color de fondo a toda la fila 1 de A a L
                    hojaReporte.Row(1).Height = 39;
                    hojaReporte.Cells[$"A1:{GetColumnLetter(ultimaColumna)}1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaReporte.Cells[$"A1:{GetColumnLetter(ultimaColumna)}1"].Style.Fill.BackgroundColor.SetColor(colorCeleste);

                    // Combinar celdas desde A hasta L
                    hojaReporte.Cells[$"A1:{GetColumnLetter(ultimaColumna)}1"].Merge = true;
                    hojaReporte.Cells["A1"].Value = "REPORTE DE ASISTENCIA";
                    hojaReporte.Cells["A1"].Style.Font.Name = "Arial";  // Fuente Arial
                    hojaReporte.Cells["A1"].Style.Font.Size = 18;
                    hojaReporte.Cells["A1"].Style.Font.Bold = true;
                    hojaReporte.Cells["A1"].Style.Font.Italic = false;  // Sin cursiva
                    hojaReporte.Cells["A1"].Style.Font.Color.SetColor(Color.Black);
                    hojaReporte.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    hojaReporte.Cells["A1"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    // ============================================
                    // INSERTAR LOGOS
                    // ============================================
                    if (System.IO.File.Exists(logoIzquierdoPath))
                    {
                        var logoImage = new FileInfo(logoIzquierdoPath);
                        var picture = hojaReporte.Drawings.AddPicture("LogoIzquierdo", logoImage);
                        picture.SetPosition(0, 5, 0, 90);
                        picture.SetSize(100, 45);
                    }

                    if (System.IO.File.Exists(logoDerechoPath))
                    {
                        var logoImageDerecho = new FileInfo(logoDerechoPath);
                        var pictureDerecho = hojaReporte.Drawings.AddPicture("LogoDerecho", logoImageDerecho);
                        int columnaDerecha = 12;
                        double anchoColumnaK = hojaReporte.Column(columnaDerecha + 1).Width;
                        double anchoColumnaKPixeles = anchoColumnaK * 7;
                        double anchoLogo = 100;
                        double offsetX = anchoColumnaKPixeles - anchoLogo - 30;
                        if (offsetX < 0) offsetX = 0;
                        pictureDerecho.SetPosition(0, (int)offsetX, columnaDerecha, -50);
                        pictureDerecho.SetSize(100, 38);
                    }

                    // ============================================
                    // SUBTÍTULO (FILA 2)
                    // ============================================
                    string rangoSubtitulo = $"A2:{GetColumnLetter(ultimaColumna)}2";
                    hojaReporte.Cells[rangoSubtitulo].Merge = true;
                    hojaReporte.View.FreezePanes(5, 1);
                    // ============================================
                    // HOJA 2: RESUMEN POR EMPLEADO
                    // ============================================
                    var hojaResumen = package.Workbook.Worksheets.Add("Resumen por Empleado");

                    hojaResumen.Column(1).Width = 6;
                    hojaResumen.Column(2).Width = 28;
                    hojaResumen.Column(3).Width = 32;
                    hojaResumen.Column(4).Width = 15;
                    hojaResumen.Column(5).Width = 18;
                    hojaResumen.Column(6).Width = 18;
                    hojaResumen.Column(7).Width = 18;
                    hojaResumen.Column(8).Width = 18;

                    hojaResumen.Row(1).Height = 35;
                    string rangoTituloResumen = $"A1:I1";
                    hojaResumen.Cells[rangoTituloResumen].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaResumen.Cells[rangoTituloResumen].Style.Fill.BackgroundColor.SetColor(colorCeleste);
                    hojaResumen.Cells[rangoTituloResumen].Merge = true;
                    hojaResumen.Cells["A1"].Value = "RESUMEN DE HORAS POR EMPLEADO";
                    hojaResumen.Cells["A1"].Style.Font.Size = 16;
                    hojaResumen.Cells["A1"].Style.Font.Bold = true;
                    hojaResumen.Cells["A1"].Style.Font.Color.SetColor(Color.Black);
                    hojaResumen.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    hojaResumen.Cells["A1"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    // LOGOS EN RESUMEN
                    if (System.IO.File.Exists(logoIzquierdoPath))
                    {
                        var logoImageIzqResumen = new FileInfo(logoIzquierdoPath);
                        var pictureIzqResumen = hojaResumen.Drawings.AddPicture("LogoIzquierdoResumen", logoImageIzqResumen);
                        pictureIzqResumen.SetPosition(0, 5, 0, 90);
                        pictureIzqResumen.SetSize(100, 45); 
                    }

                    if (System.IO.File.Exists(logoDerechoPath))
                    {
                        var logoImageDerechoResumen = new FileInfo(logoDerechoPath);
                        var pictureDerechoResumen = hojaResumen.Drawings.AddPicture("LogoDerechoResumen", logoImageDerechoResumen);
                        int columnaDerechaResumen = 8;
                        double anchoColumnaH = hojaResumen.Column(columnaDerechaResumen + 1).Width;
                        double anchoColumnaHPixeles = anchoColumnaH * 7;
                        double anchoLogoResumen = 90;
                        double offsetXResumen = anchoColumnaHPixeles - anchoLogoResumen - 30;
                        if (offsetXResumen < 0) offsetXResumen = 0;
                        pictureDerechoResumen.SetPosition(0, (int)offsetXResumen, columnaDerechaResumen, -5);
                        pictureDerechoResumen.SetSize(90, 32);
                    }

                    hojaResumen.Row(2).Height = 18;
                    string rangoSubtituloResumen = $"A2:I2";
                    hojaResumen.Cells[rangoSubtituloResumen].Merge = true;
                    hojaResumen.Cells["A2"].Value = $"Período: {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}";
                    hojaResumen.Cells["A2"].Style.Font.Name = "Calibri";
                    hojaResumen.Cells["A2"].Style.Font.Size = 11;
                    hojaResumen.Cells["A2"].Style.Font.Bold = true;
                    hojaResumen.Cells["A2"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaResumen.Cells["A2"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(211, 211, 211));
                    hojaResumen.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    hojaResumen.Cells["A2"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    // ENCABEZADOS RESUMEN
                    string[] headersResumen = { "N°", "Empleado", "Email", "Concepto Pago", "Días Trabajados", "Horas Trabajadas", "Horas Convertidas", "Horas Esperadas", "Diferencia" };

                    for (int i = 0; i < 4; i++)
                    {
                        hojaResumen.Cells[4, i + 1].Value = headersResumen[i];
                        hojaResumen.Cells[4, i + 1].Style.Font.Bold = true;
                        hojaResumen.Cells[4, i + 1].Style.Font.Size = 10;
                        hojaResumen.Cells[4, i + 1].Style.Font.Color.SetColor(Color.White);
                        hojaResumen.Cells[4, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hojaResumen.Cells[4, i + 1].Style.Fill.BackgroundColor.SetColor(colorEncabezado);
                        hojaResumen.Cells[4, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        hojaResumen.Cells[4, i + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    }

                    hojaResumen.Cells[4, 5].Value = headersResumen[4];
                    hojaResumen.Cells[4, 5].Style.Font.Bold = true;
                    hojaResumen.Cells[4, 5].Style.Font.Size = 10;
                    hojaResumen.Cells[4, 5].Style.Font.Color.SetColor(Color.Black);
                    hojaResumen.Cells[4, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaResumen.Cells[4, 5].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 192, 0));
                    hojaResumen.Cells[4, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaResumen.Cells[4, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    hojaResumen.Cells[4, 6].Value = headersResumen[5];
                    hojaResumen.Cells[4, 6].Style.Font.Bold = true;
                    hojaResumen.Cells[4, 6].Style.Font.Size = 10;
                    hojaResumen.Cells[4, 6].Style.Font.Color.SetColor(Color.White);
                    hojaResumen.Cells[4, 6].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaResumen.Cells[4, 6].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0, 176, 80));
                    hojaResumen.Cells[4, 6].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaResumen.Cells[4, 6].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    hojaResumen.Cells[4, 7].Value = headersResumen[6];
                    hojaResumen.Cells[4, 7].Style.Font.Bold = true;
                    hojaResumen.Cells[4, 7].Style.Font.Size = 10;
                    hojaResumen.Cells[4, 7].Style.Font.Color.SetColor(Color.White);
                    hojaResumen.Cells[4, 7].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaResumen.Cells[4, 7].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 102, 0));
                    hojaResumen.Cells[4, 7].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaResumen.Cells[4, 7].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    hojaResumen.Cells[4, 8].Value = headersResumen[7];
                    hojaResumen.Cells[4, 8].Style.Font.Bold = true;
                    hojaResumen.Cells[4, 8].Style.Font.Size = 10;
                    hojaResumen.Cells[4, 8].Style.Font.Color.SetColor(Color.White);
                    hojaResumen.Cells[4, 8].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaResumen.Cells[4, 8].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0, 112, 192));
                    hojaResumen.Cells[4, 8].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaResumen.Cells[4, 8].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    hojaResumen.Cells[4, 9].Value = headersResumen[8];
                    hojaResumen.Cells[4, 9].Style.Font.Bold = true;
                    hojaResumen.Cells[4, 9].Style.Font.Size = 10;
                    hojaResumen.Cells[4, 9].Style.Font.Color.SetColor(Color.White);
                    hojaResumen.Cells[4, 9].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaResumen.Cells[4, 9].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0, 112, 192));
                    hojaResumen.Cells[4, 9].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaResumen.Cells[4, 9].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    hojaResumen.Row(4).Height = 18;

                    // ============================================
                    // CALCULAR DÍAS LABORABLES Y SÁBADOS EN EL RANGO SELECCIONADO
                    // ============================================
                    int diasLaborables = 0;
                    int diasSabado = 0;

                    for (DateTime fecha = fechaInicio; fecha <= fechaFin; fecha = fecha.AddDays(1))
                    {
                        // Lunes a Viernes = 1-5
                        if ((int)fecha.DayOfWeek >= 1 && (int)fecha.DayOfWeek <= 5)
                        {
                            diasLaborables++;
                        }
                        // Sábado = 6
                        else if ((int)fecha.DayOfWeek == 6)
                        {
                            diasSabado++;
                        }
                    }

                    // ============================================
                    // CREAR DICCIONARIO DE DATOS POR USUARIO
                    // ============================================
                    Dictionary<int, decimal> horasPorDiaPorUsuario = new Dictionary<int, decimal>();
                    Dictionary<int, decimal> topeMensualPorUsuario = new Dictionary<int, decimal>();
                    Dictionary<int, string> tipoUsuarioPorUsuario = new Dictionary<int, string>();

                    foreach (DataRow dr in resumenHoras.Rows)
                    {
                        int idUsuario = Convert.ToInt32(dr["UsuarioId"]);

                        if (!horasPorDiaPorUsuario.ContainsKey(idUsuario))
                        {
                            string conceptoPago = dr["ConceptoPago"] != DBNull.Value ? dr["ConceptoPago"].ToString() : "";

                            decimal horasPorDiaLaborable = 0;
                            decimal horasPorSabado = 0;
                            decimal topeMensual = 0;

                            switch (conceptoPago)
                            {
                                case "Facilitador 1":
                                    horasPorDiaLaborable = 4;
                                    horasPorSabado = 4;
                                    topeMensual = 104;
                                    break;
                                case "Facilitador 2":
                                    horasPorDiaLaborable = 4;
                                    horasPorSabado = 4;
                                    topeMensual = 104;
                                    break;
                                case "Facilitador 3":
                                    horasPorDiaLaborable = 4;
                                    horasPorSabado = 4;
                                    topeMensual = 96;
                                    break;
                                case "Planilla Full Time":
                                    horasPorDiaLaborable = 8;
                                    horasPorSabado = 8;
                                    topeMensual = 192;
                                    break;
                                case "Planilla Part Time":
                                    horasPorDiaLaborable = 4;  // Lunes a Viernes
                                    horasPorSabado = 3;        // Sábado
                                    topeMensual = 92;
                                    break;
                                default:
                                    horasPorDiaLaborable = 4;
                                    horasPorSabado = 4;
                                    topeMensual = 104;
                                    break;
                            }

                            // Guardar horas por día y tope mensual
                            horasPorDiaPorUsuario[idUsuario] = horasPorDiaLaborable;
                            // Guardar también horas de sábado en un diccionario separado
                            if (!horasPorDiaPorUsuario.ContainsKey(idUsuario + 1000))
                            {
                                horasPorDiaPorUsuario[idUsuario + 1000] = horasPorSabado;
                            }
                            topeMensualPorUsuario[idUsuario] = topeMensual;
                        }
                    }


                    // ============================================
                    // LLENAR DATOS DE RESUMEN
                    // ============================================
                    // ============================================
                    // LLENAR DATOS DE RESUMEN
                    // ============================================
                    int rowResumen = 5;
                    int numResumen = 1;

                    foreach (DataRow dr in resumenHoras.Rows)
                    {
                        int idUsuario = Convert.ToInt32(dr["UsuarioId"]);
                        decimal horasPorDiaLaborable = horasPorDiaPorUsuario.ContainsKey(idUsuario) ? horasPorDiaPorUsuario[idUsuario] : 4;
                        decimal horasPorSabado = horasPorDiaPorUsuario.ContainsKey(idUsuario + 1000) ? horasPorDiaPorUsuario[idUsuario + 1000] : 4;
                        decimal topeMensual = topeMensualPorUsuario.ContainsKey(idUsuario) ? topeMensualPorUsuario[idUsuario] : 104;
                        string conceptoPago = dr["ConceptoPago"] != DBNull.Value ? dr["ConceptoPago"].ToString() : "";

                        decimal horasTrabajadas = dr["TotalHorasTrabajadas"] != DBNull.Value ? Convert.ToDecimal(dr["TotalHorasTrabajadas"]) : 0;
                        int diasTrabajados = dr["DiasTrabajados"] != DBNull.Value ? Convert.ToInt32(dr["DiasTrabajados"]) : 0;

                        // Calcular horas esperadas
                        decimal calculoProporcional = (diasLaborables * horasPorDiaLaborable) + (diasSabado * horasPorSabado);
                        decimal horasEsperadas = Math.Min(calculoProporcional, topeMensual);
                        horasEsperadas = Math.Round(horasEsperadas, 2);

                        decimal diferencia = horasTrabajadas - horasEsperadas;

                        // Convertir horas trabajadas a formato legible
                        string horasConvertidas = ConvertirHorasATexto(horasTrabajadas);

                        hojaResumen.Cells[rowResumen, 1].Value = numResumen++;
                        hojaResumen.Cells[rowResumen, 2].Value = dr["NombreCompleto"].ToString();
                        hojaResumen.Cells[rowResumen, 3].Value = dr["Email"].ToString();
                        hojaResumen.Cells[rowResumen, 4].Value = conceptoPago;
                        hojaResumen.Cells[rowResumen, 5].Value = diasTrabajados;
                        hojaResumen.Cells[rowResumen, 6].Value = horasTrabajadas;
                        hojaResumen.Cells[rowResumen, 7].Value = horasConvertidas;  // NUEVA COLUMNA
                        hojaResumen.Cells[rowResumen, 7].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;  // ← 
                        hojaResumen.Cells[rowResumen, 8].Value = horasEsperadas;
                        hojaResumen.Cells[rowResumen, 9].Value = Math.Round(diferencia, 2);

                        // Formato
                        hojaResumen.Cells[rowResumen, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        hojaResumen.Cells[rowResumen, 6].Style.Numberformat.Format = "0.00";
                        hojaResumen.Cells[rowResumen, 8].Style.Numberformat.Format = "0.00";
                        hojaResumen.Cells[rowResumen, 9].Style.Numberformat.Format = "0.00";
                        hojaResumen.Cells[rowResumen, 9].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        if (diferencia < 0)
                        {
                            hojaResumen.Cells[rowResumen, 9].Style.Font.Color.SetColor(Color.Red);
                            hojaResumen.Cells[rowResumen, 9].Style.Font.Bold = true;
                        }
                        else if (diferencia > 0)
                        {
                            hojaResumen.Cells[rowResumen, 9].Style.Font.Color.SetColor(Color.Green);
                            hojaResumen.Cells[rowResumen, 9].Style.Font.Bold = true;
                        }
                        rowResumen++;
                    }

                    for (int i = 1; i <= 9; i++)
                    {
                        hojaResumen.Column(i).AutoFit();
                        if (hojaResumen.Column(i).Width > 35) hojaResumen.Column(i).Width = 35;
                    }
                    hojaResumen.Column(1).Width = 6;
                    hojaResumen.Column(5).Width = 18;
                    hojaResumen.Column(6).Width = 18;
                    hojaResumen.Column(7).Width = 35;  // Columna de Horas Convertidas más ancha
                    hojaResumen.Column(8).Width = 18;
                    hojaResumen.Column(9).Width = 18;

                    // ============================================
                    // HOJA 3: ESTADÍSTICAS - CÁLCULO CORREGIDO DE AUSENCIAS
                    // ============================================
                    var hojaEstadisticas = package.Workbook.Worksheets.Add("Estadísticas");

                    int totalRegistros = 0;
                    int totalTardanzas = 0;
                    int totalAusentes = 0;
                    decimal totalHoras = 0;

                    // ============================================
                    // CONTAR REGISTROS, TARDANZAS Y HORAS
                    // ============================================
                    foreach (DataRow dr in reporteHoras.Rows)
                    {
                        if (dr["Fecha"] != null && !string.IsNullOrEmpty(dr["Fecha"].ToString()))
                        {
                            totalRegistros++;
                            string estado = dr["EstadoDia"].ToString();

                            // Contar tardanzas
                            if (estado == "Tardanza")
                            {
                                totalTardanzas++;
                            }

                            // Contar horas trabajadas
                            if (dr["HorasTrabajadasDia"] != DBNull.Value)
                            {
                                totalHoras += Convert.ToDecimal(dr["HorasTrabajadasDia"]);
                            }
                        }
                    }

                    // ============================================
                    // CALCULAR AUSENCIAS: DÍAS ESPERADOS - DÍAS TRABAJADOS
                    // ============================================

                    // Diccionario para almacenar días laborables y sábados por usuario
                    Dictionary<int, int> dictDiasLaborables = new Dictionary<int, int>();
                    Dictionary<int, int> dictDiasSabado = new Dictionary<int, int>();
                    Dictionary<int, string> dictConceptoPago = new Dictionary<int, string>();

                    // Obtener los días en el período
                    int totalDiasLaborablesPeriodo = 0;
                    int totalDiasSabadoPeriodo = 0;

                    for (DateTime fechaActual = fechaInicio; fechaActual <= fechaFin; fechaActual = fechaActual.AddDays(1))
                    {
                        if ((int)fechaActual.DayOfWeek >= 1 && (int)fechaActual.DayOfWeek <= 5) // Lunes a Viernes
                        {
                            totalDiasLaborablesPeriodo++;
                        }
                        else if ((int)fechaActual.DayOfWeek == 6) // Sábado
                        {
                            totalDiasSabadoPeriodo++;
                        }
                    }

                    // Obtener el concepto de pago y calcular días esperados por cada usuario
                    foreach (DataRow dr in resumenHoras.Rows)
                    {
                        int idUsuarioActual = Convert.ToInt32(dr["UsuarioId"]);
                        string conceptoPago = dr["ConceptoPago"] != DBNull.Value ? dr["ConceptoPago"].ToString() : "";

                        if (!dictConceptoPago.ContainsKey(idUsuarioActual))
                        {
                            dictConceptoPago[idUsuarioActual] = conceptoPago;

                            int esperadosLaborables = 0;
                            int esperadosSabado = 0;

                            switch (conceptoPago)
                            {
                                case "Facilitador 1":
                                case "Facilitador 2":
                                case "Facilitador 3":
                                    // Trabajan Lunes a Sábado
                                    esperadosLaborables = totalDiasLaborablesPeriodo;
                                    esperadosSabado = totalDiasSabadoPeriodo;
                                    break;
                                case "Planilla Full Time":
                                    // Trabajan Lunes a Viernes
                                    esperadosLaborables = totalDiasLaborablesPeriodo;
                                    esperadosSabado = 0;
                                    break;
                                case "Planilla Part Time":
                                    // Trabajan Lunes a Viernes
                                    esperadosLaborables = totalDiasLaborablesPeriodo;
                                    esperadosSabado = 0;
                                    break;
                                default:
                                    // Por defecto Lunes a Viernes
                                    esperadosLaborables = totalDiasLaborablesPeriodo;
                                    esperadosSabado = 0;
                                    break;
                            }

                            dictDiasLaborables[idUsuarioActual] = esperadosLaborables;
                            dictDiasSabado[idUsuarioActual] = esperadosSabado;
                        }
                    }

                    // Calcular días trabajados por usuario (días que NO son ausentes)
                    Dictionary<int, int> dictDiasTrabajados = new Dictionary<int, int>();

                    foreach (DataRow dr in reporteHoras.Rows)
                    {
                        if (dr["Fecha"] != null && !string.IsNullOrEmpty(dr["Fecha"].ToString()))
                        {
                            string estado = dr["EstadoDia"].ToString();
                            int idUsuarioActual = Convert.ToInt32(dr["UsuarioId"]);

                            // Si NO es Ausente, cuenta como día trabajado
                            if (estado != "Ausente")
                            {
                                if (!dictDiasTrabajados.ContainsKey(idUsuarioActual))
                                {
                                    dictDiasTrabajados[idUsuarioActual] = 0;
                                }

                                dictDiasTrabajados[idUsuarioActual]++;
                            }
                        }
                    }

                    // Calcular ausencias totales
                    foreach (var usuario in dictConceptoPago)
                    {
                        int idUsuarioActual = usuario.Key;
                        int diasLaborablesEsperados = dictDiasLaborables.ContainsKey(idUsuarioActual) ? dictDiasLaborables[idUsuarioActual] : totalDiasLaborablesPeriodo;
                        int diasSabadoEsperados = dictDiasSabado.ContainsKey(idUsuarioActual) ? dictDiasSabado[idUsuarioActual] : 0;
                        int totalDiasEsperados = diasLaborablesEsperados + diasSabadoEsperados;
                        int totalDiasTrabajados = dictDiasTrabajados.ContainsKey(idUsuarioActual) ? dictDiasTrabajados[idUsuarioActual] : 0;

                        int ausenciasDelUsuario = totalDiasEsperados - totalDiasTrabajados;
                        if (ausenciasDelUsuario > 0)
                        {
                            totalAusentes += ausenciasDelUsuario;
                        }
                    }

                    // ============================================
                    // CREAR LA HOJA DE ESTADÍSTICAS
                    // ============================================

                    int ultimaColumnaEstadisticas = 12;
                    hojaEstadisticas.Row(1).Height = 35;
                    string rangoFila1Est = $"A1:{GetColumnLetter(ultimaColumnaEstadisticas)}1";
                    hojaEstadisticas.Cells[rangoFila1Est].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaEstadisticas.Cells[rangoFila1Est].Style.Fill.BackgroundColor.SetColor(colorCeleste);
                    hojaEstadisticas.Cells[rangoFila1Est].Merge = true;
                    hojaEstadisticas.Cells["A1"].Value = "ESTADÍSTICAS DEL REPORTE";
                    hojaEstadisticas.Cells["A1"].Style.Font.Size = 16;
                    hojaEstadisticas.Cells["A1"].Style.Font.Bold = true;
                    hojaEstadisticas.Cells["A1"].Style.Font.Color.SetColor(Color.Black);
                    hojaEstadisticas.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    hojaEstadisticas.Cells["A1"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    // LOGOS EN ESTADÍSTICAS
                    if (System.IO.File.Exists(logoIzquierdoPath))
                    {
                        var logoImageIzqEst = new FileInfo(logoIzquierdoPath);
                        var pictureIzqEst = hojaEstadisticas.Drawings.AddPicture("LogoIzquierdoEstadisticas", logoImageIzqEst);
                        pictureIzqEst.SetPosition(0, 5, 0, 40);
                        pictureIzqEst.SetSize(150, 42);
                    }

                    if (System.IO.File.Exists(logoDerechoPath))
                    {
                        var logoImageDerEst = new FileInfo(logoDerechoPath);
                        var pictureDerEst = hojaEstadisticas.Drawings.AddPicture("LogoDerechoEstadisticas", logoImageDerEst);
                        int columnaLogo = 10;
                        double anchoColumnaK = hojaEstadisticas.Column(columnaLogo + 1).Width;
                        double anchoColumnaKPixeles = anchoColumnaK * 7;
                        double anchoLogo = 90;
                        double offsetXDerechaEst = anchoColumnaKPixeles - anchoLogo - 10;
                        if (offsetXDerechaEst < 0) offsetXDerechaEst = 5;
                        pictureDerEst.SetPosition(0, (int)offsetXDerechaEst, columnaLogo, 2);
                        pictureDerEst.SetSize(90, 32);
                    }

                    hojaEstadisticas.Row(2).Height = 18;
                    string rangoSubtituloEst = $"A2:{GetColumnLetter(ultimaColumnaEstadisticas)}2";
                    hojaEstadisticas.Cells[rangoSubtituloEst].Merge = true;
                    hojaEstadisticas.Cells["A2"].Value = $"Período: {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}";
                    hojaEstadisticas.Cells["A2"].Style.Font.Name = "Calibri";
                    hojaEstadisticas.Cells["A2"].Style.Font.Size = 11;
                    hojaEstadisticas.Cells["A2"].Style.Font.Bold = true;
                    hojaEstadisticas.Cells["A2"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaEstadisticas.Cells["A2"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(211, 211, 211));
                    hojaEstadisticas.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    hojaEstadisticas.Cells["A2"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    // DATOS DE ESTADÍSTICAS
                    hojaEstadisticas.Cells[4, 1].Value = "Total de Registros";
                    hojaEstadisticas.Cells[4, 1].Style.Font.Bold = true;
                    hojaEstadisticas.Cells[4, 1].Style.Font.Size = 11;
                    hojaEstadisticas.Cells[4, 1].Style.Font.Color.SetColor(Color.Black);
                    hojaEstadisticas.Cells[4, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaEstadisticas.Cells[4, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 192, 0));
                    hojaEstadisticas.Cells[4, 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaEstadisticas.Cells[4, 2].Value = totalRegistros;
                    hojaEstadisticas.Cells[4, 2].Style.Font.Size = 11;
                    hojaEstadisticas.Cells[4, 2].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaEstadisticas.Cells[4, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    hojaEstadisticas.Cells[5, 1].Value = "Total de Tardanzas";
                    hojaEstadisticas.Cells[5, 1].Style.Font.Bold = true;
                    hojaEstadisticas.Cells[5, 1].Style.Font.Size = 11;
                    hojaEstadisticas.Cells[5, 1].Style.Font.Color.SetColor(Color.White);
                    hojaEstadisticas.Cells[5, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaEstadisticas.Cells[5, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(192, 0, 0));
                    hojaEstadisticas.Cells[5, 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaEstadisticas.Cells[5, 2].Value = totalTardanzas;
                    hojaEstadisticas.Cells[5, 2].Style.Font.Size = 11;
                    hojaEstadisticas.Cells[5, 2].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaEstadisticas.Cells[5, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    hojaEstadisticas.Cells[6, 1].Value = "Total de Ausencias";
                    hojaEstadisticas.Cells[6, 1].Style.Font.Bold = true;
                    hojaEstadisticas.Cells[6, 1].Style.Font.Size = 11;
                    hojaEstadisticas.Cells[6, 1].Style.Font.Color.SetColor(Color.Black);
                    hojaEstadisticas.Cells[6, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hojaEstadisticas.Cells[6, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(191, 191, 191));
                    hojaEstadisticas.Cells[6, 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaEstadisticas.Cells[6, 2].Value = totalAusentes;
                    hojaEstadisticas.Cells[6, 2].Style.Font.Size = 11;
                    hojaEstadisticas.Cells[6, 2].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    hojaEstadisticas.Cells[6, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
 

                    hojaEstadisticas.Column(1).Width = 28;
                    hojaEstadisticas.Column(2).Width = 18;

                    // GRÁFICO
                    var chart = hojaEstadisticas.Drawings.AddChart("GraficoEstadisticas", OfficeOpenXml.Drawing.Chart.eChartType.ColumnClustered);
                    chart.SetPosition(9, 0, 3, 0);
                    chart.SetSize(650, 320);

                    var serie1 = chart.Series.Add(hojaEstadisticas.Cells["B4"], hojaEstadisticas.Cells["A4"]);
                    serie1.Header = "Total de Registros";
                    var serie2 = chart.Series.Add(hojaEstadisticas.Cells["B5"], hojaEstadisticas.Cells["A5"]);
                    serie2.Header = "Total de Tardanzas";
                    var serie3 = chart.Series.Add(hojaEstadisticas.Cells["B6"], hojaEstadisticas.Cells["A6"]);
                    serie3.Header = "Total de Ausencias";
                  

                    try
                    {
                        serie1.Fill.Color = Color.FromArgb(255, 192, 0);
                        serie2.Fill.Color = Color.FromArgb(192, 0, 0);
                        serie3.Fill.Color = Color.FromArgb(191, 191, 191);
                      
                    }
                    catch (Exception) { }

                    chart.Title.Text = "Estadísticas del Reporte";
                    chart.Title.Font.Size = 12;
                    chart.Title.Font.Bold = true;

                    if (chart.XAxis.Title != null)
                    {
                        chart.XAxis.Title.Text = "Categorías";
                        chart.XAxis.Title.Font.Size = 9;
                        chart.XAxis.Title.Font.Bold = true;
                    }

                    if (chart.YAxis.Title != null)
                    {
                        chart.YAxis.Title.Text = "Valores";
                        chart.YAxis.Title.Font.Size = 9;
                        chart.YAxis.Title.Font.Bold = true;
                    }

                    chart.Legend.Position = OfficeOpenXml.Drawing.Chart.eLegendPosition.Bottom;
                    chart.Legend.Font.Size = 8;

                    // Guardar archivo
                    var bytes = package.GetAsByteArray();
                    var result = new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                    result.FileDownloadName = $"ReporteAsistencia_{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.xlsx";
                    return result;
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al exportar: " + ex.Message;
                return RedirectToAction("Reportes");
            }
        }

        // Función para convertir horas decimales a formato legible
        private string ConvertirHorasATexto(decimal horasDecimal)
        {
            int horas = (int)horasDecimal;
            int minutos = (int)((horasDecimal - horas) * 60);

            if (minutos > 0)
            {
                return $"{horas} horas y {minutos} minutos";
            }
            else
            {
                return $"{horas} horas";
            }
        }

        // Método auxiliar para obtener la letra de columna (A, B, C, ..., AA, AB, etc.)
        private string GetColumnLetter(int columnNumber)
        {
            string columnLetter = "";
            while (columnNumber > 0)
            {
                columnNumber--;
                columnLetter = (char)('A' + columnNumber % 26) + columnLetter;
                columnNumber /= 26;
            }
            return columnLetter;
        }


        //[HttpGet]
        //public ActionResult ExportarExcelProfesional(DateTime fechaInicio, DateTime fechaFin, int? usuarioId, string rol)
        //{
        //    try
        //    {
        //        SqlParameter[] parameters = new SqlParameter[]
        //        {
        //    new SqlParameter("@FechaInicio", fechaInicio),
        //    new SqlParameter("@FechaFin", fechaFin),
        //    new SqlParameter("@UsuarioId", usuarioId.HasValue ? (object)usuarioId.Value : DBNull.Value),
        //    new SqlParameter("@Rol", string.IsNullOrEmpty(rol) ? (object)DBNull.Value : rol)
        //        };

        //        DataTable reporteHoras = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerReporteHorasPorUsuario", parameters);
        //        DataTable resumenHoras = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerResumenHorasPorUsuario", parameters);

        //        using (var package = new ExcelPackage())
        //        {
        //            // ============================================
        //            // HOJA 1: REPORTE DE ASISTENCIA
        //            // ============================================
        //            var hojaReporte = package.Workbook.Worksheets.Add("Reporte Asistencia");

        //            // Configurar anchos de columna
        //            hojaReporte.Column(1).Width = 6;   // N°
        //            hojaReporte.Column(2).Width = 25;  // Empleado
        //            hojaReporte.Column(3).Width = 30;  // Email
        //            hojaReporte.Column(4).Width = 15;  // Rol
        //            hojaReporte.Column(5).Width = 12;  // Fecha
        //            hojaReporte.Column(6).Width = 10;  // Entrada
        //            hojaReporte.Column(7).Width = 14;  // Salida Almuerzo
        //            hojaReporte.Column(8).Width = 14;  // Retorno Almuerzo
        //            hojaReporte.Column(9).Width = 10;  // Salida
        //            hojaReporte.Column(10).Width = 8;  // Horas
        //            hojaReporte.Column(11).Width = 12; // Estado
        //            hojaReporte.Column(12).Width = 40; // Observaciones

        //            // TÍTULO PRINCIPAL
        //            hojaReporte.Cells["A1:L1"].Merge = true;
        //            hojaReporte.Cells["A1"].Value = "REPORTE DE ASISTENCIA";
        //            hojaReporte.Cells["A1"].Style.Font.Size = 18;
        //            hojaReporte.Cells["A1"].Style.Font.Bold = true;
        //            hojaReporte.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        //            // SUBTÍTULO
        //            hojaReporte.Cells["A2:L2"].Merge = true;
        //            hojaReporte.Cells["A2"].Value = $"Período: {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy} - Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";
        //            hojaReporte.Cells["A2"].Style.Font.Size = 10;
        //            hojaReporte.Cells["A2"].Style.Font.Italic = true;
        //            hojaReporte.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        //            // ENCABEZADOS
        //            int headerRow = 4;
        //            string[] headers = { "N°", "Empleado", "Email", "Rol", "Fecha", "Entrada", "Salida Almuerzo", "Retorno Almuerzo", "Salida", "Horas", "Estado", "Observaciones" };
        //            for (int i = 0; i < headers.Length; i++)
        //            {
        //                hojaReporte.Cells[headerRow, i + 1].Value = headers[i];
        //                hojaReporte.Cells[headerRow, i + 1].Style.Font.Bold = true;
        //                hojaReporte.Cells[headerRow, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
        //                hojaReporte.Cells[headerRow, i + 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(68, 114, 196));
        //                hojaReporte.Cells[headerRow, i + 1].Style.Font.Color.SetColor(Color.White);
        //                hojaReporte.Cells[headerRow, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
        //            }

        //            // DATOS
        //            int row = headerRow + 1;
        //            int numero = 1;

        //            foreach (DataRow dr in reporteHoras.Rows)
        //            {
        //                hojaReporte.Cells[row, 1].Value = numero++;
        //                hojaReporte.Cells[row, 2].Value = dr["NombreCompleto"].ToString();
        //                hojaReporte.Cells[row, 3].Value = dr["Email"].ToString();
        //                hojaReporte.Cells[row, 4].Value = dr["Rol"].ToString();
        //                hojaReporte.Cells[row, 5].Value = dr["Fecha"]?.ToString();
        //                hojaReporte.Cells[row, 6].Value = dr["HoraEntrada"]?.ToString();
        //                hojaReporte.Cells[row, 7].Value = dr["HoraSalidaAlmuerzo"]?.ToString();
        //                hojaReporte.Cells[row, 8].Value = dr["HoraRetornoAlmuerzo"]?.ToString();
        //                hojaReporte.Cells[row, 9].Value = dr["HoraSalida"]?.ToString();
        //                hojaReporte.Cells[row, 10].Value = dr["HorasTrabajadasDia"] != DBNull.Value ? Convert.ToDecimal(dr["HorasTrabajadasDia"]) : 0;
        //                hojaReporte.Cells[row, 11].Value = dr["EstadoDia"].ToString();
        //                hojaReporte.Cells[row, 12].Value = dr["Comentarios"]?.ToString();

        //                // Formato de números
        //                hojaReporte.Cells[row, 10].Style.Numberformat.Format = "0.00";

        //                // Formato condicional para tardanzas
        //                if (dr["EstadoDia"].ToString() == "Tardanza")
        //                {
        //                    hojaReporte.Cells[row, 11].Style.Font.Color.SetColor(Color.Orange);
        //                    hojaReporte.Cells[row, 11].Style.Font.Bold = true;
        //                }

        //                row++;
        //            }

        //            // Aplicar bordes a los datos
        //            hojaReporte.Cells[headerRow, 1, row - 1, 12].Style.Border.Top.Style = ExcelBorderStyle.Thin;
        //            hojaReporte.Cells[headerRow, 1, row - 1, 12].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        //            hojaReporte.Cells[headerRow, 1, row - 1, 12].Style.Border.Left.Style = ExcelBorderStyle.Thin;
        //            hojaReporte.Cells[headerRow, 1, row - 1, 12].Style.Border.Right.Style = ExcelBorderStyle.Thin;

        //            // ============================================
        //            // HOJA 2: RESUMEN POR EMPLEADO
        //            // ============================================
        //            var hojaResumen = package.Workbook.Worksheets.Add("Resumen por Empleado");

        //            hojaResumen.Column(1).Width = 6;   // N°
        //            hojaResumen.Column(2).Width = 25;  // Empleado
        //            hojaResumen.Column(3).Width = 30;  // Email
        //            hojaResumen.Column(4).Width = 15;  // Rol
        //            hojaResumen.Column(5).Width = 15;  // Días Trabajados
        //            hojaResumen.Column(6).Width = 15;  // Horas Trabajadas
        //            hojaResumen.Column(7).Width = 15;  // Horas Esperadas
        //            hojaResumen.Column(8).Width = 15;  // Diferencia

        //            // Título
        //            hojaResumen.Cells["A1:H1"].Merge = true;
        //            hojaResumen.Cells["A1"].Value = "RESUMEN DE HORAS POR EMPLEADO";
        //            hojaResumen.Cells["A1"].Style.Font.Size = 18;
        //            hojaResumen.Cells["A1"].Style.Font.Bold = true;
        //            hojaResumen.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        //            // Subtítulo
        //            hojaResumen.Cells["A2:H2"].Merge = true;
        //            hojaResumen.Cells["A2"].Value = $"Período: {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}";
        //            hojaResumen.Cells["A2"].Style.Font.Size = 10;
        //            hojaResumen.Cells["A2"].Style.Font.Italic = true;
        //            hojaResumen.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        //            // Encabezados resumen
        //            string[] headersResumen = { "N°", "Empleado", "Email", "Rol", "Días Trabajados", "Horas Trabajadas", "Horas Esperadas", "Diferencia" };
        //            for (int i = 0; i < headersResumen.Length; i++)
        //            {
        //                hojaResumen.Cells[4, i + 1].Value = headersResumen[i];
        //                hojaResumen.Cells[4, i + 1].Style.Font.Bold = true;
        //                hojaResumen.Cells[4, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
        //                hojaResumen.Cells[4, i + 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(68, 114, 196));
        //                hojaResumen.Cells[4, i + 1].Style.Font.Color.SetColor(Color.White);
        //            }

        //            // Datos resumen
        //            int rowResumen = 5;
        //            int numResumen = 1;

        //            foreach (DataRow dr in resumenHoras.Rows)
        //            {
        //                decimal horasTrabajadas = dr["TotalHorasTrabajadas"] != DBNull.Value ? Convert.ToDecimal(dr["TotalHorasTrabajadas"]) : 0;
        //                decimal horasEsperadas = dr["HorasEsperadas"] != DBNull.Value ? Convert.ToDecimal(dr["HorasEsperadas"]) : 0;
        //                decimal diferencia = horasTrabajadas - horasEsperadas;

        //                hojaResumen.Cells[rowResumen, 1].Value = numResumen++;
        //                hojaResumen.Cells[rowResumen, 2].Value = dr["NombreCompleto"].ToString();
        //                hojaResumen.Cells[rowResumen, 3].Value = dr["Email"].ToString();
        //                hojaResumen.Cells[rowResumen, 4].Value = dr["Rol"].ToString();
        //                hojaResumen.Cells[rowResumen, 5].Value = dr["DiasTrabajados"] != DBNull.Value ? Convert.ToInt32(dr["DiasTrabajados"]) : 0;
        //                hojaResumen.Cells[rowResumen, 6].Value = horasTrabajadas;
        //                hojaResumen.Cells[rowResumen, 7].Value = horasEsperadas;
        //                hojaResumen.Cells[rowResumen, 8].Value = diferencia;

        //                hojaResumen.Cells[rowResumen, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        //                hojaResumen.Cells[rowResumen, 6].Style.Numberformat.Format = "0.00";
        //                hojaResumen.Cells[rowResumen, 7].Style.Numberformat.Format = "0.00";
        //                hojaResumen.Cells[rowResumen, 8].Style.Numberformat.Format = "0.00";

        //                // Color para diferencia negativa
        //                if (diferencia < 0)
        //                {
        //                    hojaResumen.Cells[rowResumen, 8].Style.Font.Color.SetColor(Color.Red);
        //                    hojaResumen.Cells[rowResumen, 8].Style.Font.Bold = true;
        //                }

        //                rowResumen++;
        //            }

        //            // ============================================
        //            // HOJA 3: ESTADÍSTICAS
        //            // ============================================
        //            var hojaEstadisticas = package.Workbook.Worksheets.Add("Estadísticas");

        //            hojaEstadisticas.Column(1).Width = 25;
        //            hojaEstadisticas.Column(2).Width = 20;

        //            // Calcular estadísticas
        //            int totalRegistros = reporteHoras.Rows.Count;
        //            int totalTardanzas = 0;
        //            int totalAusentes = 0;
        //            decimal totalHoras = 0;

        //            foreach (DataRow dr in reporteHoras.Rows)
        //            {
        //                if (dr["EstadoDia"].ToString() == "Tardanza") totalTardanzas++;
        //                if (dr["EstadoDia"].ToString() == "Ausente") totalAusentes++;
        //                if (dr["HorasTrabajadasDia"] != DBNull.Value)
        //                    totalHoras += Convert.ToDecimal(dr["HorasTrabajadasDia"]);
        //            }

        //            // Título
        //            hojaEstadisticas.Cells["A1:B1"].Merge = true;
        //            hojaEstadisticas.Cells["A1"].Value = "ESTADÍSTICAS DEL REPORTE";
        //            hojaEstadisticas.Cells["A1"].Style.Font.Size = 16;
        //            hojaEstadisticas.Cells["A1"].Style.Font.Bold = true;
        //            hojaEstadisticas.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        //            // Datos estadísticos
        //            hojaEstadisticas.Cells[3, 1].Value = "Total de Registros";
        //            hojaEstadisticas.Cells[3, 2].Value = totalRegistros;
        //            hojaEstadisticas.Cells[4, 1].Value = "Total de Tardanzas";
        //            hojaEstadisticas.Cells[4, 2].Value = totalTardanzas;
        //            hojaEstadisticas.Cells[5, 1].Value = "Total de Ausencias";
        //            hojaEstadisticas.Cells[5, 2].Value = totalAusentes;
        //            hojaEstadisticas.Cells[6, 1].Value = "Total Horas Trabajadas";
        //            hojaEstadisticas.Cells[6, 2].Value = totalHoras;

        //            // Formato de números
        //            hojaEstadisticas.Cells[6, 2].Style.Numberformat.Format = "0.00";

        //            // Negritas para etiquetas
        //            hojaEstadisticas.Cells[3, 1, 6, 1].Style.Font.Bold = true;

        //            // Guardar archivo
        //            var bytes = package.GetAsByteArray();
        //            var result = new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        //            result.FileDownloadName = $"ReporteAsistencia_{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.xlsx";

        //            return result;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        TempData["Error"] = "Error al exportar: " + ex.Message;
        //        return RedirectToAction("Reportes");
        //    }
        //}

        [HttpGet]
        public ActionResult ExportarPagosExcelProfesional(DateTime fechaInicio, DateTime fechaFin, int? usuarioId, string rolPago)
        {
            try
            {
                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@FechaInicio", fechaInicio),
            new SqlParameter("@FechaFin", fechaFin),
            new SqlParameter("@UsuarioId", usuarioId.HasValue ? (object)usuarioId.Value : DBNull.Value),
            new SqlParameter("@RolPago", string.IsNullOrEmpty(rolPago) ? (object)DBNull.Value : rolPago)
                };

                DataTable resumenGeneral = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerReportePagoHoras", parameters);

                using (var package = new ExcelPackage())
                {
                    // Logos
                    string logoIzquierdoPath = Server.MapPath("~/Content/images/logo-empresa.png");
                    string logoDerechoPath = Server.MapPath("~/Content/images/Logo_Edificio.png");

                    // Colores
                    Color colorAzulTitulo = Color.FromArgb(54, 86, 139);
                    Color colorVerdeTotal = Color.FromArgb(0, 176, 80);

                    // ============================================
                    // CALCULAR DÍAS LABORABLES EN EL RANGO
                    // ============================================
                    int totalDias = (fechaFin - fechaInicio).Days + 1;
                    int diasLaborables = 0;
                    int domingos = 0;

                    for (DateTime d = fechaInicio; d <= fechaFin; d = d.AddDays(1))
                    {
                        if (d.DayOfWeek == DayOfWeek.Sunday)
                        {
                            domingos++;
                        }
                        else
                        {
                            diasLaborables++;
                        }
                    }

                    // ============================================
                    // CALCULAR PAGOS POR CADA EMPLEADO
                    // ============================================
                    var pagosPorUsuario = new Dictionary<int, dynamic>();

                    foreach (DataRow dr in resumenGeneral.Rows)
                    {
                        int usuarioIdDetalle = Convert.ToInt32(dr["UsuarioId"]);
                        if (!pagosPorUsuario.ContainsKey(usuarioIdDetalle))
                        {
                            SqlParameter[] paramPago = new SqlParameter[]
                            {
                        new SqlParameter("@UsuarioId", usuarioIdDetalle),
                        new SqlParameter("@FechaInicio", fechaInicio),
                        new SqlParameter("@FechaFin", fechaFin)
                            };
                            DataTable dtPago = DatabaseHelper.ExecuteStoredProcedure("sp_CalcularPagosPorEmpleado", paramPago);

                            if (dtPago.Rows.Count > 0)
                            {
                                DataRow pagoRow = dtPago.Rows[0];
                                pagosPorUsuario[usuarioIdDetalle] = new
                                {
                                    Tipo = pagoRow["Tipo"].ToString(),
                                    TarifaHora = Convert.ToDecimal(pagoRow["TarifaHora"]),
                                    TotalHoras = Convert.ToDecimal(pagoRow["TotalHoras"]),
                                    DiasTrabajados = Convert.ToInt32(pagoRow["DiasTrabajados"]),
                                    MontoTotal = Convert.ToDecimal(pagoRow["MontoTotal"]),
                                    DetalleCalculo = pagoRow["DetalleCalculo"].ToString()
                                };
                            }
                        }
                    }

                    // ============================================
                    // HOJA DE INICIO (PORTADA)
                    // ============================================
                    var portada = package.Workbook.Worksheets.Add("INICIO");

                    Color colorTitulo = Color.FromArgb(0, 176, 240);
                    Color colorPeriodo = Color.FromArgb(218, 233, 248);
                    Color colorFecha = Color.FromArgb(217, 217, 217);
                    Color colorRojoOscuro = Color.FromArgb(192, 0, 0);
                    Color colorFondoVerde = Color.FromArgb(146, 208, 80);
                    Color colorFondoInfo2 = Color.FromArgb(218, 233, 248);  // ← Agregar esta línea
                    // Título
                    portada.Row(1).Height = 40;
                    portada.Cells["A1:G1"].Merge = true;
                    portada.Cells["A1"].Value = "REPORTE DE PAGO POR HORAS";
                    portada.Cells["A1"].Style.Font.Size = 16;
                    portada.Cells["A1"].Style.Font.Bold = true;
                    portada.Cells["A1"].Style.Font.Color.SetColor(Color.Black);
                    portada.Cells["A1"].Style.Font.Name = "Arial";
                    portada.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    portada.Cells["A1"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    portada.Cells["A1:G1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    portada.Cells["A1:G1"].Style.Fill.BackgroundColor.SetColor(colorTitulo);

                    // Logos
                    if (System.IO.File.Exists(logoIzquierdoPath))
                    {
                        var logoImage = new FileInfo(logoIzquierdoPath);
                        var picture = portada.Drawings.AddPicture("LogoIzquierdo", logoImage);
                        picture.SetPosition(0, 7, 0, 50);
                        picture.SetSize(100, 45);
                    }

                    if (System.IO.File.Exists(logoDerechoPath))
                    {
                        var logoImageDerecho = new FileInfo(logoDerechoPath);
                        var pictureDerecho = portada.Drawings.AddPicture("LogoDerecho", logoImageDerecho);
                        pictureDerecho.SetPosition(0, 5, 6, 2);
                        pictureDerecho.SetSize(80, 32);
                    }

                    // Período
                    portada.Row(2).Height = 18;
                    portada.Cells["A2:G2"].Merge = true;
                    portada.Cells["A2"].Value = $"Período: {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}";
                    portada.Cells["A2"].Style.Font.Name = "Arial";
                    portada.Cells["A2"].Style.Font.Size = 12;
                    portada.Cells["A2"].Style.Font.Bold = true;
                    portada.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    portada.Cells["A2"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    portada.Cells["A2:G2"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    portada.Cells["A2:G2"].Style.Fill.BackgroundColor.SetColor(colorPeriodo);

                    // Fecha de generación
                    portada.Row(3).Height = 18;
                    portada.Cells["A3:G3"].Merge = true;
                    portada.Cells["A3"].Value = $"Fecha de generación: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                    portada.Cells["A3"].Style.Font.Name = "Arial";
                    portada.Cells["A3"].Style.Font.Size = 10;
                    portada.Cells["A3"].Style.Font.Italic = true;
                    portada.Cells["A3"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    portada.Cells["A3"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    portada.Cells["A3:G3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    portada.Cells["A3:G3"].Style.Fill.BackgroundColor.SetColor(colorFecha);

                    // Información del período
                    int rowInfo = 5;
                    portada.Cells[rowInfo, 2].Value = "INFORMACIÓN DEL PERÍODO:";
                    portada.Cells[rowInfo, 2].Style.Font.Bold = true;
                    portada.Cells[rowInfo, 2].Style.Font.Size = 11;
                    rowInfo += 1;

                    // Tabla de información
                    portada.Cells[rowInfo, 2].Value = "Total de días en el período:";
                    portada.Cells[rowInfo, 2].Style.Font.Bold = true; 
                    portada.Cells[rowInfo, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    portada.Cells[rowInfo, 2].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(218, 233, 248));  // Color directo
                    portada.Cells[rowInfo, 2].Style.Border.BorderAround(ExcelBorderStyle.Thin);


                    portada.Cells[rowInfo, 3].Value = totalDias;
                    portada.Cells[rowInfo, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    portada.Cells[rowInfo, 5].Value = "Días laborables (Lun a Sáb):";
                    portada.Cells[rowInfo, 5].Style.Font.Bold = true;
                    portada.Cells[rowInfo, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    portada.Cells[rowInfo, 5].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(218, 233, 248));  // Color directo
                    portada.Cells[rowInfo, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin);



                    portada.Cells[rowInfo, 6].Value = diasLaborables;
                    portada.Cells[rowInfo, 6].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    rowInfo += 1;

                    portada.Cells[rowInfo, 2].Value = "Domingos:";
                    portada.Cells[rowInfo, 2].Style.Font.Bold = true; 
                    portada.Cells[rowInfo, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    portada.Cells[rowInfo, 2].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(218, 233, 248));  // Color directo
                    portada.Cells[rowInfo, 2].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    portada.Cells[rowInfo, 3].Value = domingos;
                    portada.Cells[rowInfo, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    portada.Cells[rowInfo, 5].Value = "Horas esperadas (8h x día):"; 
                    portada.Cells[rowInfo, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    portada.Cells[rowInfo, 5].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(218, 233, 248));  // Color directo
                    portada.Cells[rowInfo, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin); 
                    portada.Cells[rowInfo, 5].Style.Font.Bold = true;

                    portada.Cells[rowInfo, 6].Value = diasLaborables * 8;
                    portada.Cells[rowInfo, 6].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    rowInfo += 2;

                    // Filtros aplicados
                    portada.Cells[rowInfo, 2].Value = "FILTROS APLICADOS:";
                    portada.Cells[rowInfo, 2].Style.Font.Bold = true;
                    portada.Cells[rowInfo, 2].Style.Font.Size = 11;
                    rowInfo += 1;

                    portada.Cells[rowInfo, 2].Value = "Fecha Inicio:";
                    portada.Cells[rowInfo, 2].Style.Font.Bold = true; 
                    portada.Cells[rowInfo, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    portada.Cells[rowInfo, 2].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(218, 233, 248));  // Color directo
                    portada.Cells[rowInfo, 2].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    portada.Cells[rowInfo, 3].Value = fechaInicio.ToString("dd/MM/yyyy");
                    portada.Cells[rowInfo, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    portada.Cells[rowInfo, 5].Value = "Fecha Fin:";
                    portada.Cells[rowInfo, 5].Style.Font.Bold = true;
                    portada.Cells[rowInfo, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    portada.Cells[rowInfo, 5].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(218, 233, 248));  // Color directo
                    portada.Cells[rowInfo, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin);


                    portada.Cells[rowInfo, 6].Value = fechaFin.ToString("dd/MM/yyyy");
                    portada.Cells[rowInfo, 6].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    rowInfo += 1;

                    if (!string.IsNullOrEmpty(rolPago))
                    {
                        portada.Cells[rowInfo, 2].Value = "Rol de Pago:";
                        portada.Cells[rowInfo, 2].Style.Font.Bold = true;
                        portada.Cells[rowInfo, 3].Value = rolPago;
                        rowInfo += 1;
                    }

                    if (usuarioId.HasValue && usuarioId.Value > 0)
                    {
                        portada.Cells[rowInfo, 2].Value = "Usuario filtrado:";
                        portada.Cells[rowInfo, 2].Style.Font.Bold = true;
                        portada.Cells[rowInfo, 3].Value = "Empleado específico";
                        rowInfo += 1;
                    }

                    // Tabla resumen general
                    // TABLA DE RESUMEN GENERAL
                    rowInfo += 1;
                    portada.Cells[rowInfo, 1, rowInfo, 7].Merge = true;  // Combina de columna A a G
                    portada.Cells[rowInfo, 1].Value = "RESUMEN GENERAL DEL PERÍODO";
                    portada.Cells[rowInfo, 1].Style.Font.Bold = true;
                    portada.Cells[rowInfo, 1].Style.Font.Size = 12;
                    portada.Cells[rowInfo, 1].Style.Font.Name = "Arial";
                    portada.Cells[rowInfo, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;  // Centrado
                    portada.Cells[rowInfo, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    rowInfo += 1;

                    string[] headersResumen = { "N°", "Empleado", "Rol de Pago", "Tipo Pago", "Días Trabajados", "Horas Totales", "Monto Total" };
                    for (int i = 0; i < headersResumen.Length; i++)
                    {
                        portada.Cells[rowInfo, i + 1].Value = headersResumen[i];
                        portada.Cells[rowInfo, i + 1].Style.Font.Bold = true;
                        portada.Cells[rowInfo, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        portada.Cells[rowInfo, i + 1].Style.Fill.BackgroundColor.SetColor(colorRojoOscuro);
                        portada.Cells[rowInfo, i + 1].Style.Font.Color.SetColor(Color.White);
                        portada.Cells[rowInfo, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    }
                    rowInfo++;

                    int numero = 1;
                    decimal totalHorasGeneral = 0;
                    decimal totalMontoGeneral = 0;

                    foreach (DataRow dr in resumenGeneral.Rows)
                    {
                        int usuarioIdActual = Convert.ToInt32(dr["UsuarioId"]);
                        string tipoPago = "";
                        decimal totalHoras = 0;
                        int diasTrabajados = 0;
                        decimal montoTotal = 0;

                        if (pagosPorUsuario.ContainsKey(usuarioIdActual))
                        {
                            var pago = pagosPorUsuario[usuarioIdActual];
                            tipoPago = pago.Tipo;
                            totalHoras = pago.TotalHoras;
                            diasTrabajados = pago.DiasTrabajados;
                            montoTotal = pago.MontoTotal;
                        }

                        portada.Cells[rowInfo, 1].Value = numero++;
                        portada.Cells[rowInfo, 2].Value = dr["NombreCompleto"].ToString();
                        portada.Cells[rowInfo, 3].Value = dr["RolPago"].ToString();
                        portada.Cells[rowInfo, 4].Value = tipoPago == "Facilitador" ? "Por Horas" : "Por Días";
                        portada.Cells[rowInfo, 5].Value = diasTrabajados;
                        portada.Cells[rowInfo, 6].Value = totalHoras;
                        portada.Cells[rowInfo, 7].Value = montoTotal;

                        portada.Cells[rowInfo, 6].Style.Numberformat.Format = "0.00";
                        portada.Cells[rowInfo, 7].Style.Numberformat.Format = "0.00";

                        for (int i = 1; i <= 7; i++)
                        {
                            portada.Cells[rowInfo, i].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        }

                        totalHorasGeneral += totalHoras;
                        totalMontoGeneral += montoTotal;
                        rowInfo++;
                    }

                    // Totales
                    portada.Cells[rowInfo, 5].Value = "TOTAL GENERAL:";
                    portada.Cells[rowInfo, 5].Style.Font.Bold = true;
                    portada.Cells[rowInfo, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    portada.Cells[rowInfo, 5].Style.Fill.BackgroundColor.SetColor(colorFondoVerde);
                    portada.Cells[rowInfo, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin); 

                    portada.Cells[rowInfo, 6].Value = totalHorasGeneral;
                    portada.Cells[rowInfo, 6].Style.Numberformat.Format = "0.00";
                    portada.Cells[rowInfo, 6].Style.Font.Bold = true;
                    portada.Cells[rowInfo, 6].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    portada.Cells[rowInfo, 7].Value = totalMontoGeneral;
                    portada.Cells[rowInfo, 7].Style.Numberformat.Format = "0.00";
                    portada.Cells[rowInfo, 7].Style.Font.Bold = true;
                    portada.Cells[rowInfo, 7].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    portada.Cells[rowInfo, 7].Style.Font.Color.SetColor(Color.Green);

                    // Ajustar columnas
                    portada.Cells[1, 1, rowInfo, 7].AutoFitColumns();

                    // ============================================
                    // HOJAS INDIVIDUALES
                    // ============================================
                    foreach (DataRow dr in resumenGeneral.Rows)
                    {
                        int usuarioIdDetalle = Convert.ToInt32(dr["UsuarioId"]);
                        string nombreEmpleado = dr["NombreCompleto"].ToString();
                        string rolPagoEmpleado = dr["RolPago"].ToString();

                        string tipoPagoEmpleado = "Facilitador";
                        decimal tarifaHora = 0;
                        decimal totalHorasEmpleado = 0;
                        int diasTrabajadosEmpleado = 0;
                        decimal montoTotalEmpleado = 0;
                        string detalleCalculo = "";

                        if (pagosPorUsuario.ContainsKey(usuarioIdDetalle))
                        {
                            var pago = pagosPorUsuario[usuarioIdDetalle];
                            tipoPagoEmpleado = pago.Tipo;
                            tarifaHora = pago.TarifaHora;
                            totalHorasEmpleado = pago.TotalHoras;
                            diasTrabajadosEmpleado = pago.DiasTrabajados;
                            montoTotalEmpleado = pago.MontoTotal;
                            detalleCalculo = pago.DetalleCalculo;
                        }

                        string nombreHoja = nombreEmpleado.Length > 31 ? nombreEmpleado.Substring(0, 28) + ".." : nombreEmpleado;
                        nombreHoja = nombreHoja.Replace("/", "").Replace("\\", "").Replace("?", "").Replace("*", "").Replace("[", "").Replace("]", "").Replace(":", "");

                        SqlParameter[] detalleParams = new SqlParameter[]
                        {
                    new SqlParameter("@FechaInicio", fechaInicio),
                    new SqlParameter("@FechaFin", fechaFin),
                    new SqlParameter("@UsuarioId", usuarioIdDetalle)
                        };

                        DataTable detalle = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerDetallePagoPorUsuario", detalleParams);

                        var hoja = package.Workbook.Worksheets.Add(nombreHoja);

                        // Encabezado
                        hoja.Row(1).Height = 40;
                        hoja.Cells["A1:J1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hoja.Cells["A1:J1"].Style.Fill.BackgroundColor.SetColor(colorTitulo);
                        hoja.Cells["B1:I1"].Merge = true;
                        hoja.Cells["B1"].Value = "REPORTE DE ASISTENCIA Y PAGO";
                        hoja.Cells["B1"].Style.Font.Size = 14;
                        hoja.Cells["B1"].Style.Font.Bold = true;
                        hoja.Cells["B1"].Style.Font.Color.SetColor(Color.Black);
                        hoja.Cells["B1"].Style.Font.Name = "Arial";
                        hoja.Cells["B1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        hoja.Cells["B1"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                        // Logos
                        if (System.IO.File.Exists(logoIzquierdoPath))
                        {
                            var logoImage = new FileInfo(logoIzquierdoPath);
                            var picture = hoja.Drawings.AddPicture("LogoIzquierdo", logoImage);
                            picture.SetPosition(0, 7, 0, 30);
                            picture.SetSize(115, 45);
                        }

                        if (System.IO.File.Exists(logoDerechoPath))
                        {
                            var logoImageDerecho = new FileInfo(logoDerechoPath);
                            var pictureDerecho = hoja.Drawings.AddPicture("LogoDerecho", logoImageDerecho);
                            pictureDerecho.SetPosition(0, 5, 8, 0);
                            pictureDerecho.SetSize(80, 32);
                        }

                        Color colorFondoInfo = Color.FromArgb(218, 233, 248);

                        // Información del empleado
                        hoja.Cells["B3"].Value = "Empleado:";
                        hoja.Cells["B3"].Style.Font.Bold = true;
                        hoja.Cells["B3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hoja.Cells["B3"].Style.Fill.BackgroundColor.SetColor(colorFondoInfo);
                        hoja.Cells["B3"].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        hoja.Cells["C3"].Value = nombreEmpleado;
                        hoja.Cells["C3"].Style.Font.Bold = true;
                        hoja.Cells["C3"].Style.Font.Color.SetColor(colorAzulTitulo);
                        hoja.Cells["C3"].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        hoja.Cells["B4"].Value = "Rol de Pago:";
                        hoja.Cells["B4"].Style.Font.Bold = true;
                        hoja.Cells["B4"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hoja.Cells["B4"].Style.Fill.BackgroundColor.SetColor(colorFondoInfo);
                        hoja.Cells["B4"].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        hoja.Cells["C4"].Value = rolPagoEmpleado;
                        hoja.Cells["C4"].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        hoja.Cells["B5"].Value = "Tipo de Pago:";
                        hoja.Cells["B5"].Style.Font.Bold = true;
                        hoja.Cells["B5"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hoja.Cells["B5"].Style.Fill.BackgroundColor.SetColor(colorFondoInfo);
                        hoja.Cells["B5"].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        hoja.Cells["C5"].Value = tipoPagoEmpleado == "Facilitador" ? "Pago por Horas" : "Pago por Días";
                        hoja.Cells["C5"].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        hoja.Cells["E3"].Value = "Período:";
                        hoja.Cells["E3"].Style.Font.Bold = true;
                        hoja.Cells["E3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hoja.Cells["E3"].Style.Fill.BackgroundColor.SetColor(colorFondoInfo);
                        hoja.Cells["E3"].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        hoja.Cells["F3"].Value = $"{fechaInicio:dd/MM/yyyy} - {fechaFin:dd/MM/yyyy}";
                        hoja.Cells["F3"].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        hoja.Cells["E4"].Value = "Días Laborables:";
                        hoja.Cells["E4"].Style.Font.Bold = true;
                        hoja.Cells["E4"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hoja.Cells["E4"].Style.Fill.BackgroundColor.SetColor(colorFondoInfo);
                        hoja.Cells["E4"].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        hoja.Cells["F4"].Value = diasLaborables;
                        hoja.Cells["F4"].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        hoja.Cells["E5"].Value = "Valor por Día:";
                        hoja.Cells["F5"].Value = tarifaHora;
                        hoja.Cells["F5"].Style.Numberformat.Format = "0.00"; 
                        hoja.Cells["E5"].Style.Font.Bold = true;
                        hoja.Cells["E5"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hoja.Cells["E5"].Style.Fill.BackgroundColor.SetColor(colorFondoInfo);
                        hoja.Cells["E5"].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        hoja.Cells["F5"].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        // Tabla de registros
                        int filaTabla = 7;
                        string[] headersTabla = { "N°", "Fecha", "Día", "Entrada", "Salida", "Salida Alm.", "Retorno Alm.", "Horas", "Monto Día" };

                        Color[] headerColors = new Color[]
                        {
                    Color.FromArgb(192, 0, 0), Color.FromArgb(192, 0, 0), Color.FromArgb(192, 0, 0),
                    Color.FromArgb(60, 125, 34), Color.FromArgb(255, 153, 51),
                    Color.FromArgb(0, 112, 192), Color.FromArgb(0, 112, 192),
                    Color.FromArgb(192, 0, 0), Color.FromArgb(192, 0, 0)
                        };

                        for (int i = 0; i < headersTabla.Length; i++)
                        {
                            hoja.Cells[filaTabla, i + 1].Value = headersTabla[i];
                            hoja.Cells[filaTabla, i + 1].Style.Font.Bold = true;
                            hoja.Cells[filaTabla, i + 1].Style.Font.Color.SetColor(Color.White);
                            hoja.Cells[filaTabla, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            hoja.Cells[filaTabla, i + 1].Style.Fill.BackgroundColor.SetColor(headerColors[i]);
                            hoja.Cells[filaTabla, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        }
                        filaTabla++;

                        int diaNumero = 1;
                        decimal totalHorasHoja = 0;
                        decimal totalMontoHoja = 0;
                        int diasConRegistros = 0;

                        var registrosPorFecha = new Dictionary<DateTime, DataRow>();
                        foreach (DataRow det in detalle.Rows)
                        {
                            DateTime fecha = Convert.ToDateTime(det["Fecha"]);
                            registrosPorFecha[fecha] = det;
                        }

                        for (DateTime fecha = fechaInicio; fecha <= fechaFin; fecha = fecha.AddDays(1))
                        {
                            if (fecha.DayOfWeek == DayOfWeek.Sunday) { diaNumero++; continue; }

                            string diaSemana = ObtenerDiaSemana(fecha);

                            hoja.Cells[filaTabla, 1].Value = diaNumero;
                            hoja.Cells[filaTabla, 2].Value = fecha.ToString("dd/MM/yyyy");
                            hoja.Cells[filaTabla, 3].Value = diaSemana;

                            if (registrosPorFecha.ContainsKey(fecha))
                            {
                                DataRow registro = registrosPorFecha[fecha];
                                decimal horas = Convert.ToDecimal(registro["HorasTrabajadas"]);
                                decimal montoDia = Convert.ToDecimal(registro["MontoDia"]);

                                hoja.Cells[filaTabla, 4].Value = registro["HoraEntrada"]?.ToString() ?? "-";
                                hoja.Cells[filaTabla, 5].Value = registro["HoraSalida"]?.ToString() ?? "-";
                                hoja.Cells[filaTabla, 6].Value = registro["SalidaAlmuerzo"]?.ToString() ?? "-";
                                hoja.Cells[filaTabla, 7].Value = registro["RetornoAlmuerzo"]?.ToString() ?? "-";
                                hoja.Cells[filaTabla, 8].Value = horas ;
                                hoja.Cells[filaTabla, 9].Value = montoDia;

                                hoja.Cells[filaTabla, 8].Style.Numberformat.Format = "0.00";
                                hoja.Cells[filaTabla, 9].Style.Numberformat.Format = "0.00";

                                totalHorasHoja += horas;
                                totalMontoHoja += montoDia;
                                diasConRegistros++;

                                string horaEntrada = registro["HoraEntrada"]?.ToString();
                                if (!string.IsNullOrEmpty(horaEntrada) && string.Compare(horaEntrada, "08:15") > 0)
                                {
                                    hoja.Cells[filaTabla, 4].Style.Font.Color.SetColor(Color.Orange);
                                    hoja.Cells[filaTabla, 4].Style.Font.Bold = true;
                                }
                            }
                            else
                            {
                                hoja.Cells[filaTabla, 4].Value = "-";
                                hoja.Cells[filaTabla, 5].Value = "-";
                                hoja.Cells[filaTabla, 6].Value = "-";
                                hoja.Cells[filaTabla, 7].Value = "-";
                                hoja.Cells[filaTabla, 8].Value = 0;
                                hoja.Cells[filaTabla, 9].Value = 0;
                                hoja.Cells[filaTabla, 8].Style.Numberformat.Format = "0.00";
                                hoja.Cells[filaTabla, 9].Style.Numberformat.Format = "0.00";

                                hoja.Cells[filaTabla, 1, filaTabla, 9].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                hoja.Cells[filaTabla, 1, filaTabla, 9].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                            }

                            for (int i = 1; i <= 9; i++)
                            {
                                hoja.Cells[filaTabla, i].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                            }

                            filaTabla++;
                            diaNumero++;
                        }

                        // Totales
                        //hoja.Cells[filaTabla + 1, 7].Value = "TOTALES:";
                        //hoja.Cells[filaTabla + 1, 7].Style.Font.Bold = true;
                        //hoja.Cells[filaTabla + 1, 7].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        //hoja.Cells[filaTabla + 1, 7].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(146, 208, 80));
                        //hoja.Cells[filaTabla + 1, 7].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        //hoja.Cells[filaTabla + 1, 7].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        //hoja.Cells[filaTabla + 1, 7].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                        //hoja.Cells[filaTabla + 1, 8].Value = totalHorasHoja;
                        //hoja.Cells[filaTabla + 1, 8].Style.Numberformat.Format = "0.00";
                        //hoja.Cells[filaTabla + 1, 8].Style.Font.Bold = true;
                        //hoja.Cells[filaTabla + 1, 8].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        //hoja.Cells[filaTabla + 1, 8].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        //hoja.Cells[filaTabla + 1, 8].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                        //hoja.Cells[filaTabla + 1, 9].Value = totalMontoHoja;
                        //hoja.Cells[filaTabla + 1, 9].Style.Numberformat.Format = "0.00";
                        //hoja.Cells[filaTabla + 1, 9].Style.Font.Bold = true;
                        //hoja.Cells[filaTabla + 1, 9].Style.Font.Color.SetColor(Color.Green);
                        //hoja.Cells[filaTabla + 1, 9].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        //hoja.Cells[filaTabla + 1, 9].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        //hoja.Cells[filaTabla + 1, 9].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                        // Resumen de pago
                        int filaResumen = filaTabla + 1;

                        hoja.Cells[filaResumen, 1, filaResumen, 5].Merge = true;
                        hoja.Cells[filaResumen, 1, filaResumen, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hoja.Cells[filaResumen, 1, filaResumen, 5].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(57, 57, 57));
                        hoja.Cells[filaResumen, 1].Value = "RESUMEN DE PAGO";
                        hoja.Cells[filaResumen, 1].Style.Font.Bold = true;
                        hoja.Cells[filaResumen, 1].Style.Font.Color.SetColor(Color.White);
                        hoja.Cells[filaResumen, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
 

                        int filaDatosResumen = filaResumen + 1;

                        // Horas Totales Trabajadas
                        hoja.Cells[filaDatosResumen, 1, filaDatosResumen, 4].Merge = true;
                        hoja.Cells[filaDatosResumen, 1].Value = "Horas Totales Trabajadas:";
                        hoja.Cells[filaDatosResumen, 1].Style.Font.Bold = true;
                        hoja.Cells[filaDatosResumen, 1, filaDatosResumen, 4].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        hoja.Cells[filaDatosResumen, 5].Value = totalHorasGeneral;
                        hoja.Cells[filaDatosResumen, 5].Style.Numberformat.Format = "0.00";
                        hoja.Cells[filaDatosResumen, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        // Días Trabajados
                        hoja.Cells[filaDatosResumen + 1, 1, filaDatosResumen + 1, 4].Merge = true;
                        hoja.Cells[filaDatosResumen + 1, 1].Value = "Días Trabajados:";
                        hoja.Cells[filaDatosResumen + 1, 1].Style.Font.Bold = true;
                        hoja.Cells[filaDatosResumen + 1, 1, filaDatosResumen + 1, 4].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        hoja.Cells[filaDatosResumen + 1, 5].Value = diasConRegistros;
                        hoja.Cells[filaDatosResumen + 1, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                       
                            hoja.Cells[filaDatosResumen + 2, 1, filaDatosResumen + 2, 4].Merge = true;
                            hoja.Cells[filaDatosResumen + 2, 1].Value = "Tarifa por Hora:";
                            hoja.Cells[filaDatosResumen + 2, 1].Style.Font.Bold = true;
                            hoja.Cells[filaDatosResumen + 2, 1, filaDatosResumen + 2, 4].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                            hoja.Cells[filaDatosResumen + 2, 5].Value = tarifaHora;
                            hoja.Cells[filaDatosResumen + 2, 5].Style.Numberformat.Format = "0.00";
                            hoja.Cells[filaDatosResumen + 2, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                      

                        // MONTO TOTAL A PAGAR
                        hoja.Cells[filaDatosResumen + 3, 1, filaDatosResumen + 3, 4].Merge = true;
                        hoja.Cells[filaDatosResumen + 3, 1, filaDatosResumen + 3, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        hoja.Cells[filaDatosResumen + 3, 1, filaDatosResumen + 3, 4].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(146, 208, 80));
                        hoja.Cells[filaDatosResumen + 3, 1, filaDatosResumen + 3, 4].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        hoja.Cells[filaDatosResumen + 3, 1].Value = "MONTO TOTAL A PAGAR:";
                        hoja.Cells[filaDatosResumen + 3, 1].Style.Font.Bold = true;
                        hoja.Cells[filaDatosResumen + 3, 1].Style.Font.Size = 12;

                        hoja.Cells[filaDatosResumen + 3, 5].Value = montoTotalEmpleado;
                        hoja.Cells[filaDatosResumen + 3, 5].Style.Numberformat.Format = "0.00";
                        hoja.Cells[filaDatosResumen + 3, 5].Style.Font.Bold = true;
                        hoja.Cells[filaDatosResumen + 3, 5].Style.Font.Color.SetColor(Color.Green);
                        hoja.Cells[filaDatosResumen + 3, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        // Detalle del cálculo
                        hoja.Cells[filaDatosResumen + 5, 1, filaDatosResumen + 5, 6].Merge = true;
                        hoja.Cells[filaDatosResumen + 5, 1].Value = detalleCalculo;
                        hoja.Cells[filaDatosResumen + 5, 1].Style.Font.Italic = true;
                        hoja.Cells[filaDatosResumen + 5, 1].Style.Font.Size = 9;
                        hoja.Cells[filaDatosResumen + 5, 1].Style.Font.Color.SetColor(Color.DarkBlue);
                       

                        hoja.Cells[1, 1, filaDatosResumen + 10, 10].AutoFitColumns();
                    }

                    var bytes = package.GetAsByteArray();
                    var result = new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                    result.FileDownloadName = $"ReportePagos_{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.xlsx";

                    return result;
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al exportar: " + ex.Message;
                return RedirectToAction("ReportePagos");
            }
        }

        //[HttpGet]
        //public ActionResult ExportarPagosExcelProfesionalBack(DateTime fechaInicio, DateTime fechaFin, int? usuarioId, string rolPago)
        //{
        //    try
        //    {
        //        SqlParameter[] parameters = new SqlParameter[]
        //        {
        //    new SqlParameter("@FechaInicio", fechaInicio),
        //    new SqlParameter("@FechaFin", fechaFin),
        //    new SqlParameter("@UsuarioId", usuarioId.HasValue ? (object)usuarioId.Value : DBNull.Value),
        //    new SqlParameter("@RolPago", string.IsNullOrEmpty(rolPago) ? (object)DBNull.Value : rolPago)
        //        };

        //        DataTable resumenGeneral = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerReportePagoHoras", parameters);

        //        using (var package = new ExcelPackage())
        //        {
        //            // ============================================
        //            // HOJA DE INICIO (PORTADA)
        //            // ============================================
        //            var portada = package.Workbook.Worksheets.Add("INICIO");

        //            // Título principal
        //            portada.Cells["A1:H1"].Merge = true;
        //            portada.Cells["A1"].Value = "REPORTE DE PAGO POR HORAS";
        //            portada.Cells["A1"].Style.Font.Size = 22;
        //            portada.Cells["A1"].Style.Font.Bold = true;
        //            portada.Cells["A1"].Style.Font.Color.SetColor(Color.FromArgb(54, 86, 139));
        //            portada.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        //            // Subtítulo
        //            portada.Cells["A3:H3"].Merge = true;
        //            portada.Cells["A3"].Value = $"Período: {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}";
        //            portada.Cells["A3"].Style.Font.Size = 14;
        //            portada.Cells["A3"].Style.Font.Italic = true;
        //            portada.Cells["A3"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        //            portada.Cells["A5:H5"].Merge = true;
        //            portada.Cells["A5"].Value = $"Fecha de generación: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
        //            portada.Cells["A5"].Style.Font.Size = 11;
        //            portada.Cells["A5"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        //            // Calcular días del período
        //            int totalDias = (fechaFin - fechaInicio).Days + 1;
        //            int diasLaborables = 0;
        //            int domingos = 0;

        //            for (DateTime d = fechaInicio; d <= fechaFin; d = d.AddDays(1))
        //            {
        //                if (d.DayOfWeek == DayOfWeek.Sunday)
        //                    domingos++;
        //                else
        //                    diasLaborables++;
        //            }

        //            // Información del período
        //            int rowInfo = 7;
        //            portada.Cells[rowInfo, 1].Value = "INFORMACIÓN DEL PERÍODO:";
        //            portada.Cells[rowInfo, 1].Style.Font.Bold = true;
        //            portada.Cells[rowInfo, 1].Style.Font.Size = 12;
        //            rowInfo += 2;

        //            portada.Cells[rowInfo, 1].Value = "Total de días en el período:";
        //            portada.Cells[rowInfo, 2].Value = totalDias;
        //            portada.Cells[rowInfo, 4].Value = "Días laborables (Lun a Sáb):";
        //            portada.Cells[rowInfo, 5].Value = diasLaborables;
        //            rowInfo += 1;

        //            portada.Cells[rowInfo, 1].Value = "Domingos:";
        //            portada.Cells[rowInfo, 2].Value = domingos;
        //            portada.Cells[rowInfo, 4].Value = "Horas esperadas (8h x día):";
        //            portada.Cells[rowInfo, 5].Value = diasLaborables * 8;
        //            rowInfo += 2;

        //            // Filtros aplicados
        //            portada.Cells[rowInfo, 1].Value = "FILTROS APLICADOS:";
        //            portada.Cells[rowInfo, 1].Style.Font.Bold = true;
        //            portada.Cells[rowInfo, 1].Style.Font.Size = 12;
        //            rowInfo += 2;

        //            portada.Cells[rowInfo, 1].Value = "Fecha Inicio:";
        //            portada.Cells[rowInfo, 1].Style.Font.Bold = true;
        //            portada.Cells[rowInfo, 2].Value = fechaInicio.ToString("dd/MM/yyyy");
        //            portada.Cells[rowInfo, 4].Value = "Fecha Fin:";
        //            portada.Cells[rowInfo, 4].Style.Font.Bold = true;
        //            portada.Cells[rowInfo, 5].Value = fechaFin.ToString("dd/MM/yyyy");
        //            rowInfo += 1;

        //            if (!string.IsNullOrEmpty(rolPago))
        //            {
        //                portada.Cells[rowInfo, 1].Value = "Rol de Pago:";
        //                portada.Cells[rowInfo, 1].Style.Font.Bold = true;
        //                portada.Cells[rowInfo, 2].Value = rolPago;
        //                rowInfo += 1;
        //            }

        //            if (usuarioId.HasValue && usuarioId.Value > 0)
        //            {
        //                portada.Cells[rowInfo, 1].Value = "Usuario filtrado:";
        //                portada.Cells[rowInfo, 1].Style.Font.Bold = true;
        //                portada.Cells[rowInfo, 2].Value = "Empleado específico";
        //                rowInfo += 1;
        //            }

        //            // Tabla de resumen
        //            rowInfo += 2;
        //            portada.Cells[rowInfo, 1].Value = "RESUMEN GENERAL DEL PERÍODO";
        //            portada.Cells[rowInfo, 1].Style.Font.Bold = true;
        //            portada.Cells[rowInfo, 1].Style.Font.Size = 14;
        //            rowInfo += 1;

        //            string[] headersResumen = { "Empleado", "Rol de Pago", "Tarifa x Hora", "Días Trabajados", "Horas Totales", "Monto Total" };
        //            for (int i = 0; i < headersResumen.Length; i++)
        //            {
        //                portada.Cells[rowInfo, i + 1].Value = headersResumen[i];
        //                portada.Cells[rowInfo, i + 1].Style.Font.Bold = true;
        //                portada.Cells[rowInfo, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
        //                portada.Cells[rowInfo, i + 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(54, 86, 139));
        //                portada.Cells[rowInfo, i + 1].Style.Font.Color.SetColor(Color.White);
        //            }
        //            rowInfo++;

        //            decimal totalHorasGeneral = 0;
        //            decimal totalMontoGeneral = 0;

        //            foreach (DataRow dr in resumenGeneral.Rows)
        //            {
        //                portada.Cells[rowInfo, 1].Value = dr["NombreCompleto"].ToString();
        //                portada.Cells[rowInfo, 2].Value = dr["RolPago"].ToString();
        //                portada.Cells[rowInfo, 3].Value = Convert.ToDecimal(dr["TarifaHora"]);
        //                portada.Cells[rowInfo, 4].Value = Convert.ToInt32(dr["DiasTrabajados"]);
        //                portada.Cells[rowInfo, 5].Value = Convert.ToDecimal(dr["TotalHoras"]);
        //                portada.Cells[rowInfo, 6].Value = Convert.ToDecimal(dr["MontoTotal"]);

        //                portada.Cells[rowInfo, 3].Style.Numberformat.Format = "S/ 0.00";
        //                portada.Cells[rowInfo, 5].Style.Numberformat.Format = "0.00";
        //                portada.Cells[rowInfo, 6].Style.Numberformat.Format = "S/ 0.00";

        //                totalHorasGeneral += Convert.ToDecimal(dr["TotalHoras"]);
        //                totalMontoGeneral += Convert.ToDecimal(dr["MontoTotal"]);
        //                rowInfo++;
        //            }

        //            // Totales generales
        //            portada.Cells[rowInfo, 4].Value = "TOTAL GENERAL:";
        //            portada.Cells[rowInfo, 4].Style.Font.Bold = true;
        //            portada.Cells[rowInfo, 5].Value = totalHorasGeneral;
        //            portada.Cells[rowInfo, 5].Style.Numberformat.Format = "0.00";
        //            portada.Cells[rowInfo, 5].Style.Font.Bold = true;
        //            portada.Cells[rowInfo, 6].Value = totalMontoGeneral;
        //            portada.Cells[rowInfo, 6].Style.Numberformat.Format = "S/ 0.00";
        //            portada.Cells[rowInfo, 6].Style.Font.Bold = true;
        //            portada.Cells[rowInfo, 6].Style.Font.Color.SetColor(Color.Green);

        //            portada.Cells[1, 1, rowInfo, 6].AutoFitColumns();

        //            // ============================================
        //            // HOJAS INDIVIDUALES POR CADA EMPLEADO
        //            // ============================================
        //            foreach (DataRow dr in resumenGeneral.Rows)
        //            {
        //                int usuarioIdDetalle = Convert.ToInt32(dr["UsuarioId"]);
        //                string nombreEmpleado = dr["NombreCompleto"].ToString();
        //                string rolPagoEmpleado = dr["RolPago"].ToString();
        //                decimal tarifaHora = Convert.ToDecimal(dr["TarifaHora"]);

        //                string nombreHoja = nombreEmpleado.Length > 31 ? nombreEmpleado.Substring(0, 28) + ".." : nombreEmpleado;
        //                nombreHoja = nombreHoja.Replace("/", "").Replace("\\", "").Replace("?", "").Replace("*", "").Replace("[", "").Replace("]", "").Replace(":", "");

        //                SqlParameter[] detalleParams = new SqlParameter[]
        //                {
        //            new SqlParameter("@FechaInicio", fechaInicio),
        //            new SqlParameter("@FechaFin", fechaFin),
        //            new SqlParameter("@UsuarioId", usuarioIdDetalle)
        //                };

        //                DataTable detalle = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerDetallePagoPorUsuario", detalleParams);

        //                var hoja = package.Workbook.Worksheets.Add(nombreHoja);

        //                // Encabezado
        //                hoja.Cells["A1:J1"].Merge = true;
        //                hoja.Cells["A1"].Value = "REPORTE DE ASISTENCIA Y PAGO POR HORAS";
        //                hoja.Cells["A1"].Style.Font.Size = 16;
        //                hoja.Cells["A1"].Style.Font.Bold = true;
        //                hoja.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
        //                hoja.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(54, 86, 139));
        //                hoja.Cells["A1"].Style.Font.Color.SetColor(Color.White);
        //                hoja.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        //                // Información del empleado
        //                hoja.Cells["A3"].Value = "Empleado:";
        //                hoja.Cells["A3"].Style.Font.Bold = true;
        //                hoja.Cells["B3"].Value = nombreEmpleado;
        //                hoja.Cells["B3"].Style.Font.Bold = true;
        //                hoja.Cells["B3"].Style.Font.Color.SetColor(Color.FromArgb(54, 86, 139));

        //                hoja.Cells["A4"].Value = "Rol de Pago:";
        //                hoja.Cells["A4"].Style.Font.Bold = true;
        //                hoja.Cells["B4"].Value = rolPagoEmpleado;

        //                hoja.Cells["A5"].Value = "Tarifa por Hora:";
        //                hoja.Cells["A5"].Style.Font.Bold = true;
        //                hoja.Cells["B5"].Value = tarifaHora;
        //                hoja.Cells["B5"].Style.Numberformat.Format = "S/ 0.00";

        //                hoja.Cells["D3"].Value = "Período:";
        //                hoja.Cells["D3"].Style.Font.Bold = true;
        //                hoja.Cells["E3"].Value = $"{fechaInicio:dd/MM/yyyy} - {fechaFin:dd/MM/yyyy}";

        //                hoja.Cells["D4"].Value = "Días Laborables:";
        //                hoja.Cells["D4"].Style.Font.Bold = true;
        //                hoja.Cells["E4"].Value = diasLaborables;

        //                hoja.Cells["D5"].Value = "Horas Esperadas:";
        //                hoja.Cells["D5"].Style.Font.Bold = true;
        //                hoja.Cells["E5"].Value = diasLaborables * 8;
        //                hoja.Cells["E5"].Style.Numberformat.Format = "0";

        //                // Tabla de registros
        //                int filaTabla = 7;
        //                string[] headers = { "N°", "Fecha", "Día", "Entrada", "Salida", "Salida Alm.", "Retorno Alm.", "Horas", "Monto", "Observaciones" };
        //                for (int i = 0; i < headers.Length; i++)
        //                {
        //                    hoja.Cells[filaTabla, i + 1].Value = headers[i];
        //                    hoja.Cells[filaTabla, i + 1].Style.Font.Bold = true;
        //                    hoja.Cells[filaTabla, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
        //                    hoja.Cells[filaTabla, i + 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(54, 86, 139));
        //                    hoja.Cells[filaTabla, i + 1].Style.Font.Color.SetColor(Color.White);
        //                    hoja.Cells[filaTabla, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
        //                }
        //                filaTabla++;

        //                int diaNumero = 1;
        //                decimal totalHorasEmpleado = 0;
        //                decimal totalMontoEmpleado = 0;
        //                int diasTrabajadosCount = 0;

        //                var registrosPorFecha = new Dictionary<DateTime, DataRow>();
        //                foreach (DataRow det in detalle.Rows)
        //                {
        //                    DateTime fecha = Convert.ToDateTime(det["Fecha"]);
        //                    registrosPorFecha[fecha] = det;
        //                }

        //                for (DateTime fecha = fechaInicio; fecha <= fechaFin; fecha = fecha.AddDays(1))
        //                {
        //                    string diaSemana = ObtenerDiaSemana(fecha);

        //                    hoja.Cells[filaTabla, 1].Value = diaNumero;
        //                    hoja.Cells[filaTabla, 2].Value = fecha.ToString("dd/MM/yyyy");
        //                    hoja.Cells[filaTabla, 3].Value = diaSemana;

        //                    if (registrosPorFecha.ContainsKey(fecha))
        //                    {
        //                        DataRow registro = registrosPorFecha[fecha];
        //                        decimal horas = Convert.ToDecimal(registro["HorasTrabajadas"]);
        //                        decimal monto = Convert.ToDecimal(registro["MontoDia"]);

        //                        hoja.Cells[filaTabla, 4].Value = registro["HoraEntrada"]?.ToString();
        //                        hoja.Cells[filaTabla, 5].Value = registro["HoraSalida"]?.ToString();
        //                        hoja.Cells[filaTabla, 6].Value = registro["SalidaAlmuerzo"]?.ToString();
        //                        hoja.Cells[filaTabla, 7].Value = registro["RetornoAlmuerzo"]?.ToString();
        //                        hoja.Cells[filaTabla, 8].Value = horas;
        //                        hoja.Cells[filaTabla, 9].Value = monto;
        //                        hoja.Cells[filaTabla, 10].Value = registro["Comentarios"]?.ToString();

        //                        hoja.Cells[filaTabla, 8].Style.Numberformat.Format = "0.00";
        //                        hoja.Cells[filaTabla, 9].Style.Numberformat.Format = "S/ 0.00";

        //                        totalHorasEmpleado += horas;
        //                        totalMontoEmpleado += monto;
        //                        diasTrabajadosCount++;

        //                        string horaEntrada = registro["HoraEntrada"]?.ToString();
        //                        if (!string.IsNullOrEmpty(horaEntrada) && string.Compare(horaEntrada, "08:15") > 0)
        //                        {
        //                            hoja.Cells[filaTabla, 4].Style.Font.Color.SetColor(Color.Orange);
        //                            hoja.Cells[filaTabla, 4].Style.Font.Bold = true;
        //                        }
        //                    }
        //                    else
        //                    {
        //                        hoja.Cells[filaTabla, 4].Value = "-";
        //                        hoja.Cells[filaTabla, 5].Value = "-";
        //                        hoja.Cells[filaTabla, 6].Value = "-";
        //                        hoja.Cells[filaTabla, 7].Value = "-";
        //                        hoja.Cells[filaTabla, 8].Value = 0;
        //                        hoja.Cells[filaTabla, 9].Value = 0;
        //                        hoja.Cells[filaTabla, 8].Style.Numberformat.Format = "0.00";
        //                        hoja.Cells[filaTabla, 9].Style.Numberformat.Format = "S/ 0.00";

        //                        hoja.Cells[filaTabla, 1, filaTabla, 10].Style.Fill.PatternType = ExcelFillStyle.Solid;
        //                        hoja.Cells[filaTabla, 1, filaTabla, 10].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
        //                    }

        //                    hoja.Cells[filaTabla, 1, filaTabla, 10].Style.Border.Top.Style = ExcelBorderStyle.Thin;
        //                    hoja.Cells[filaTabla, 1, filaTabla, 10].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

        //                    filaTabla++;
        //                    diaNumero++;
        //                }

        //                filaTabla++;

        //                hoja.Cells[filaTabla, 7].Value = "TOTALES:";
        //                hoja.Cells[filaTabla, 7].Style.Font.Bold = true;
        //                hoja.Cells[filaTabla, 8].Value = totalHorasEmpleado;
        //                hoja.Cells[filaTabla, 8].Style.Numberformat.Format = "0.00";
        //                hoja.Cells[filaTabla, 8].Style.Font.Bold = true;
        //                hoja.Cells[filaTabla, 9].Value = totalMontoEmpleado;
        //                hoja.Cells[filaTabla, 9].Style.Numberformat.Format = "S/ 0.00";
        //                hoja.Cells[filaTabla, 9].Style.Font.Bold = true;
        //                hoja.Cells[filaTabla, 9].Style.Font.Color.SetColor(Color.Green);

        //                filaTabla += 2;

        //                hoja.Cells[filaTabla, 1].Value = "RESUMEN DE PAGO";
        //                hoja.Cells[filaTabla, 1].Style.Font.Size = 14;
        //                hoja.Cells[filaTabla, 1].Style.Font.Bold = true;
        //                hoja.Cells[filaTabla, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
        //                hoja.Cells[filaTabla, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(54, 86, 139));
        //                hoja.Cells[filaTabla, 1].Style.Font.Color.SetColor(Color.White);
        //                hoja.Cells[filaTabla, 1, filaTabla, 5].Merge = true;
        //                filaTabla++;

        //                hoja.Cells[filaTabla, 1].Value = "Horas Totales Trabajadas:";
        //                hoja.Cells[filaTabla, 2].Value = totalHorasEmpleado;
        //                hoja.Cells[filaTabla, 2].Style.Numberformat.Format = "0.00";
        //                filaTabla++;

        //                hoja.Cells[filaTabla, 1].Value = "Días Trabajados:";
        //                hoja.Cells[filaTabla, 2].Value = diasTrabajadosCount;
        //                filaTabla++;

        //                hoja.Cells[filaTabla, 1].Value = "Tarifa por Hora:";
        //                hoja.Cells[filaTabla, 2].Value = tarifaHora;
        //                hoja.Cells[filaTabla, 2].Style.Numberformat.Format = "S/ 0.00";
        //                filaTabla++;

        //                hoja.Cells[filaTabla, 1].Value = "MONTO TOTAL A PAGAR:";
        //                hoja.Cells[filaTabla, 1].Style.Font.Bold = true;
        //                hoja.Cells[filaTabla, 2].Value = totalMontoEmpleado;
        //                hoja.Cells[filaTabla, 2].Style.Numberformat.Format = "S/ 0.00";
        //                hoja.Cells[filaTabla, 2].Style.Font.Bold = true;
        //                hoja.Cells[filaTabla, 2].Style.Font.Size = 14;
        //                hoja.Cells[filaTabla, 2].Style.Font.Color.SetColor(Color.Green);

        //                decimal horasEsperadas = diasLaborables * 8;
        //                decimal diferencia = totalHorasEmpleado - horasEsperadas;
        //                if (diferencia < 0)
        //                {
        //                    filaTabla += 2;
        //                    hoja.Cells[filaTabla, 1].Value = $"NOTA: Horas faltantes: {Math.Abs(diferencia):F2} horas para alcanzar el objetivo de {horasEsperadas:F0} horas.";
        //                    hoja.Cells[filaTabla, 1].Style.Font.Italic = true;
        //                    hoja.Cells[filaTabla, 1].Style.Font.Color.SetColor(Color.Red);
        //                }

        //                hoja.Cells[1, 1, filaTabla + 2, 10].AutoFitColumns();
        //            }

        //            var bytes = package.GetAsByteArray();
        //            var result = new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        //            result.FileDownloadName = $"ReportePagos_{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.xlsx";

        //            return result;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        TempData["Error"] = "Error al exportar: " + ex.Message;
        //        return RedirectToAction("ReportePagos");
        //    }
        //}

        private string ObtenerDiaSemana(DateTime fecha)
        {
            switch (fecha.DayOfWeek)
            {
                case DayOfWeek.Monday: return "LUNES";
                case DayOfWeek.Tuesday: return "MARTES";
                case DayOfWeek.Wednesday: return "MIÉRCOLES";
                case DayOfWeek.Thursday: return "JUEVES";
                case DayOfWeek.Friday: return "VIERNES";
                case DayOfWeek.Saturday: return "SÁBADO";
                case DayOfWeek.Sunday: return "DOMINGO";
                default: return "";
            }
        }

        // ============================================
        // CALCULAR DÍAS LABORABLES EN EL RANGO SELECCIONADO
        // ============================================

        // Para FACILITADOR: Lunes a Viernes (5 días por semana)
        private int CalcularDiasLaborablesFacilitador(DateTime fechaInicio, DateTime fechaFin)
        {
            int diasLaborables = 0;
            for (DateTime fecha = fechaInicio; fecha <= fechaFin; fecha = fecha.AddDays(1))
            {
                if ((int)fecha.DayOfWeek >= 1 && (int)fecha.DayOfWeek <= 5)
                {
                    diasLaborables++;
                }
            }
            return diasLaborables;
        }

        // Para PLANILLA: Lunes a Sábado (6 días por semana)
        private int CalcularDiasLaborablesPlanilla(DateTime fechaInicio, DateTime fechaFin)
        {
            int diasLaborables = 0;
            for (DateTime fecha = fechaInicio; fecha <= fechaFin; fecha = fecha.AddDays(1))
            {
                if ((int)fecha.DayOfWeek >= 1 && (int)fecha.DayOfWeek <= 6)
                {
                    diasLaborables++;
                }
            }
            return diasLaborables;
        }

        // ============================================
        // CALCULAR DÍAS LABORABLES EN EL RANGO SELECCIONADO (Lunes a Sábado)
        // ============================================
        private int CalcularDiasLaborables(DateTime fechaInicio, DateTime fechaFin)
        {
            int diasLaborables = 0;
            for (DateTime fecha = fechaInicio; fecha <= fechaFin; fecha = fecha.AddDays(1))
            {
                if ((int)fecha.DayOfWeek >= 1 && (int)fecha.DayOfWeek <= 6)
                {
                    diasLaborables++;
                }
            }
            return diasLaborables;
        }

        [HttpPost]
        public JsonResult BuscarUsuarios(string termino)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                string query = "";
                SqlParameter[] parameters = null;

                // ============================================
                // Si no hay término de búsqueda, mostrar TODOS los usuarios
                // ============================================
                if (string.IsNullOrWhiteSpace(termino))
                {
                    query = @"SELECT Id, NombreCompleto, Email, Rol 
                      FROM Usuarios 
                      WHERE Rol != 'Admin'
                      ORDER BY NombreCompleto";
                }
                else
                {
                    // Si hay término, buscar por coincidencia
                    query = @"SELECT Id, NombreCompleto, Email, Rol 
                      FROM Usuarios 
                      WHERE (NombreCompleto LIKE @Termino 
                         OR Email LIKE @Termino
                         OR DNI LIKE @Termino)
                        AND Rol != 'Admin'
                      ORDER BY NombreCompleto";

                    parameters = new SqlParameter[]
                    {
                new SqlParameter("@Termino", "%" + termino + "%")
                    };
                }

                DataTable dt = DatabaseHelper.ExecuteQuery(query, parameters);
                List<object> usuarios = new List<object>();

                foreach (DataRow row in dt.Rows)
                {
                    usuarios.Add(new
                    {
                        Id = row["Id"],
                        NombreCompleto = row["NombreCompleto"].ToString(),
                        Email = row["Email"].ToString(),
                        Rol = row["Rol"].ToString()
                    });
                }

                return Json(new { success = true, data = usuarios });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost]
        public JsonResult CambiarContrasena(int usuarioId, string nuevaContrasena)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                if (string.IsNullOrEmpty(nuevaContrasena) || nuevaContrasena.Length < 6)
                    return Json(new { success = false, message = "La contraseña debe tener al menos 6 caracteres" });

                // ============================================
                // CORREGIDO: Usar SHA1 (40 caracteres) como en tu sistema original
                // ============================================
                string passwordEncriptada = EncryptPasswordSHA1(nuevaContrasena);

                string query = "UPDATE Usuarios SET PasswordHash = @Password WHERE Id = @UsuarioId";
                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@Password", passwordEncriptada),
            new SqlParameter("@UsuarioId", usuarioId)
                };

                int filas = DatabaseHelper.ExecuteNonQuery(query, parameters);

                if (filas > 0)
                    return Json(new { success = true, message = "Contraseña cambiada exitosamente" });
                else
                    return Json(new { success = false, message = "No se pudo cambiar la contraseña" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string EncryptPasswordSHA1(string password)
        {
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                byte[] bytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                // Convertir a hexadecimal en mayúsculas (formato que usas)
                return BitConverter.ToString(bytes).Replace("-", "").ToUpper();
            }
        }


        // GET: Admin/RecuperarContrasena
        public ActionResult RecuperarContrasena()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }

        // GET: Admin/RecuperarUser
        public ActionResult RecuperarUser()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }

        // Método auxiliar para encriptar contraseña (ajústalo según tu método actual)
        // GET: Admin/ControlPagos
        public ActionResult ControlPagos()
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
            {
                return RedirectToAction("Login", "Account");
            }

            string query = "SELECT * FROM ConfiguracionPagos ORDER BY Id";
            DataTable dt = DatabaseHelper.ExecuteQuery(query, null);

            return View(dt);
        }

        // POST: Admin/GuardarConfiguracionPago
        [HttpPost]
        public JsonResult GuardarConfiguracionPago(int id, decimal montoBase, string diasBase, decimal valorHora,
            string valorDia, string valorMinuto, string horasDiarias, string horasSemana,
            string horasSabado, string horasMensuales, int activo)
        {
            if (Session["Rol"] == null || Session["Rol"].ToString() != "Admin")
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                // Validar campos obligatorios
                if (montoBase <= 0)
                    return Json(new { success = false, message = "El Monto Base debe ser mayor a 0" });

                if (valorHora <= 0)
                    return Json(new { success = false, message = "El Valor Hora debe ser mayor a 0" });

                // Construir la consulta dinámicamente (solo campos que vienen con valor)
                List<string> updates = new List<string>();
                List<SqlParameter> parameters = new List<SqlParameter>();

                // Campo obligatorio - siempre se actualiza
                updates.Add("MontoBase = @MontoBase");
                parameters.Add(new SqlParameter("@MontoBase", montoBase));

                updates.Add("ValorHora = @ValorHora");
                parameters.Add(new SqlParameter("@ValorHora", valorHora));

                updates.Add("Activo = @Activo");
                parameters.Add(new SqlParameter("@Activo", activo));

                // Campos opcionales - solo si tienen valor
                if (!string.IsNullOrWhiteSpace(diasBase))
                {
                    updates.Add("DiasBase = @DiasBase");
                    parameters.Add(new SqlParameter("@DiasBase", Convert.ToInt32(diasBase)));
                }

                if (!string.IsNullOrWhiteSpace(valorDia))
                {
                    updates.Add("ValorDia = @ValorDia");
                    parameters.Add(new SqlParameter("@ValorDia", Convert.ToDecimal(valorDia)));
                }

                if (!string.IsNullOrWhiteSpace(valorMinuto))
                {
                    updates.Add("ValorMinuto = @ValorMinuto");
                    parameters.Add(new SqlParameter("@ValorMinuto", Convert.ToDecimal(valorMinuto)));
                }

                if (!string.IsNullOrWhiteSpace(horasDiarias))
                {
                    updates.Add("HorasDiarias = @HorasDiarias");
                    parameters.Add(new SqlParameter("@HorasDiarias", Convert.ToInt32(horasDiarias)));
                }

                if (!string.IsNullOrWhiteSpace(horasSemana))
                {
                    updates.Add("HorasSemana = @HorasSemana");
                    parameters.Add(new SqlParameter("@HorasSemana", Convert.ToInt32(horasSemana)));
                }

                if (!string.IsNullOrWhiteSpace(horasSabado))
                {
                    updates.Add("HorasSabado = @HorasSabado");
                    parameters.Add(new SqlParameter("@HorasSabado", Convert.ToInt32(horasSabado)));
                }

                if (!string.IsNullOrWhiteSpace(horasMensuales))
                {
                    updates.Add("HorasMensuales = @HorasMensuales");
                    parameters.Add(new SqlParameter("@HorasMensuales", Convert.ToInt32(horasMensuales)));
                }

                updates.Add("FechaActualizacion = GETDATE()");

                string query = $"UPDATE ConfiguracionPagos SET {string.Join(", ", updates)} WHERE Id = @Id";
                parameters.Add(new SqlParameter("@Id", id));

                DatabaseHelper.ExecuteNonQuery(query, parameters.ToArray());

                return Json(new { success = true, message = "Configuración guardada exitosamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


    }
}