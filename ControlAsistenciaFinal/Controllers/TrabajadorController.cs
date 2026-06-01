using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using ControlAsistenciaFinal.Models;
using System.Collections.Generic;

namespace ControlAsistenciaFinal.Controllers
{
    public class TrabajadorController : Controller
    {

        [HttpGet]
                    public ActionResult MiJornada()
                    {
                        if (Session["UsuarioId"] == null)
                            return RedirectToAction("Login", "Account");

                        int usuarioId = Convert.ToInt32(Session["UsuarioId"]);

                        // ============================================
                        // FECHAS: Mes actual, pero hasta el DÍA ANTERIOR
                        // ============================================
                        DateTime inicioMes = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                        DateTime fechaHastaAnterior = DateTime.Now.AddDays(-1);  // Hasta ayer
                        DateTime fechaActual = DateTime.Now;

                        // Verificar rol del usuario
                        string queryRol = "SELECT Rol FROM Usuarios WHERE Id = @Id";
                        SqlParameter[] paramRol = { new SqlParameter("@Id", usuarioId) };
                        DataTable dtRol = DatabaseHelper.ExecuteQuery(queryRol, paramRol);
                        string rol = dtRol.Rows.Count > 0 ? dtRol.Rows[0]["Rol"].ToString() : "";

                        // Verificar el tipo de usuario (Facilitador o Planilla)
                        string queryConcepto = @"SELECT cp.Tipo, cp.HorasMensuales, cp.DiasBase, cp.HorasDiarias
                                         FROM Usuarios u 
                                         LEFT JOIN ConfiguracionPagos cp ON u.ConceptoPagoId = cp.Id 
                                         WHERE u.Id = @Id";
                        DataTable dtConcepto = DatabaseHelper.ExecuteQuery(queryConcepto, paramRol);

                        bool esFacilitador = dtConcepto.Rows.Count > 0 && dtConcepto.Rows[0]["Tipo"].ToString() == "Facilitador";
                        bool esPlanilla = dtConcepto.Rows.Count > 0 && dtConcepto.Rows[0]["Tipo"].ToString() == "Planilla";

                        // ============================================
                        // CALCULAR HORAS TRABAJADAS EN EL MES (HASTA EL DÍA ANTERIOR)
                        // ============================================
                        string queryHoras = @"
                    SELECT 
                        ISNULL(SUM(
                            CASE 
                                WHEN r.TipoRegistro = 'Entrada' AND r2.TipoRegistro = 'Salida' 
                                THEN DATEDIFF(MINUTE, r.FechaHora, r2.FechaHora) / 60.0
                                ELSE 0 
                            END
                        ), 0) AS TotalHoras,
                        COUNT(DISTINCT CAST(r.FechaHora AS DATE)) AS DiasTrabajados
                    FROM Usuarios u
                    LEFT JOIN RegistrosAsistencia r ON u.Id = r.UsuarioId 
                        AND r.TipoRegistro = 'Entrada'
                        AND CAST(r.FechaHora AS DATE) BETWEEN @FechaInicio AND @FechaFin
                    LEFT JOIN RegistrosAsistencia r2 ON u.Id = r2.UsuarioId 
                        AND r2.TipoRegistro = 'Salida'
                        AND CAST(r2.FechaHora AS DATE) = CAST(r.FechaHora AS DATE)
                    WHERE u.Id = @UsuarioId";

                        SqlParameter[] paramHoras = new SqlParameter[]
                        {
                    new SqlParameter("@UsuarioId", usuarioId),
                    new SqlParameter("@FechaInicio", inicioMes),
                    new SqlParameter("@FechaFin", fechaHastaAnterior)  // Hasta el día anterior
                        };

                        DataTable dtHoras = DatabaseHelper.ExecuteQuery(queryHoras, paramHoras);

                        decimal horasTrabajadas = 0;
                        int diasTrabajados = 0;

                        if (dtHoras.Rows.Count > 0)
                        {
                            horasTrabajadas = Convert.ToDecimal(dtHoras.Rows[0]["TotalHoras"]);
                            diasTrabajados = Convert.ToInt32(dtHoras.Rows[0]["DiasTrabajados"]);
                        }

                        ViewBag.HorasTrabajadas = Math.Round(horasTrabajadas, 2);
                        ViewBag.DiasTrabajados = diasTrabajados;

                        // ============================================
                        // CALCULAR HORAS OBJETIVO DEL MES (COMPLETO)
                        // ============================================
                        decimal horasObjetivo = 0;

                        if (esFacilitador)
                        {
                            // Facilitador: usar HorasMensuales de ConfiguracionPagos
                            decimal horasMensuales = dtConcepto.Rows.Count > 0 && dtConcepto.Rows[0]["HorasMensuales"] != DBNull.Value
                                ? Convert.ToDecimal(dtConcepto.Rows[0]["HorasMensuales"]) : 104;

                            horasObjetivo = horasMensuales;
                            ViewBag.EsFacilitador = true;
                            ViewBag.EsPlanilla = false;
                        }
                        else if (esPlanilla)
                        {
                            // Planilla: usar HorasMensuales de ConfiguracionPagos
                            decimal horasMensuales = dtConcepto.Rows.Count > 0 && dtConcepto.Rows[0]["HorasMensuales"] != DBNull.Value
                                ? Convert.ToDecimal(dtConcepto.Rows[0]["HorasMensuales"]) : 192;

                            horasObjetivo = horasMensuales;
                            ViewBag.EsPlanilla = true;
                            ViewBag.EsFacilitador = false;
                            ViewBag.DiasTrabajadosPlanilla = diasTrabajados;
                        }
                        else
                        {
                            // Otros roles
                            horasObjetivo = 160;
                            ViewBag.EsFacilitador = false;
                            ViewBag.EsPlanilla = false;
                        }

                        ViewBag.HorasObjetivo = Math.Round(horasObjetivo, 2);

                        // Calcular porcentaje de avance (basado en el objetivo del mes completo)
                        int porcentaje = horasObjetivo > 0 ? (int)((horasTrabajadas / horasObjetivo) * 100) : 0;
                        porcentaje = Math.Min(porcentaje, 100);
                        ViewBag.PorcentajeAvance = porcentaje;

                        // ============================================
                        // VERIFICAR PERMISOS EXCEPCIONALES
                        // ============================================
                        string queryPermiso = "SELECT PermisoMarcacionExcepcional, TipoPermisoExcepcional FROM Usuarios WHERE Id = @Id";
                        DataTable dtPermiso = DatabaseHelper.ExecuteQuery(queryPermiso, paramRol);

                        if (dtPermiso.Rows.Count > 0)
                        {
                            ViewBag.TienePermisoExcepcional = Convert.ToBoolean(dtPermiso.Rows[0]["PermisoMarcacionExcepcional"]);
                            ViewBag.TipoPermisoExcepcional = dtPermiso.Rows[0]["TipoPermisoExcepcional"].ToString();
                        }
                        else
                        {
                            ViewBag.TienePermisoExcepcional = false;
                            ViewBag.TipoPermisoExcepcional = "Todos";
                        }

                        ViewBag.Nombre = Session["Nombre"];
                        return View();
                    }
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

