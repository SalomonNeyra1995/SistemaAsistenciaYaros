using ControlAsistenciaFinal.Services;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Data.SqlClient;

namespace ControlAsistenciaFinal.Controllers
{
    public class BackupController : Controller
    {
        private readonly BackupService _backupService;
        private readonly string _backupRootFolder;

        public BackupController()
        {
            _backupService = new BackupService();
            _backupRootFolder = System.Web.Hosting.HostingEnvironment.MapPath("~/Backups");

            // Crear la carpeta si no existe
            if (!Directory.Exists(_backupRootFolder))
                Directory.CreateDirectory(_backupRootFolder);
        }

        // Validar acceso manualmente
        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            base.OnActionExecuting(filterContext);

            if (Session["UsuarioId"] == null)
            {
                filterContext.Result = new RedirectToRouteResult(
                    new System.Web.Routing.RouteValueDictionary {
                        { "controller", "Account" },
                        { "action", "Login" }
                    }
                );
                return;
            }

            var rol = Session["Rol"]?.ToString();
            if (rol != "Admin")
            {
                filterContext.Result = new RedirectToRouteResult(
                    new System.Web.Routing.RouteValueDictionary {
                        { "controller", "Home" },
                        { "action", "Index" }
                    }
                );
                return;
            }
        }

        public ActionResult Index()
        {
            var backups = _backupService.ObtenerBackupsDisponibles();
            return View(backups);
        }

