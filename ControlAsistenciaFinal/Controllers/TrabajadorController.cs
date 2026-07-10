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
        public ActionResult MiJornadaMejorada()
        {
            if (Session["UsuarioId"] == null)
                return RedirectToAction("Login", "Account");

            int usuarioId = Convert.ToInt32(Session["UsuarioId"]);

            // ============================================
            // OBTENER DATOS DEL CICLO ACTUAL USANDO EL SP
            // ============================================
            DataTable dtCiclo = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerCicloActualUsuario",
                new SqlParameter[] { new SqlParameter("@UsuarioId", usuarioId) });

            DateTime fechaInicioCiclo = DateTime.Now;
            decimal horasObjetivo = 104;
            decimal horasAcumuladas = 0;
            decimal horasRestantes = 0;
            decimal horasTransferidas = 0;
            int porcentajeAvance = 0;
            int ciclosCompletados = 0;

            if (dtCiclo.Rows.Count > 0)
            {
                fechaInicioCiclo = Convert.ToDateTime(dtCiclo.Rows[0]["FechaInicioCiclo"]);
                horasObjetivo = Convert.ToDecimal(dtCiclo.Rows[0]["HorasObjetivo"]);
                horasAcumuladas = Convert.ToDecimal(dtCiclo.Rows[0]["HorasAcumuladas"]);
                horasRestantes = Convert.ToDecimal(dtCiclo.Rows[0]["HorasRestantes"]);
                horasTransferidas = Convert.ToDecimal(dtCiclo.Rows[0]["HorasTransferidas"]);
                porcentajeAvance = Convert.ToInt32(dtCiclo.Rows[0]["PorcentajeAvance"]);
                ciclosCompletados = Convert.ToInt32(dtCiclo.Rows[0]["CiclosCompletados"]);
            }

            // ============================================
            // VERIFICAR TIPO DE USUARIO
            // ============================================
            SqlParameter[] paramUsuario = { new SqlParameter("@Id", usuarioId) };
            string queryConcepto = @"SELECT cp.Tipo, cp.HorasMensuales, cp.DiasBase, cp.HorasDiarias, cp.HorasSabado
                     FROM Usuarios u 
                     LEFT JOIN ConfiguracionPagos cp ON u.ConceptoPagoId = cp.Id 
                     WHERE u.Id = @Id";
            DataTable dtConcepto = DatabaseHelper.ExecuteQuery(queryConcepto, paramUsuario);

            bool esFacilitador = dtConcepto.Rows.Count > 0 && dtConcepto.Rows[0]["Tipo"].ToString() == "Facilitador";
            bool esPlanilla = dtConcepto.Rows.Count > 0 && dtConcepto.Rows[0]["Tipo"].ToString() == "Planilla";

       
            // ============================================
            // CALCULAR DÍAS TRABAJADOS - SEGÚN TIPO DE USUARIO
            // ============================================
            int diasTrabajados = 0;

            if (esPlanilla)
            {
                // Para Planilla: contar días del mes actual (lunes a sábado, excluyendo domingos)
                DateTime inicioMes = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                DateTime finMes = inicioMes.AddMonths(1).AddDays(-1);

              string queryDiasPlanilla = @"
            SELECT COUNT(*) AS DiasTrabajados
            FROM (
                SELECT CAST(FechaHora AS DATE) AS Fecha
                FROM RegistrosAsistencia
                WHERE UsuarioId = @UsuarioId
                    AND CAST(FechaHora AS DATE) >= @FechaInicio
                    AND CAST(FechaHora AS DATE) <= @FechaFin
                    AND DATEPART(dw, CAST(FechaHora AS DATE)) NOT IN (1) -- Excluye domingos
                GROUP BY CAST(FechaHora AS DATE)
                HAVING 
                    SUM(CASE WHEN TipoRegistro = 'Entrada' THEN 1 ELSE 0 END) > 0
                    AND SUM(CASE WHEN TipoRegistro = 'Salida' THEN 1 ELSE 0 END) > 0
                    -- Asegurar que la salida tiene una hora válida (no NULL)
                    AND MAX(CASE WHEN TipoRegistro = 'Salida' THEN CAST(FechaHora AS TIME) END) IS NOT NULL
            ) AS DiasConEntradaYSalida";

                SqlParameter[] paramDiasPlanilla = new SqlParameter[]
                {
        new SqlParameter("@UsuarioId", usuarioId),
        new SqlParameter("@FechaInicio", inicioMes),
        new SqlParameter("@FechaFin", finMes)
                };

                DataTable dtDiasPlanilla = DatabaseHelper.ExecuteQuery(queryDiasPlanilla, paramDiasPlanilla);
                diasTrabajados = dtDiasPlanilla.Rows.Count > 0 ? Convert.ToInt32(dtDiasPlanilla.Rows[0]["DiasTrabajados"]) : 0;
            }
            else
            {
            // Para Facilitador y otros: contar días desde el inicio del ciclo
                        string queryDias = @"
            SET DATEFIRST 1;
            SELECT COUNT(DISTINCT CAST(FechaHora AS DATE)) AS DiasTrabajados
            FROM RegistrosAsistencia
            WHERE UsuarioId = @UsuarioId
                AND CAST(FechaHora AS DATE) >= @FechaInicioCiclo
                AND DATEPART(dw, CAST(FechaHora AS DATE)) BETWEEN 1 AND 6
                AND TipoRegistro = 'Entrada'";

                        SqlParameter[] paramDias = new SqlParameter[]
                        {
                new SqlParameter("@UsuarioId", usuarioId),
                new SqlParameter("@FechaInicioCiclo", fechaInicioCiclo)
                        };

                DataTable dtDias = DatabaseHelper.ExecuteQuery(queryDias, paramDias);
                diasTrabajados = dtDias.Rows.Count > 0 ? Convert.ToInt32(dtDias.Rows[0]["DiasTrabajados"]) : 0;
            }

            // ============================================
            // ASIGNAR VALORES AL ViewBag
            // ============================================
            ViewBag.HorasTrabajadas = horasAcumuladas;
            ViewBag.HorasObjetivo = horasObjetivo;
            ViewBag.HorasRestantes = horasRestantes;
            ViewBag.HorasTransferidas = horasTransferidas;
            ViewBag.DiasTrabajados = diasTrabajados;
            ViewBag.PorcentajeAvance = porcentajeAvance;
            ViewBag.FechaInicioCiclo = fechaInicioCiclo;
            ViewBag.CiclosCompletados = ciclosCompletados;

            // Texto del período actual
            if (ciclosCompletados > 0 && horasTransferidas > 0)
            {
                ViewBag.PeriodoTexto = $"Ciclo #{ciclosCompletados + 1}: {fechaInicioCiclo:dd/MM/yyyy} - hasta completar {horasObjetivo} horas (con {horasTransferidas} horas transferidas del ciclo anterior)";
            }
            else
            {
                ViewBag.PeriodoTexto = $"Ciclo #{ciclosCompletados + 1}: {fechaInicioCiclo:dd/MM/yyyy} - hasta completar {horasObjetivo} horas";
            }

            if (esFacilitador)
            {
                ViewBag.EsFacilitador = true;
                ViewBag.EsPlanilla = false;
            }
            else if (esPlanilla)
            {
                ViewBag.EsPlanilla = true;
                ViewBag.EsFacilitador = false;
                ViewBag.DiasTrabajadosPlanilla = diasTrabajados;
            }
            else
            {
                ViewBag.EsFacilitador = false;
                ViewBag.EsPlanilla = false;
            }

            // ============================================
            // VERIFICAR PERMISOS EXCEPCIONALES
            // ============================================
            string queryPermiso = "SELECT PermisoMarcacionExcepcional, TipoPermisoExcepcional FROM Usuarios WHERE Id = @Id";
            DataTable dtPermiso = DatabaseHelper.ExecuteQuery(queryPermiso, paramUsuario);

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

        // ============================================
        // MÉTODO AUXILIAR PARA CONTAR DÍAS TRABAJADOS (LUNES A SÁBADO)
        // ============================================
        private int ContarDiasTrabajados(int usuarioId, DateTime fechaInicio, DateTime fechaFin)
        {
            string query = @"
    SELECT COUNT(*) AS DiasTrabajados
    FROM (
        SELECT CAST(FechaHora AS DATE) AS Fecha
        FROM RegistrosAsistencia
        WHERE UsuarioId = @UsuarioId
            AND CAST(FechaHora AS DATE) >= @FechaInicio
            AND CAST(FechaHora AS DATE) <= @FechaFin
            AND DATEPART(dw, CAST(FechaHora AS DATE)) NOT IN (1) -- Excluye domingos (1 = Domingo)
        GROUP BY CAST(FechaHora AS DATE)
        HAVING 
            SUM(CASE WHEN TipoRegistro = 'Entrada' THEN 1 ELSE 0 END) > 0
            AND SUM(CASE WHEN TipoRegistro = 'Salida' THEN 1 ELSE 0 END) > 0
    ) AS DiasConEntradaYSalida";

            SqlParameter[] parameters = new SqlParameter[]
            {
        new SqlParameter("@UsuarioId", usuarioId),
        new SqlParameter("@FechaInicio", fechaInicio),
        new SqlParameter("@FechaFin", fechaFin)
            };

            DataTable dt = DatabaseHelper.ExecuteQuery(query, parameters);
            return dt.Rows.Count > 0 ? Convert.ToInt32(dt.Rows[0]["DiasTrabajados"]) : 0;
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
                    HAVING 
                        SUM(CASE WHEN TipoRegistro = 'Entrada' THEN 1 ELSE 0 END) > 0
                        AND SUM(CASE WHEN TipoRegistro = 'Salida' THEN 1 ELSE 0 END) > 0
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
                    SELECT COUNT(*) AS DiasTrabajados
                    FROM (
                        SELECT CAST(FechaHora AS DATE) AS Fecha
                        FROM RegistrosAsistencia
                        WHERE UsuarioId = @UsuarioId 
                          AND MONTH(FechaHora) = MONTH(GETDATE()) 
                          AND YEAR(FechaHora) = YEAR(GETDATE())
                        GROUP BY CAST(FechaHora AS DATE)
                        HAVING 
                            SUM(CASE WHEN TipoRegistro = 'Entrada' THEN 1 ELSE 0 END) > 0
                            AND SUM(CASE WHEN TipoRegistro = 'Salida' THEN 1 ELSE 0 END) > 0
                    ) AS DiasConEntradaYSalida";

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


        [HttpGet]
        public JsonResult ObtenerAlertasHorasJson(bool soloNoLeidas = true)
        {
            try
            {
                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@SoloNoLeidas", soloNoLeidas)
                };

                DataTable resultado = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerAlertasHoras", parameters);

                var alertas = new List<object>();

                if (resultado != null && resultado.Rows.Count > 0)
                {
                    foreach (DataRow row in resultado.Rows)
                    {
                        var alerta = new
                        {
                            Id = Convert.ToInt32(row["Id"]),
                            UsuarioId = Convert.ToInt32(row["UsuarioId"]),
                            NombreCompleto = row["NombreCompleto"]?.ToString() ?? "",
                            Email = row["Email"]?.ToString() ?? "",
                            RolPago = row["RolPago"]?.ToString() ?? "",
                            Mes = Convert.ToInt32(row["Mes"]),
                            Anio = Convert.ToInt32(row["Anio"]),
                            HorasAlcanzadas = Convert.ToDecimal(row["HorasAlcanzadas"]),
                            HorasObjetivo = Convert.ToDecimal(row["HorasObjetivo"]),
                            FechaAlerta = Convert.ToDateTime(row["FechaAlerta"]).ToString("dd/MM/yyyy HH:mm"),
                            Leido = Convert.ToBoolean(row["Leido"]),
                            Estado = row["Estado"]?.ToString() ?? "",
                            TipoAlerta = row["TipoAlerta"]?.ToString() ?? ""
                        };
                        alertas.Add(alerta);
                    }
                }

                return Json(new { success = true, data = alertas, count = alertas.Count }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en ObtenerAlertasHorasJson: " + ex.Message);
                return Json(new { success = false, message = ex.Message, data = new List<object>(), count = 0 }, JsonRequestBehavior.AllowGet);
            }
        }


        [HttpGet]
        public JsonResult ObtenerConteoAlertasNoLeidas()
        {
            try
            {
                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@SoloNoLeidas", true)
                };

                DataTable resultado = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerAlertasHoras", parameters);
                int count = resultado?.Rows.Count ?? 0;

                return Json(new { success = true, count = count }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, count = 0 }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult MarcarAlertaLeida(int alertaId)
        {
            try
            {
                string query = "UPDATE AlertasHoras SET Leido = 1 WHERE Id = @AlertaId";
                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@AlertaId", alertaId)
                };

                int filasAfectadas = DatabaseHelper.ExecuteNonQuery(query, parameters);

                if (filasAfectadas > 0)
                {
                    return Json(new { success = true });
                }

                return Json(new { success = false, message = "Alerta no encontrada" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost]
        public JsonResult MarcarTodasAlertasLeidas()
        {
            try
            {
                string query = "UPDATE AlertasHoras SET Leido = 1 WHERE Leido = 0";
                int filasAfectadas = DatabaseHelper.ExecuteNonQuery(query, null);

                return Json(new { success = true, count = filasAfectadas });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost]
        public JsonResult Registrar(string tipo, string comentario)
        {
            try
            {
                if (Session["UsuarioId"] == null)
                    return Json(new { exito = false, mensaje = "Sesión expirada" });

                int usuarioId = Convert.ToInt32(Session["UsuarioId"]);
                DateTime ahora = DateTime.Now;

                // Primero, actualizar el ciclo del usuario
                DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerCicloActualUsuario",
                    new SqlParameter[] { new SqlParameter("@UsuarioId", usuarioId) });

                // Registrar la asistencia
                string query = @"INSERT INTO RegistrosAsistencia (UsuarioId, TipoRegistro, FechaHora, Comentario) 
                         VALUES (@UsuarioId, @Tipo, @FechaHora, @Comentario)";

                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId),
            new SqlParameter("@Tipo", tipo),
            new SqlParameter("@FechaHora", ahora),
            new SqlParameter("@Comentario", comentario ?? (object)DBNull.Value)
                };

                DatabaseHelper.ExecuteNonQuery(query, parameters);

                // ============================================
                // SI ES SALIDA, ELIMINAR LA ALERTA DE SALIDA PENDIENTE
                // ============================================
                // En el método Registrar, cuando tipo == "Salida"
                if (tipo == "Salida")
                {
                    // Marcar la alerta como aceptada automáticamente
                    string updateAlerta = @"
        UPDATE AlertasSalidaPendiente 
        SET Estado = 'Aceptada', FechaAlerta = GETDATE() 
        WHERE UsuarioId = @UsuarioId AND Fecha = @Fecha";

                    SqlParameter[] paramUpdate = new SqlParameter[]
                    {
        new SqlParameter("@UsuarioId", usuarioId),
        new SqlParameter("@Fecha", ahora.Date)
                    };

                    try
                    {
                        DatabaseHelper.ExecuteNonQuery(updateAlerta, paramUpdate);
                    }
                    catch { }

                    // También eliminar por si existe con otro estado
                    string deleteAlerta = @"
        DELETE FROM AlertasSalidaPendiente 
        WHERE UsuarioId = @UsuarioId AND Fecha = @Fecha AND Estado = 'Pendiente'";

                    try
                    {
                        DatabaseHelper.ExecuteNonQuery(deleteAlerta, paramUpdate);
                    }
                    catch { }
                }

                return Json(new { exito = true, mensaje = "Registro exitoso" });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = ex.Message });
            }
        }

        // ============================================
        // OBTENER REGISTROS DE HOY
        // ============================================
        // ============================================
        // OBTENER REGISTROS DE HOY - OPTIMIZADO
        // ============================================
        [HttpPost]
        public JsonResult ObtenerRegistrosHoy()
        {
            try
            {
                if (Session["UsuarioId"] == null)
                    return Json(new { error = "Sesión expirada" });

                int usuarioId = Convert.ToInt32(Session["UsuarioId"]);
                DateTime fecha = DateTime.Now.Date;

                // Consulta optimizada sin CONVERT en WHERE
                string query = @"
        SELECT Id, TipoRegistro, 
               CONVERT(TIME, FechaHora) AS Hora,
               CONVERT(VARCHAR(5), FechaHora, 108) AS HoraStr,
               Comentario
        FROM RegistrosAsistencia 
        WHERE UsuarioId = @UsuarioId 
          AND FechaHora >= @FechaInicio 
          AND FechaHora < @FechaFin
        ORDER BY 
            CASE TipoRegistro
                WHEN 'Entrada' THEN 1
                WHEN 'Almuerzo_Salida' THEN 2
                WHEN 'Almuerzo_Retorno' THEN 3
                WHEN 'Salida' THEN 4
                ELSE 5
            END,
            FechaHora ASC";

                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId),
            new SqlParameter("@FechaInicio", fecha),
            new SqlParameter("@FechaFin", fecha.AddDays(1))
                };

                DataTable dt = DatabaseHelper.ExecuteQuery(query, parameters);
                List<object> registros = new List<object>();

                foreach (DataRow row in dt.Rows)
                {
                    registros.Add(new
                    {
                        Id = row["Id"],
                        TipoRegistro = row["TipoRegistro"].ToString(),
                        Hora = row["HoraStr"].ToString(),
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


        [HttpPost]
        public JsonResult ObtenerTardanzasMes()
        {
            try
            {
                if (Session["UsuarioId"] == null)
                    return Json(new { success = false, message = "Sesión expirada" });

                int usuarioId = Convert.ToInt32(Session["UsuarioId"]);

                // Ejecutar el stored procedure
                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId)
                };

                DataTable resultado = DatabaseHelper.ExecuteStoredProcedure("sp_ObtenerTardanzasMes", parameters);

                var tardanzas = new List<object>();
                int totalTardanzas = 0;

                if (resultado != null && resultado.Rows.Count > 0)
                {
                    foreach (DataRow row in resultado.Rows)
                    {
                        int minutosTardanza = Convert.ToInt32(row["MinutosTardanza"]);
                        if (minutosTardanza > 0)
                        {
                            totalTardanzas++;

                            tardanzas.Add(new
                            {
                                Fecha = row["Fecha"].ToString(),
                                HoraIngreso = row["HoraIngreso"].ToString(),  // <-- HORA DE INGRESO
                                HoraLimite = row["HoraLimite"].ToString(),    // <-- HORA LÍMITE
                                MinutosTardanza = minutosTardanza,
                                TipoJornada = row["TipoJornada"].ToString(),
                                TurnoDescripcion = row["TurnoDescripcion"].ToString(),
                                NivelTardanza = row["NivelTardanza"].ToString()
                            });
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    data = tardanzas,
                    total = totalTardanzas
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en ObtenerTardanzasMes: " + ex.Message);
                return Json(new { success = false, message = ex.Message });
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
        // OBTENER ALERTA DE SALIDA PENDIENTE - CON DEPURACIÓN
        // ============================================
        [HttpGet]
        public JsonResult ObtenerAlertaSalidaPendiente()
        {
            try
            {
                if (Session["UsuarioId"] == null)
                    return Json(new { success = false, message = "Sesión expirada" }, JsonRequestBehavior.AllowGet);

                int usuarioId = Convert.ToInt32(Session["UsuarioId"]);

                // ============================================
                // BUSCAR CUALQUIER FECHA DONDE FALTE SALIDA
                // ============================================
                string query = @"
        WITH FechasConEntrada AS (
            SELECT DISTINCT CAST(FechaHora AS DATE) AS Fecha
            FROM RegistrosAsistencia
            WHERE UsuarioId = @UsuarioId
              AND TipoRegistro = 'Entrada'
              AND DATEPART(dw, CAST(FechaHora AS DATE)) NOT IN (1) -- Excluye domingos
        ),
        FechasSinSalida AS (
            SELECT 
                f.Fecha,
                (SELECT TOP 1 FORMAT(r.FechaHora, 'HH:mm')
                 FROM RegistrosAsistencia r
                 WHERE r.UsuarioId = @UsuarioId
                   AND r.TipoRegistro = 'Entrada'
                   AND CAST(r.FechaHora AS DATE) = f.Fecha
                 ORDER BY r.FechaHora ASC) AS HoraEntrada,
                (SELECT COUNT(*)
                 FROM RegistrosAsistencia r
                 WHERE r.UsuarioId = @UsuarioId
                   AND r.TipoRegistro = 'Salida'
                   AND CAST(r.FechaHora AS DATE) = f.Fecha) AS TieneSalida
            FROM FechasConEntrada f
        )
        SELECT TOP 1 
            Fecha,
            HoraEntrada,
            TieneSalida,
            CASE DATEPART(dw, Fecha)
                WHEN 1 THEN 'Domingo'
                WHEN 2 THEN 'Lunes'
                WHEN 3 THEN 'Martes'
                WHEN 4 THEN 'Miércoles'
                WHEN 5 THEN 'Jueves'
                WHEN 6 THEN 'Viernes'
                WHEN 7 THEN 'Sábado'
            END AS DiaSemana
        FROM FechasSinSalida
        WHERE TieneSalida = 0
          AND NOT EXISTS (
              SELECT 1 
              FROM AlertasSalidaPendiente a 
              WHERE a.UsuarioId = @UsuarioId 
                AND a.Fecha = Fecha 
                AND a.Estado = 'Aceptada'
          )
          AND Fecha < CAST(GETDATE() AS DATE)
        ORDER BY Fecha DESC";

                SqlParameter[] parameters = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId)
                };

                DataTable dt = DatabaseHelper.ExecuteQuery(query, parameters);

                // ============================================
                // SI HAY FECHA PENDIENTE, MOSTRAR ALERTA
                // ============================================
                if (dt.Rows.Count > 0)
                {
                    DataRow row = dt.Rows[0];
                    DateTime fechaPendiente = Convert.ToDateTime(row["Fecha"]);
                    string horaEntrada = row["HoraEntrada"]?.ToString() ?? "--:--";
                    string diaSemana = row["DiaSemana"]?.ToString() ?? "";

                    int diasRetraso = (DateTime.Now.Date - fechaPendiente.Date).Days;

                    string fechaFormateada = fechaPendiente.ToString("dd 'de' MMMM 'de' yyyy",
                        new System.Globalization.CultureInfo("es-ES"));

                    string mensaje = $"Tiene salida pendiente del {diaSemana} {fechaFormateada}";

                    var resultado = new
                    {
                        success = true,
                        tieneAlerta = true,
                        fecha = fechaPendiente.ToString("dd/MM/yyyy"),
                        fechaFormateada = fechaFormateada,
                        diaSemana = diaSemana,
                        horaEntrada = horaEntrada,
                        diasRetraso = diasRetraso,
                        mensaje = mensaje
                    };

                     return Json(resultado, JsonRequestBehavior.AllowGet);
                }

                // ============================================
                // NO HAY FECHA PENDIENTE
                // ============================================
                System.Diagnostics.Debug.WriteLine("NO HAY ALERTA para usuario: " + usuarioId);
                return Json(new
                {
                    success = true,
                    tieneAlerta = false,
                    message = "No hay salidas pendientes"
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ERROR en ObtenerAlertaSalidaPendiente: " + ex.Message);
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    tieneAlerta = false
                }, JsonRequestBehavior.AllowGet);
            }
        }


        // ============================================
        // REGISTRAR ACEPTACIÓN DE ALERTA DE SALIDA - CORREGIDO
        // ============================================
        [HttpPost]
        public JsonResult RegistrarAceptacionAlertaSalida()
        {
            try
            {
                if (Session["UsuarioId"] == null)
                    return Json(new { success = false, message = "Sesión expirada" });

                int usuarioId = Convert.ToInt32(Session["UsuarioId"]);

                // Buscar la fecha pendiente más reciente
                string queryBuscar = @"
        WITH FechasConEntrada AS (
            SELECT DISTINCT CAST(FechaHora AS DATE) AS Fecha
            FROM RegistrosAsistencia
            WHERE UsuarioId = @UsuarioId
              AND TipoRegistro = 'Entrada'
              AND DATEPART(dw, CAST(FechaHora AS DATE)) NOT IN (1)
        )
        SELECT TOP 1 Fecha
        FROM FechasConEntrada f
        WHERE NOT EXISTS (
            SELECT 1 
            FROM RegistrosAsistencia r 
            WHERE r.UsuarioId = @UsuarioId 
              AND r.TipoRegistro = 'Salida' 
              AND CAST(r.FechaHora AS DATE) = f.Fecha
        )
        AND NOT EXISTS (
            SELECT 1 
            FROM AlertasSalidaPendiente a 
            WHERE a.UsuarioId = @UsuarioId 
              AND a.Fecha = f.Fecha 
              AND a.Estado = 'Aceptada'
        )
        AND f.Fecha < CAST(GETDATE() AS DATE)
        ORDER BY f.Fecha DESC";

                SqlParameter[] paramBuscar = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId)
                };

                object fechaObj = DatabaseHelper.ExecuteScalar(queryBuscar, paramBuscar);

                if (fechaObj == null || fechaObj == DBNull.Value)
                {
                    return Json(new { success = false, message = "No hay salidas pendientes" });
                }

                DateTime fechaPendiente = Convert.ToDateTime(fechaObj);

                // Verificar si ya existe un registro de alerta para esta fecha
                string queryCheck = @"
        SELECT COUNT(*) FROM AlertasSalidaPendiente 
        WHERE UsuarioId = @UsuarioId 
        AND Fecha = @Fecha";

                SqlParameter[] paramCheck = new SqlParameter[]
                {
            new SqlParameter("@UsuarioId", usuarioId),
            new SqlParameter("@Fecha", fechaPendiente.Date)
                };

                int existe = Convert.ToInt32(DatabaseHelper.ExecuteScalar(queryCheck, paramCheck));

                if (existe > 0)
                {
                    string queryUpdate = @"
            UPDATE AlertasSalidaPendiente 
            SET Estado = 'Aceptada', FechaAlerta = GETDATE() 
            WHERE UsuarioId = @UsuarioId 
            AND Fecha = @Fecha";

                    SqlParameter[] paramUpdate = new SqlParameter[]
                    {
                new SqlParameter("@UsuarioId", usuarioId),
                new SqlParameter("@Fecha", fechaPendiente.Date)
                    };

                    DatabaseHelper.ExecuteNonQuery(queryUpdate, paramUpdate);
                }
                else
                {
                    string queryInsert = @"
            INSERT INTO AlertasSalidaPendiente (UsuarioId, Fecha, FechaAlerta, Estado)
            VALUES (@UsuarioId, @Fecha, GETDATE(), 'Aceptada')";

                    SqlParameter[] paramInsert = new SqlParameter[]
                    {
                new SqlParameter("@UsuarioId", usuarioId),
                new SqlParameter("@Fecha", fechaPendiente.Date)
                    };

                    DatabaseHelper.ExecuteNonQuery(queryInsert, paramInsert);
                }

                return Json(new
                {
                    success = true,
                    message = "Alerta aceptada correctamente",
                    fecha = fechaPendiente.ToString("dd/MM/yyyy")
                });
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