        // ============================================
        // RESUMEN PARA FACILITADORES
        // ============================================

        private void CargarResumenHorasFacilitador(int usuarioId)
        {
            try
            {
                // Obtener la fecha de inicio del usuario (del ciclo actual)
                string queryFechaInicio = @"
            SELECT ISNULL(ct.FechaInicio, u.FechaInicio) AS FechaInicio
            FROM Usuarios u
            LEFT JOIN CiclosTrabajo ct ON u.CicloActualId = ct.Id
            WHERE u.Id = @UsuarioId";

                SqlParameter[] paramFecha = { new SqlParameter("@UsuarioId", usuarioId) };
                object fechaInicioObj = DatabaseHelper.ExecuteScalar(queryFechaInicio, paramFecha);

                DateTime fechaInicio = fechaInicioObj != DBNull.Value ? Convert.ToDateTime(fechaInicioObj) : new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

                // Consulta para calcular horas trabajadas desde la fecha de inicio
                string queryResumen = @"
            SELECT 
                COUNT(DISTINCT Fecha) AS DiasTrabajados,
                ISNULL(SUM(HorasDia), 0) AS HorasTrabajadas
            FROM (
                SELECT 
                    CAST(FechaHora AS DATE) AS Fecha,
                    (
                        DATEDIFF(MINUTE, 
                            MIN(CASE WHEN TipoRegistro = 'Entrada' THEN FechaHora END),
                            MAX(CASE WHEN TipoRegistro = 'Salida' THEN FechaHora END)
                        ) - 
                        ISNULL(
                            DATEDIFF(MINUTE,
                                MAX(CASE WHEN TipoRegistro = 'Almuerzo_Salida' THEN FechaHora END),
                                MAX(CASE WHEN TipoRegistro = 'Almuerzo_Retorno' THEN FechaHora END)
                            ), 0)
                    ) / 60.0 AS HorasDia
                FROM RegistrosAsistencia
                WHERE UsuarioId = @UsuarioId 
                  AND CAST(FechaHora AS DATE) >= @FechaInicio
                GROUP BY CAST(FechaHora AS DATE)
            ) AS HorasPorDia
            WHERE HorasDia > 0";

                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId),
            new SqlParameter("@FechaInicio", fechaInicio)
                };