        [HttpPost]
        public async Task<JsonResult> GenerarBackup(int anio, int mes)
        {
            try
            {
                var ruta = await _backupService.GenerarBackupMensualAsync(anio, mes);
                return Json(new { success = true, mensaje = $"Backup del mes {mes:00}/{anio} generado correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, mensaje = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<JsonResult> GenerarBackupMesAnterior()
        {
            try
            {
                var fechaActual = DateTime.Now;
                var mesAnterior = fechaActual.AddMonths(-1);
                var ruta = await _backupService.GenerarBackupMensualAsync(mesAnterior.Year, mesAnterior.Month);
                return Json(new { success = true, mensaje = "Backup del mes anterior generado correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, mensaje = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        public FileResult DescargarBackup(int anio, int mes, string archivo)
        {
            var ruta = _backupService.ObtenerRutaArchivoBackup(anio, mes, archivo);
            byte[] fileBytes = System.IO.File.ReadAllBytes(ruta);
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", archivo);
        }

        [HttpPost]
        public async Task<JsonResult> RestaurarBackup(int anio, int mes, string archivo, List<string> tablas)
        {
            try
            {
                var ruta = _backupService.ObtenerRutaArchivoBackup(anio, mes, archivo);
                var resultado = await _backupService.RestaurarDesdeBackup(ruta, tablas);

                return Json(new
                {
                    success = resultado.Exito,
                    mensaje = resultado.Mensaje,
                    registros = resultado.RegistrosInsertados,
                    omitidos = resultado.RegistrosOmitidos,
                    errores = resultado.RegistrosConError,
                    totalProcesados = resultado.TotalProcesados,
                    totalDuplicados = resultado.TotalDuplicados,
                    registrosPorTabla = resultado.RegistrosPorTabla,
                    omitidosPorTabla = resultado.OmitidosPorTabla,
                    erroresDetallados = resultado.ErroresDetallados,
                    registrosOmitidosDetalle = resultado.RegistrosOmitidosDetalle,
                    tablasEstado = resultado.TablasEstado,
                    error = resultado.Error
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    mensaje = $"Error: {ex.Message}",
                    erroresDetallados = new List<string> { ex.Message },
                    errores = 1
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SubirYRestaurarBackup()
        {
            try
            {
                if (Request.Files.Count == 0)
                    return Json(new { success = false, mensaje = "No se seleccionó ningún archivo" });

                var archivo = Request.Files[0];
                if (archivo == null || archivo.ContentLength == 0)
                    return Json(new { success = false, mensaje = "Archivo inválido o vacío" });

                // Validar extensión
                var extension = System.IO.Path.GetExtension(archivo.FileName).ToLower();
                if (extension != ".xlsx")
                    return Json(new { success = false, mensaje = "Solo se permiten archivos Excel (.xlsx)" });

                // Crear carpeta temporal si no existe
                string tempFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp");
                if (!System.IO.Directory.Exists(tempFolder))
                    System.IO.Directory.CreateDirectory(tempFolder);

                // Guardar archivo temporal
                string nombreArchivo = $"Temp_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                string rutaTemp = System.IO.Path.Combine(tempFolder, nombreArchivo);
                archivo.SaveAs(rutaTemp);

                // Leer el archivo para obtener las tablas disponibles
                var tablasDisponibles = new List<string>();
                using (var package = new ExcelPackage(new System.IO.FileInfo(rutaTemp)))
                {
                    foreach (var worksheet in package.Workbook.Worksheets)
                    {
                        if (worksheet.Name != "Info_Backup")
                        {
                            tablasDisponibles.Add(worksheet.Name);
                        }
                    }
                }

                // Guardar la ruta del archivo en sesión para la restauración
                Session["ArchivoRestauracionTemp"] = rutaTemp;

                return Json(new
                {
                    success = true,
                    mensaje = "Archivo cargado correctamente",
                    nombreArchivo = nombreArchivo,
                    tablas = tablasDisponibles
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, mensaje = $"Error al cargar archivo: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<JsonResult> RestaurarDesdeArchivoTemp(List<string> tablas)
        {
            try
            {
                var rutaTemp = Session["ArchivoRestauracionTemp"]?.ToString();
                if (string.IsNullOrEmpty(rutaTemp) || !System.IO.File.Exists(rutaTemp))
                    return Json(new { success = false, mensaje = "No hay ningún archivo cargado para restaurar" });

                var resultado = await _backupService.RestaurarDesdeBackup(rutaTemp, tablas);

                // Limpiar archivo temporal después de la restauración
                try { System.IO.File.Delete(rutaTemp); } catch { }
                Session.Remove("ArchivoRestauracionTemp");

                return Json(new
                {
                    success = resultado.Exito,
                    mensaje = resultado.Mensaje,
                    registros = resultado.RegistrosInsertados,
                    omitidos = resultado.RegistrosOmitidos,
                    errores = resultado.RegistrosConError,
                    totalProcesados = resultado.TotalProcesados,
                    totalDuplicados = resultado.TotalDuplicados,
                    registrosPorTabla = resultado.RegistrosPorTabla,
                    omitidosPorTabla = resultado.OmitidosPorTabla,
                    erroresDetallados = resultado.ErroresDetallados,
                    registrosOmitidosDetalle = resultado.RegistrosOmitidosDetalle,
                    tablasEstado = resultado.TablasEstado,
                    error = resultado.Error
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    mensaje = $"Error al restaurar: {ex.Message}",
                    erroresDetallados = new List<string> { ex.Message },
                    errores = 1
                });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> GenerarBackupAutomatico()
        {
            try
            {
                var fechaActual = DateTime.Now;
                var mesActual = fechaActual.Month;
                var anioActual = fechaActual.Year;

                var ruta = await _backupService.GenerarBackupMensualCompletoAsync(anioActual, mesActual);

                // Guardar log
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backup_log.txt");
                System.IO.File.AppendAllText(logPath, $"{DateTime.Now}: Backup del mes {mesActual:00}/{anioActual} generado en {ruta}\r\n");

                return Json(new { success = true, mensaje = $"Backup del mes {mesActual:00}/{anioActual} generado correctamente", ruta });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, mensaje = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult ObtenerTablasBackup(int anio, int mes, string archivo)
        {
            try
            {
                var ruta = _backupService.ObtenerRutaArchivoBackup(anio, mes, archivo);
                var tablas = new List<string>();

                using (var package = new ExcelPackage(new System.IO.FileInfo(ruta)))
                {
                    foreach (var worksheet in package.Workbook.Worksheets)
                    {
                        if (worksheet.Name != "Info_Backup")
                        {
                            tablas.Add(worksheet.Name);
                        }
                    }
                }

                return Json(new { success = true, tablas }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // ============================================
        // MÉTODOS PARA ELIMINAR RESPALDOS
        // ============================================

        [HttpPost]
        public JsonResult EliminarBackup(int anio, int mes, string archivo)
        {
            try
            {
                var ruta = _backupService.ObtenerRutaArchivoBackup(anio, mes, archivo);
                if (System.IO.File.Exists(ruta))
                {
                    System.IO.File.Delete(ruta);

                    // Si la carpeta queda vacía, eliminarla también
                    var carpeta = System.IO.Path.GetDirectoryName(ruta);
                    if (Directory.Exists(carpeta) && !Directory.GetFiles(carpeta).Any())
                    {
                        Directory.Delete(carpeta);
                    }

                    return Json(new { success = true, mensaje = $"Archivo '{archivo}' eliminado correctamente" });
                }
                else
                {
                    return Json(new { success = false, mensaje = "El archivo no existe" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, mensaje = $"Error al eliminar: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult EliminarTodosBackups(int anio, int mes)
        {
            try
            {
                var nombreCarpeta = $"BackAsistencia-{anio:D4}-{mes:D2}";
                var rutaCarpeta = Path.Combine(_backupRootFolder, nombreCarpeta);

                if (Directory.Exists(rutaCarpeta))
                {
                    // Eliminar todos los archivos dentro de la carpeta
                    foreach (var archivo in Directory.GetFiles(rutaCarpeta))
                    {
                        System.IO.File.Delete(archivo);
                    }

                    // Eliminar la carpeta
                    Directory.Delete(rutaCarpeta);

                    return Json(new { success = true, mensaje = $"Todos los respaldos de {nombreCarpeta} eliminados correctamente" });
                }
                else
                {
                    return Json(new { success = false, mensaje = "La carpeta no existe" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, mensaje = $"Error al eliminar: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult EliminarTodosLosBackups()
        {
            try
            {
                if (Directory.Exists(_backupRootFolder))
                {
                    // Eliminar todas las carpetas y archivos dentro de Backups
                    foreach (var carpeta in Directory.GetDirectories(_backupRootFolder))
                    {
                        foreach (var archivo in Directory.GetFiles(carpeta))
                        {
                            System.IO.File.Delete(archivo);
                        }
                        Directory.Delete(carpeta);
                    }

                    return Json(new { success = true, mensaje = "Todos los respaldos eliminados correctamente" });
                }
                else
                {
                    return Json(new { success = false, mensaje = "No hay respaldos para eliminar" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, mensaje = $"Error al eliminar: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult LimpiarTablas(List<string> tablas)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            var tablasLimpiadas = new List<string>();

                            // ORDEN DE ELIMINACIÓN (primero las que tienen dependencias)
                            // Las tablas con claves foráneas deben eliminarse ANTES que las tablas referenciadas
                            var ordenTablas = new List<string> {
                        "AlertasSalidaPendiente",  // <-- AGREGADA: depende de Usuarios
                        "RegistrosExcepcionales",
                        "RegistrosAsistencia",
                        "AlertasHoras",
                        "NotificacionesAdmin",
                        "SolicitudesExcepcionales",
                        "Usuarios"
                    };

                            // Si no se especifican tablas, limpiar todas
                            if (tablas == null || tablas.Count == 0)
                            {
                                tablas = ordenTablas;
                            }

                            // Desactivar restricciones
                            foreach (var tabla in ordenTablas)
                            {
                                try
                                {
                                    if (tablas.Contains(tabla))
                                    {
                                        string disableConstraints = $"ALTER TABLE {tabla} NOCHECK CONSTRAINT ALL";
                                        using (var cmdDisable = new SqlCommand(disableConstraints, conn, transaction))
                                        {
                                            cmdDisable.ExecuteNonQuery();
                                        }
                                    }
                                }
                                catch { }
                            }

                            // Eliminar datos y reiniciar IDENTITY
                            foreach (var tabla in ordenTablas)
                            {
                                if (!tablas.Contains(tabla)) continue;

                                try
                                {
                                    // Eliminar registros
                                    string deleteQuery = $"DELETE FROM {tabla}";
                                    int rowsAffected;
                                    using (var cmdDelete = new SqlCommand(deleteQuery, conn, transaction))
                                    {
                                        rowsAffected = cmdDelete.ExecuteNonQuery();
                                    }

                                    // Reiniciar IDENTITY
                                    string resetIdentity = $"DBCC CHECKIDENT ('{tabla}', RESEED, 0)";
                                    using (var cmdReset = new SqlCommand(resetIdentity, conn, transaction))
                                    {
                                        cmdReset.ExecuteNonQuery();
                                    }

                                    tablasLimpiadas.Add($"{tabla}: {rowsAffected} registros eliminados, IDENTITY reiniciado a 0");
                                }
                                catch (Exception ex)
                                {
                                    tablasLimpiadas.Add($"{tabla}: Error - {ex.Message}");
                                }
                            }

                            // Reestablecer restricciones
                            foreach (var tabla in ordenTablas)
                            {
                                try
                                {
                                    if (tablas.Contains(tabla))
                                    {
                                        string enableConstraints = $"ALTER TABLE {tabla} CHECK CONSTRAINT ALL";
                                        using (var cmdEnable = new SqlCommand(enableConstraints, conn, transaction))
                                        {
                                            cmdEnable.ExecuteNonQuery();
                                        }
                                    }
                                }
                                catch { }
                            }

                            transaction.Commit();
                            return Json(new
                            {
                                success = true,
                                mensaje = "Tablas limpiadas correctamente y contadores reiniciados",
                                detalles = tablasLimpiadas
                            });
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            return Json(new { success = false, mensaje = $"Error al limpiar tablas: {ex.Message}" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, mensaje = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult LimpiarTablaEspecifica(string tabla)
        {
            try
            {
                var tablasPermitidas = new List<string> {
            "Usuarios",
            "RegistrosAsistencia",
            "RegistrosExcepcionales",
            "AlertasHoras",
            "NotificacionesAdmin",
            "SolicitudesExcepcionales",
            "AlertasSalidaPendiente"  // <-- AGREGADA
        };

                if (!tablasPermitidas.Contains(tabla))
                    return Json(new { success = false, mensaje = "Tabla no permitida para limpieza" });

                using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // Si es Usuarios, primero limpiar AlertasSalidaPendiente
                            if (tabla == "Usuarios")
                            {
                                // Limpiar AlertasSalidaPendiente primero
                                string deleteAlertas = "DELETE FROM AlertasSalidaPendiente";
                                using (var cmdDeleteAlertas = new SqlCommand(deleteAlertas, conn, transaction))
                                {
                                    cmdDeleteAlertas.ExecuteNonQuery();
                                }

                                // Reiniciar IDENTITY de AlertasSalidaPendiente
                                string resetAlertas = "DBCC CHECKIDENT ('AlertasSalidaPendiente', RESEED, 0)";
                                using (var cmdResetAlertas = new SqlCommand(resetAlertas, conn, transaction))
                                {
                                    cmdResetAlertas.ExecuteNonQuery();
                                }
                            }

                            // Desactivar restricciones
                            string disableConstraints = $"ALTER TABLE {tabla} NOCHECK CONSTRAINT ALL";
                            using (var cmdDisable = new SqlCommand(disableConstraints, conn, transaction))
                            {
                                cmdDisable.ExecuteNonQuery();
                            }

                            // Eliminar registros
                            string deleteQuery = $"DELETE FROM {tabla}";
                            int rowsAffected;
                            using (var cmdDelete = new SqlCommand(deleteQuery, conn, transaction))
                            {
                                rowsAffected = cmdDelete.ExecuteNonQuery();
                            }

                            // Reiniciar IDENTITY
                            string resetIdentity = $"DBCC CHECKIDENT ('{tabla}', RESEED, 0)";
                            using (var cmdReset = new SqlCommand(resetIdentity, conn, transaction))
                            {
                                cmdReset.ExecuteNonQuery();
                            }

                            // Reestablecer restricciones
                            string enableConstraints = $"ALTER TABLE {tabla} CHECK CONSTRAINT ALL";
                            using (var cmdEnable = new SqlCommand(enableConstraints, conn, transaction))
                            {
                                cmdEnable.ExecuteNonQuery();
                            }

                            transaction.Commit();
                            return Json(new
                            {
                                success = true,
                                mensaje = $"Tabla '{tabla}' limpiada correctamente ({rowsAffected} registros eliminados, IDENTITY reiniciado a 0)"
                            });
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            return Json(new { success = false, mensaje = $"Error al limpiar tabla: {ex.Message}" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, mensaje = $"Error: {ex.Message}" });
            }
        }


    }
}