                DataTable dt = DatabaseHelper.ExecuteQuery(queryResumen, parameters);

                if (dt.Rows.Count > 0 && dt.Rows[0]["DiasTrabajados"] != DBNull.Value)
                {
                    ViewBag.DiasTrabajados = Convert.ToInt32(dt.Rows[0]["DiasTrabajados"]);
                    ViewBag.HorasTrabajadas = Math.Round(Convert.ToDecimal(dt.Rows[0]["HorasTrabajadas"]), 2);
                }
                else
                {
                    ViewBag.DiasTrabajados = 0;
                    ViewBag.HorasTrabajadas = 0;
                }

                // ============================================
                // CORREGIDO: Obtener horas objetivo desde ConfiguracionPagos
                // Para Facilitadores, usar HorasMensuales (104)
                // ============================================
                string queryObjetivo = @"
            SELECT 
                CASE 
                    WHEN cp.Tipo = 'Facilitador' THEN cp.HorasMensuales
                    WHEN cp.Tipo = 'Planilla' THEN cp.HorasMensuales
                    ELSE 160
                END AS HorasObjetivo
            FROM Usuarios u
            INNER JOIN ConfiguracionPagos cp ON u.ConceptoPagoId = cp.Id
            WHERE u.Id = @UsuarioId";

                object objetivo = DatabaseHelper.ExecuteScalar(queryObjetivo, new SqlParameter[] { new SqlParameter("@UsuarioId", usuarioId) });
                ViewBag.HorasObjetivo = objetivo != DBNull.Value ? Convert.ToDecimal(objetivo) : 104;

                // Depuración (opcional - para verificar en la ventana de salida)
                System.Diagnostics.Debug.WriteLine($"Usuario {usuarioId} - HorasObjetivo: {ViewBag.HorasObjetivo}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en CargarResumenHorasFacilitador: " + ex.Message);
                ViewBag.DiasTrabajados = 0;
                ViewBag.HorasTrabajadas = 0;
                ViewBag.HorasObjetivo = 104;
            }
        }
        // ============================================
        // RESUMEN PARA PLANILLA (SOLO DÍAS TRABAJADOS)
        // ============================================


        private void CargarDiasTrabajadosPlanilla(int usuarioId)
        {
            try
            {
                string queryDias = @"
            SELECT COUNT(DISTINCT CAST(FechaHora AS DATE)) AS DiasTrabajados
            FROM RegistrosAsistencia
            WHERE UsuarioId = @UsuarioId 
              AND MONTH(FechaHora) = MONTH(GETDATE()) 
              AND YEAR(FechaHora) = YEAR(GETDATE())";

                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId)
                };

                DataTable dt = DatabaseHelper.ExecuteQuery(queryDias, parameters);

                if (dt.Rows.Count > 0 && dt.Rows[0]["DiasTrabajados"] != DBNull.Value)
                {
                    ViewBag.DiasTrabajadosPlanilla = Convert.ToInt32(dt.Rows[0]["DiasTrabajados"]);
                }
                else
                {
                    ViewBag.DiasTrabajadosPlanilla = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en CargarDiasTrabajadosPlanilla: " + ex.Message);
                ViewBag.DiasTrabajadosPlanilla = 0;
            }
        }


        // ============================================
        // REGISTRAR MARCACIÓN NORMAL
        // ============================================
        [HttpPost]
        public JsonResult Registrar(string tipo, string comentario)
        {
            try
            {
                if (Session["UsuarioId"] == null)
                    return Json(new { exito = false, mensaje = "Sesión expirada" });

                int usuarioId = Convert.ToInt32(Session["UsuarioId"]);

                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId),
            new SqlParameter("@TipoRegistro", tipo),
            new SqlParameter("@Comentario", string.IsNullOrEmpty(comentario) ? (object)DBNull.Value : comentario)
                };

                DataTable result = DatabaseHelper.ExecuteStoredProcedure("sp_RegistrarAsistencia", parameters);

                string mensaje = "";
                bool exito = false;
                bool cicloReiniciado = false;
                decimal horasCompletadas = 0;

                if (result.Rows.Count > 0)
                {
                    int resultado = Convert.ToInt32(result.Rows[0]["Resultado"]);
                    mensaje = result.Rows[0]["Mensaje"].ToString();
                    exito = (resultado == 1);

                    // SI ES UNA SALIDA, VERIFICAR SI COMPLETÓ EL CICLO
                    if (exito && tipo == "Salida")
                    {
                        try
                        {
                            DataTable cicloResult = DatabaseHelper.ExecuteStoredProcedure("sp_VerificarYReiniciarCicloPorUsuario",
                                new SqlParameter[] { new SqlParameter("@UsuarioId", usuarioId) });

                            if (cicloResult.Rows.Count > 0 && Convert.ToInt32(cicloResult.Rows[0]["CicloReiniciado"]) == 1)
                            {
                                cicloReiniciado = true;
                                horasCompletadas = Convert.ToDecimal(cicloResult.Rows[0]["HorasCompletadas"]);
                                decimal horasObjetivo = Convert.ToDecimal(cicloResult.Rows[0]["HorasObjetivo"]);
                                mensaje += $" ¡FELICIDADES! Has completado {horasCompletadas} horas de {horasObjetivo}. Tu contador se ha reiniciado para el nuevo ciclo.";

                                // Limpiar caché de sesión para que el resumen se actualice
                                Session["HorasActualizadas"] = DateTime.Now;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("Error al verificar ciclo: " + ex.Message);
                        }
                    }
                }

                return Json(new
                {
                    exito = exito,
                    mensaje = mensaje,
                    cicloReiniciado = cicloReiniciado,
                    horasCompletadas = horasCompletadas
                });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ============================================
        // OBTENER REGISTROS DE HOY
        // ============================================
        [HttpPost]
        public JsonResult ObtenerRegistrosHoy()
        {
            try
            {
                int usuarioId = Convert.ToInt32(Session["UsuarioId"]);

                // Cambiar a día ANTERIOR
                DateTime fechaAnterior = DateTime.Now.AddDays(-1);

                string query = @"SELECT Id, TipoRegistro, FechaHora, Comentario 
                         FROM RegistrosAsistencia 
                         WHERE UsuarioId = @UsuarioId 
                           AND CAST(FechaHora AS DATE) = @Fecha
                         ORDER BY FechaHora ASC";

                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId),
            new SqlParameter("@Fecha", fechaAnterior.Date)
                };

                DataTable dt = DatabaseHelper.ExecuteQuery(query, parameters);
                List<object> registros = new List<object>();

                foreach (DataRow row in dt.Rows)
                {
                    DateTime fechaHora = Convert.ToDateTime(row["FechaHora"]);

                    registros.Add(new
                    {
                        Id = row["Id"],
                        TipoRegistro = row["TipoRegistro"].ToString(),
                        FechaHora = fechaHora,
                        Hora = fechaHora.ToString("HH:mm"),
                        Comentario = row["Comentario"]?.ToString()
                    });
                }

                return Json(registros);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }
        // ============================================
        // SOLICITAR MARCACIÓN EXCEPCIONAL
        // ============================================
        [HttpPost]
        public JsonResult SolicitarMarcacionExcepcional(string tipo, DateTime fechaHora, string justificacion)
        {
            try
            {
                int usuarioId = Convert.ToInt32(Session["UsuarioId"]);

                // Validar que la fecha sea hoy
                if (fechaHora.Date != DateTime.Now.Date)
                {
                    return Json(new { success = false, message = "Solo puede solicitar marcación para el día de hoy" });
                }

                // ============================================
                // VALIDACIÓN DE HORA FUTURA ELIMINADA
                // Puede solicitar CUALQUIER hora del día actual
                // ============================================

                // Verificar si el usuario tiene permiso
                string checkQuery = @"SELECT PermisoMarcacionExcepcional, TipoPermisoExcepcional 
                              FROM Usuarios WHERE Id = @UsuarioId";
                SqlParameter[] checkParams = new SqlParameter[] { new SqlParameter("@UsuarioId", usuarioId) };
                DataTable dt = DatabaseHelper.ExecuteQuery(checkQuery, checkParams);

                if (dt.Rows.Count == 0)
                    return Json(new { success = false, message = "Usuario no encontrado" });

                bool tienePermiso = Convert.ToBoolean(dt.Rows[0]["PermisoMarcacionExcepcional"]);
                string tipoPermiso = dt.Rows[0]["TipoPermisoExcepcional"].ToString();

                if (!tienePermiso)
                    return Json(new { success = false, message = "No tiene permiso para marcación excepcional" });

                // Verificar tipo de permiso
                if (tipoPermiso != "Todos" && tipoPermiso != tipo)
                    return Json(new { success = false, message = $"Solo tiene permiso para: {tipoPermiso}" });

                // Verificar si ya existe una solicitud pendiente
                string checkSolicitud = @"SELECT COUNT(*) FROM SolicitudesExcepcionales 
                                  WHERE UsuarioId = @UsuarioId 
                                    AND TipoRegistro = @Tipo 
                                    AND CAST(FechaHoraSolicitada AS DATE) = @Fecha
                                    AND Estado = 'Pendiente'";
                SqlParameter[] checkSolicitudParams = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId),
            new SqlParameter("@Tipo", tipo),
            new SqlParameter("@Fecha", fechaHora.Date)
                };
                int solicitudesPendientes = Convert.ToInt32(DatabaseHelper.ExecuteScalar(checkSolicitud, checkSolicitudParams));

                if (solicitudesPendientes > 0)
                    return Json(new { success = false, message = "Ya tiene una solicitud pendiente para este tipo hoy" });

                // Verificar si ya existe un registro de asistencia
                string checkRegistro = @"SELECT COUNT(*) FROM RegistrosAsistencia 
                                 WHERE UsuarioId = @UsuarioId 
                                   AND TipoRegistro = @Tipo 
                                   AND CAST(FechaHora AS DATE) = @Fecha";
                int existeRegistro = Convert.ToInt32(DatabaseHelper.ExecuteScalar(checkRegistro, checkSolicitudParams));

                if (existeRegistro > 0)
                    return Json(new { success = false, message = "Ya tiene un registro para este tipo hoy" });

                // Insertar solicitud
                string query = @"INSERT INTO SolicitudesExcepcionales (UsuarioId, TipoRegistro, FechaHoraSolicitada, Justificacion, Estado, FechaSolicitud)
                         VALUES (@UsuarioId, @Tipo, @FechaHora, @Justificacion, 'Pendiente', GETDATE());
                         SELECT SCOPE_IDENTITY();";

                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId),
            new SqlParameter("@Tipo", tipo),
            new SqlParameter("@FechaHora", fechaHora),
            new SqlParameter("@Justificacion", justificacion)
                };

                object result = DatabaseHelper.ExecuteScalar(query, parameters);
                int solicitudId = result != null ? Convert.ToInt32(result) : 0;

                // Crear notificación para el administrador
                string nombreUsuario = Session["Nombre"].ToString();
                string notificacionQuery = @"INSERT INTO NotificacionesAdmin (Titulo, Mensaje, Tipo, UsuarioId, SolicitudId)
                                     VALUES ('Nueva solicitud de marcación excepcional', 
                                             @Mensaje, 
                                             'SolicitudExcepcional', 
                                             @UsuarioId, 
                                             @SolicitudId)";

                string mensajeNotificacion = $"El trabajador {nombreUsuario} ha solicitado registrar {tipo} para hoy a las {fechaHora:HH:mm}. Justificación: {justificacion}";
                SqlParameter[] notifParams = new SqlParameter[]
                {
            new SqlParameter("@Mensaje", mensajeNotificacion),
            new SqlParameter("@UsuarioId", usuarioId),
            new SqlParameter("@SolicitudId", solicitudId)
                };

                DatabaseHelper.ExecuteNonQuery(notificacionQuery, notifParams);

                return Json(new { success = true, message = "Solicitud enviada al administrador" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        // ============================================
        // OBTENER PERMISO EXCEPCIONAL
        // ============================================
        [HttpGet]
        public JsonResult ObtenerPermisoExcepcional()
        {
            try
            {
                int usuarioId = Convert.ToInt32(Session["UsuarioId"]);

                string query = @"SELECT PermisoMarcacionExcepcional, TipoPermisoExcepcional 
                                 FROM Usuarios WHERE Id = @UsuarioId";
                SqlParameter[] parameters = new SqlParameter[] { new SqlParameter("@UsuarioId", usuarioId) };
                DataTable dt = DatabaseHelper.ExecuteQuery(query, parameters);

                if (dt.Rows.Count > 0)
                {
                    return Json(new
                    {
                        success = true,
                        tienePermiso = Convert.ToBoolean(dt.Rows[0]["PermisoMarcacionExcepcional"]),
                        tipoPermiso = dt.Rows[0]["TipoPermisoExcepcional"].ToString()
                    }, JsonRequestBehavior.AllowGet);
                }

                return Json(new { success = false, tienePermiso = false }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
    }
}