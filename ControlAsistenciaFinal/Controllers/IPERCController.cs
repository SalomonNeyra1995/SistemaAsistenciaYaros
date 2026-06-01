using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using ControlAsistenciaFinal.Models;

namespace ControlAsistenciaFinal.Controllers
{
    public class IPERCController : Controller
    {
        private string connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        // ============================================
        // VERIFICAR SESIÓN ADMIN
        // ============================================
        private bool EsAdmin()
        {
            return Session["Rol"] != null && Session["Rol"].ToString() == "Admin";
        }

        // ============================================
        // VISTA PRINCIPAL (LISTADO)
        // ============================================
        public ActionResult Index()
        {
            if (!EsAdmin())
                return RedirectToAction("Login", "Account");

            List<IPERC_Matriz> lista = new List<IPERC_Matriz>();

            string query = @"SELECT m.Id, m.Codigo, m.Actividad, m.Tarea, m.Peligro, m.Riesgo,
                                    p.Nombre as PuestoNombre, tp.Nombre as TipoPeligroNombre,
                                    e.NivelRiesgo, e.Significancia, m.EsRutinaria, m.Activo,
                                    e.PersonasExpuestas, e.ProcedimientosExistentes, e.Capacitacion, e.ExposicionRiesgo, e.Severidad
                             FROM IPERC_Matriz m
                             LEFT JOIN PuestosTrabajo p ON m.PuestoId = p.Id
                             LEFT JOIN TiposPeligro tp ON m.TipoPeligroId = tp.Id
                             LEFT JOIN IPERC_Evaluacion e ON m.Id = e.IPERCId AND e.Tipo = 'INICIAL'
                             WHERE m.Activo = 1
                             ORDER BY m.Id DESC";

            DataTable dt = DatabaseHelper.ExecuteQuery(query, null);

            foreach (DataRow row in dt.Rows)
            {
                lista.Add(new IPERC_Matriz
                {
                    Id = Convert.ToInt32(row["Id"]),
                    Codigo = row["Codigo"]?.ToString(),
                    Actividad = row["Actividad"]?.ToString(),
                    Tarea = row["Tarea"]?.ToString(),
                    Peligro = row["Peligro"]?.ToString(),
                    Riesgo = row["Riesgo"]?.ToString(),
                    PuestoNombre = row["PuestoNombre"]?.ToString(),
                    TipoPeligroNombre = row["TipoPeligroNombre"]?.ToString(),
                    EsRutinaria = Convert.ToBoolean(row["EsRutinaria"]),
                    Activo = Convert.ToBoolean(row["Activo"]),
                    Evaluacion = new IPERC_Evaluacion
                    {
                        NivelRiesgo = row["NivelRiesgo"] != DBNull.Value ? Convert.ToInt32(row["NivelRiesgo"]) : 0,
                        Significancia = row["Significancia"] != DBNull.Value && Convert.ToBoolean(row["Significancia"])
                    }
                });
            }

            return View(lista);
        }

        // ============================================
        // CREAR NUEVO REGISTRO (GET)
        // ============================================
        public ActionResult Create()
        {
            if (!EsAdmin())
                return RedirectToAction("Login", "Account");

            var model = new IPERCViewModel();
            CargarListas(model);

            return View(model);
        }

        // ============================================
        // CREAR NUEVO REGISTRO (POST)
        // ============================================
        [HttpPost]
        [ValidateInput(false)]
        public ActionResult Create(IPERCViewModel model)
        {
            if (!EsAdmin())
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                // 1. Insertar matriz
                string queryMatriz = @"INSERT INTO IPERC_Matriz (Codigo, PuestoId, Actividad, Tarea, TipoPeligroId, 
                                        BaseLegal, Peligro, Riesgo, Consecuencia, EsRutinaria, EsEmergencia, 
                                        GrupoVulnerable, FechaCreacion, CreadoPor, Activo)
                                        VALUES (@Codigo, @PuestoId, @Actividad, @Tarea, @TipoPeligroId,
                                        @BaseLegal, @Peligro, @Riesgo, @Consecuencia, @EsRutinaria, @EsEmergencia,
                                        @GrupoVulnerable, GETDATE(), @CreadoPor, 1);
                                        SELECT SCOPE_IDENTITY();";

                SqlParameter[] paramsMatriz = new SqlParameter[]
                {
                    new SqlParameter("@Codigo", model.Matriz.Codigo ?? GenerarCodigo()),
                    new SqlParameter("@PuestoId", model.Matriz.PuestoId),
                    new SqlParameter("@Actividad", model.Matriz.Actividad ?? ""),
                    new SqlParameter("@Tarea", model.Matriz.Tarea ?? ""),
                    new SqlParameter("@TipoPeligroId", model.Matriz.TipoPeligroId),
                    new SqlParameter("@BaseLegal", model.Matriz.BaseLegal ?? ""),
                    new SqlParameter("@Peligro", model.Matriz.Peligro ?? ""),
                    new SqlParameter("@Riesgo", model.Matriz.Riesgo ?? ""),
                    new SqlParameter("@Consecuencia", model.Matriz.Consecuencia ?? ""),
                    new SqlParameter("@EsRutinaria", model.Matriz.EsRutinaria),
                    new SqlParameter("@EsEmergencia", model.Matriz.EsEmergencia),
                    new SqlParameter("@GrupoVulnerable", model.Matriz.GrupoVulnerable ?? ""),
                    new SqlParameter("@CreadoPor", Convert.ToInt32(Session["UsuarioId"]))
                };

                object result = DatabaseHelper.ExecuteScalar(queryMatriz, paramsMatriz);
                int matrizId = Convert.ToInt32(result);

                // 2. Insertar evaluación
                string queryEvaluacion = @"INSERT INTO IPERC_Evaluacion (IPERCId, Tipo, PersonasExpuestas, ProcedimientosExistentes,
                                            Capacitacion, ExposicionRiesgo, Severidad, FechaEvaluacion)
                                            VALUES (@IPERCId, 'INICIAL', @PersonasExpuestas, @ProcedimientosExistentes,
                                            @Capacitacion, @ExposicionRiesgo, @Severidad, GETDATE());
                                            SELECT SCOPE_IDENTITY();";

                SqlParameter[] paramsEval = new SqlParameter[]
                {
                    new SqlParameter("@IPERCId", matrizId),
                    new SqlParameter("@PersonasExpuestas", model.Evaluacion.PersonasExpuestas),
                    new SqlParameter("@ProcedimientosExistentes", model.Evaluacion.ProcedimientosExistentes),
                    new SqlParameter("@Capacitacion", model.Evaluacion.Capacitacion),
                    new SqlParameter("@ExposicionRiesgo", model.Evaluacion.ExposicionRiesgo),
                    new SqlParameter("@Severidad", model.Evaluacion.Severidad)
                };

                object evalResult = DatabaseHelper.ExecuteScalar(queryEvaluacion, paramsEval);
                int evaluacionId = Convert.ToInt32(evalResult);

                // 3. Insertar controles
                if (model.Controles != null && model.Controles.Any())
                {
                    foreach (var control in model.Controles)
                    {
                        string queryControl = @"INSERT INTO IPERC_Controles (EvaluacionId, TipoControl, Descripcion, Orden)
                                                VALUES (@EvaluacionId, @TipoControl, @Descripcion, @Orden)";

                        SqlParameter[] paramsControl = new SqlParameter[]
                        {
                            new SqlParameter("@EvaluacionId", evaluacionId),
                            new SqlParameter("@TipoControl", control.TipoControl),
                            new SqlParameter("@Descripcion", control.Descripcion ?? ""),
                            new SqlParameter("@Orden", control.Orden)
                        };
                        DatabaseHelper.ExecuteNonQuery(queryControl, paramsControl);
                    }
                }

                TempData["Success"] = "Registro creado exitosamente";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error: " + ex.Message;
                CargarListas(model);
                return View(model);
            }
        }

        // ============================================
        // EDITAR REGISTRO (GET)
        // ============================================
        public ActionResult Edit(int id)
        {
            if (!EsAdmin())
                return RedirectToAction("Login", "Account");

            var model = new IPERCViewModel();
            CargarListas(model);

            // Cargar matriz
            string queryMatriz = @"SELECT * FROM IPERC_Matriz WHERE Id = @Id";
            DataTable dtMatriz = DatabaseHelper.ExecuteQuery(queryMatriz, new SqlParameter[] { new SqlParameter("@Id", id) });

            if (dtMatriz.Rows.Count == 0)
            {
                TempData["Error"] = "Registro no encontrado";
                return RedirectToAction("Index");
            }

            DataRow row = dtMatriz.Rows[0];
            model.Matriz = new IPERC_Matriz
            {
                Id = Convert.ToInt32(row["Id"]),
                Codigo = row["Codigo"]?.ToString(),
                PuestoId = Convert.ToInt32(row["PuestoId"]),
                Actividad = row["Actividad"]?.ToString(),
                Tarea = row["Tarea"]?.ToString(),
                TipoPeligroId = Convert.ToInt32(row["TipoPeligroId"]),
                BaseLegal = row["BaseLegal"]?.ToString(),
                Peligro = row["Peligro"]?.ToString(),
                Riesgo = row["Riesgo"]?.ToString(),
                Consecuencia = row["Consecuencia"]?.ToString(),
                EsRutinaria = Convert.ToBoolean(row["EsRutinaria"]),
                EsEmergencia = Convert.ToBoolean(row["EsEmergencia"]),
                GrupoVulnerable = row["GrupoVulnerable"]?.ToString()
            };

            // Cargar evaluación
            string queryEval = @"SELECT * FROM IPERC_Evaluacion WHERE IPERCId = @Id AND Tipo = 'INICIAL'";
            DataTable dtEval = DatabaseHelper.ExecuteQuery(queryEval, new SqlParameter[] { new SqlParameter("@Id", id) });

            if (dtEval.Rows.Count > 0)
            {
                DataRow rowEval = dtEval.Rows[0];
                model.Evaluacion = new IPERC_Evaluacion
                {
                    Id = Convert.ToInt32(rowEval["Id"]),
                    PersonasExpuestas = Convert.ToInt32(rowEval["PersonasExpuestas"]),
                    ProcedimientosExistentes = Convert.ToInt32(rowEval["ProcedimientosExistentes"]),
                    Capacitacion = Convert.ToInt32(rowEval["Capacitacion"]),
                    ExposicionRiesgo = Convert.ToInt32(rowEval["ExposicionRiesgo"]),
                    Severidad = Convert.ToInt32(rowEval["Severidad"])
                };
            }
            else
            {
                model.Evaluacion = new IPERC_Evaluacion();
            }

            // Cargar controles
            string queryControles = @"SELECT * FROM IPERC_Controles WHERE EvaluacionId IN 
                                       (SELECT Id FROM IPERC_Evaluacion WHERE IPERCId = @Id AND Tipo = 'INICIAL')";
            DataTable dtControles = DatabaseHelper.ExecuteQuery(queryControles, new SqlParameter[] { new SqlParameter("@Id", id) });

            model.Controles = new List<IPERC_Control>();
            foreach (DataRow rowControl in dtControles.Rows)
            {
                model.Controles.Add(new IPERC_Control
                {
                    Id = Convert.ToInt32(rowControl["Id"]),
                    TipoControl = rowControl["TipoControl"]?.ToString(),
                    Descripcion = rowControl["Descripcion"]?.ToString(),
                    Orden = Convert.ToInt32(rowControl["Orden"])
                });
            }

            return View(model);
        }

        // ============================================
        // EDITAR REGISTRO (POST)
        // ============================================
        [HttpPost]
        [ValidateInput(false)]
        public ActionResult Edit(IPERCViewModel model)
        {
            if (!EsAdmin())
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                // Actualizar matriz
                string queryMatriz = @"UPDATE IPERC_Matriz SET 
                                        Codigo = @Codigo,
                                        PuestoId = @PuestoId,
                                        Actividad = @Actividad,
                                        Tarea = @Tarea,
                                        TipoPeligroId = @TipoPeligroId,
                                        BaseLegal = @BaseLegal,
                                        Peligro = @Peligro,
                                        Riesgo = @Riesgo,
                                        Consecuencia = @Consecuencia,
                                        EsRutinaria = @EsRutinaria,
                                        EsEmergencia = @EsEmergencia,
                                        GrupoVulnerable = @GrupoVulnerable,
                                        FechaActualizacion = GETDATE()
                                        WHERE Id = @Id";

                SqlParameter[] paramsMatriz = new SqlParameter[]
                {
                    new SqlParameter("@Id", model.Matriz.Id),
                    new SqlParameter("@Codigo", model.Matriz.Codigo ?? GenerarCodigo()),
                    new SqlParameter("@PuestoId", model.Matriz.PuestoId),
                    new SqlParameter("@Actividad", model.Matriz.Actividad ?? ""),
                    new SqlParameter("@Tarea", model.Matriz.Tarea ?? ""),
                    new SqlParameter("@TipoPeligroId", model.Matriz.TipoPeligroId),
                    new SqlParameter("@BaseLegal", model.Matriz.BaseLegal ?? ""),
                    new SqlParameter("@Peligro", model.Matriz.Peligro ?? ""),
                    new SqlParameter("@Riesgo", model.Matriz.Riesgo ?? ""),
                    new SqlParameter("@Consecuencia", model.Matriz.Consecuencia ?? ""),
                    new SqlParameter("@EsRutinaria", model.Matriz.EsRutinaria),
                    new SqlParameter("@EsEmergencia", model.Matriz.EsEmergencia),
                    new SqlParameter("@GrupoVulnerable", model.Matriz.GrupoVulnerable ?? "")
                };

                DatabaseHelper.ExecuteNonQuery(queryMatriz, paramsMatriz);

                // Actualizar evaluación
                string queryEval = @"UPDATE IPERC_Evaluacion SET 
                                        PersonasExpuestas = @PersonasExpuestas,
                                        ProcedimientosExistentes = @ProcedimientosExistentes,
                                        Capacitacion = @Capacitacion,
                                        ExposicionRiesgo = @ExposicionRiesgo,
                                        Severidad = @Severidad,
                                        FechaEvaluacion = GETDATE()
                                        WHERE IPERCId = @IPERCId AND Tipo = 'INICIAL'";

                SqlParameter[] paramsEval = new SqlParameter[]
                {
                    new SqlParameter("@IPERCId", model.Matriz.Id),
                    new SqlParameter("@PersonasExpuestas", model.Evaluacion.PersonasExpuestas),
                    new SqlParameter("@ProcedimientosExistentes", model.Evaluacion.ProcedimientosExistentes),
                    new SqlParameter("@Capacitacion", model.Evaluacion.Capacitacion),
                    new SqlParameter("@ExposicionRiesgo", model.Evaluacion.ExposicionRiesgo),
                    new SqlParameter("@Severidad", model.Evaluacion.Severidad)
                };
                DatabaseHelper.ExecuteNonQuery(queryEval, paramsEval);

                TempData["Success"] = "Registro actualizado exitosamente";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error: " + ex.Message;
                CargarListas(model);
                return View(model);
            }
        }

        // ============================================
        // ELIMINAR REGISTRO (POST)
        // ============================================
        [HttpPost]
        public JsonResult Delete(int id)
        {
            if (!EsAdmin())
                return Json(new { success = false, message = "No autorizado" });

            try
            {
                string query = "UPDATE IPERC_Matriz SET Activo = 0 WHERE Id = @Id";
                DatabaseHelper.ExecuteNonQuery(query, new SqlParameter[] { new SqlParameter("@Id", id) });

                return Json(new { success = true, message = "Registro eliminado" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ============================================
        // VISTA DE DETALLE
        // ============================================
        public ActionResult Details(int id)
        {
            if (!EsAdmin())
                return RedirectToAction("Login", "Account");

            var model = new IPERCViewModel();

            string query = @"SELECT m.*, p.Nombre as PuestoNombre, tp.Nombre as TipoPeligroNombre,
                                    e.*, 
                                    CASE 
                                        WHEN e.NivelRiesgo <= 4 THEN 'Bajo'
                                        WHEN e.NivelRiesgo <= 9 THEN 'Medio'
                                        WHEN e.NivelRiesgo <= 16 THEN 'Alto'
                                        ELSE 'Muy Alto'
                                    END as NivelTexto
                             FROM IPERC_Matriz m
                             LEFT JOIN PuestosTrabajo p ON m.PuestoId = p.Id
                             LEFT JOIN TiposPeligro tp ON m.TipoPeligroId = tp.Id
                             LEFT JOIN IPERC_Evaluacion e ON m.Id = e.IPERCId AND e.Tipo = 'INICIAL'
                             WHERE m.Id = @Id";

            DataTable dt = DatabaseHelper.ExecuteQuery(query, new SqlParameter[] { new SqlParameter("@Id", id) });

            if (dt.Rows.Count == 0)
            {
                TempData["Error"] = "Registro no encontrado";
                return RedirectToAction("Index");
            }

            DataRow row = dt.Rows[0];
            model.Matriz = new IPERC_Matriz
            {
                Id = Convert.ToInt32(row["Id"]),
                Codigo = row["Codigo"]?.ToString(),
                Actividad = row["Actividad"]?.ToString(),
                Tarea = row["Tarea"]?.ToString(),
                Peligro = row["Peligro"]?.ToString(),
                Riesgo = row["Riesgo"]?.ToString(),
                Consecuencia = row["Consecuencia"]?.ToString(),
                BaseLegal = row["BaseLegal"]?.ToString(),
                EsRutinaria = Convert.ToBoolean(row["EsRutinaria"]),
                EsEmergencia = Convert.ToBoolean(row["EsEmergencia"]),
                GrupoVulnerable = row["GrupoVulnerable"]?.ToString(),
                PuestoNombre = row["PuestoNombre"]?.ToString(),
                TipoPeligroNombre = row["TipoPeligroNombre"]?.ToString(),
                FechaCreacion = Convert.ToDateTime(row["FechaCreacion"])
            };

            if (row["PersonasExpuestas"] != DBNull.Value)
            {
                model.Evaluacion = new IPERC_Evaluacion
                {
                    PersonasExpuestas = Convert.ToInt32(row["PersonasExpuestas"]),
                    ProcedimientosExistentes = Convert.ToInt32(row["ProcedimientosExistentes"]),
                    Capacitacion = Convert.ToInt32(row["Capacitacion"]),
                    ExposicionRiesgo = Convert.ToInt32(row["ExposicionRiesgo"]),
                    Severidad = Convert.ToInt32(row["Severidad"]),
                    Probabilidad = Convert.ToInt32(row["Probabilidad"]),
                    NivelRiesgo = Convert.ToInt32(row["NivelRiesgo"]),
                    Significancia = Convert.ToBoolean(row["Significancia"])
                };
            }

            // Cargar controles
            string queryControles = @"SELECT * FROM IPERC_Controles WHERE EvaluacionId IN 
                                       (SELECT Id FROM IPERC_Evaluacion WHERE IPERCId = @Id AND Tipo = 'INICIAL')
                                       ORDER BY Orden";
            DataTable dtControles = DatabaseHelper.ExecuteQuery(queryControles, new SqlParameter[] { new SqlParameter("@Id", id) });

            model.Controles = new List<IPERC_Control>();
            foreach (DataRow rowControl in dtControles.Rows)
            {
                model.Controles.Add(new IPERC_Control
                {
                    TipoControl = rowControl["TipoControl"]?.ToString(),
                    Descripcion = rowControl["Descripcion"]?.ToString()
                });
            }

            ViewBag.NivelTexto = dt.Rows[0]["NivelTexto"]?.ToString();
            ViewBag.ColorNivel = GetColorNivel(model.Evaluacion?.NivelRiesgo ?? 0);

            return View(model);
        }

        // ============================================
        // REPORTE GENERAL
        // ============================================
        public ActionResult Reporte()
        {
            if (!EsAdmin())
                return RedirectToAction("Login", "Account");

            var reporte = new IPERCReporteViewModel();

            // Totales
            string queryTotal = @"SELECT COUNT(*) as Total FROM IPERC_Matriz WHERE Activo = 1";
            reporte.TotalRegistros = Convert.ToInt32(DatabaseHelper.ExecuteScalar(queryTotal, null));

            // Riesgos por nivel
            string queryNiveles = @"SELECT 
                                        SUM(CASE WHEN e.NivelRiesgo <= 4 THEN 1 ELSE 0 END) as Bajos,
                                        SUM(CASE WHEN e.NivelRiesgo BETWEEN 5 AND 9 THEN 1 ELSE 0 END) as Medios,
                                        SUM(CASE WHEN e.NivelRiesgo BETWEEN 10 AND 16 THEN 1 ELSE 0 END) as Altos,
                                        SUM(CASE WHEN e.NivelRiesgo >= 17 THEN 1 ELSE 0 END) as MuyAltos
                                    FROM IPERC_Matriz m
                                    LEFT JOIN IPERC_Evaluacion e ON m.Id = e.IPERCId AND e.Tipo = 'INICIAL'
                                    WHERE m.Activo = 1";

            DataTable dtNiveles = DatabaseHelper.ExecuteQuery(queryNiveles, null);
            if (dtNiveles.Rows.Count > 0)
            {
                reporte.RiesgosBajos = Convert.ToInt32(dtNiveles.Rows[0]["Bajos"]);
                reporte.RiesgosMedios = Convert.ToInt32(dtNiveles.Rows[0]["Medios"]);
                reporte.RiesgosAltos = Convert.ToInt32(dtNiveles.Rows[0]["Altos"]);
                reporte.RiesgosMuyAltos = Convert.ToInt32(dtNiveles.Rows[0]["MuyAltos"]);
            }

            // Resumen por puesto
            string queryPuesto = @"SELECT p.Nombre as Puesto, COUNT(*) as Total,
                                        SUM(CASE WHEN e.NivelRiesgo >= 17 THEN 1 ELSE 0 END) as Altos
                                    FROM IPERC_Matriz m
                                    LEFT JOIN PuestosTrabajo p ON m.PuestoId = p.Id
                                    LEFT JOIN IPERC_Evaluacion e ON m.Id = e.IPERCId AND e.Tipo = 'INICIAL'
                                    WHERE m.Activo = 1
                                    GROUP BY p.Nombre
                                    ORDER BY Altos DESC";

            DataTable dtPuesto = DatabaseHelper.ExecuteQuery(queryPuesto, null);
            reporte.ResumenPorPuesto = new List<ResumenPorPuesto>();
            foreach (DataRow row in dtPuesto.Rows)
            {
                reporte.ResumenPorPuesto.Add(new ResumenPorPuesto
                {
                    Puesto = row["Puesto"]?.ToString(),
                    Total = Convert.ToInt32(row["Total"]),
                    Altos = Convert.ToInt32(row["Altos"])
                });
            }

            // Resumen por tipo de peligro
            string queryPeligro = @"SELECT tp.Nombre as Peligro, COUNT(*) as Total
                                    FROM IPERC_Matriz m
                                    LEFT JOIN TiposPeligro tp ON m.TipoPeligroId = tp.Id
                                    WHERE m.Activo = 1
                                    GROUP BY tp.Nombre
                                    ORDER BY Total DESC";

            DataTable dtPeligro = DatabaseHelper.ExecuteQuery(queryPeligro, null);
            reporte.ResumenPorPeligro = new List<ResumenPorPeligro>();
            foreach (DataRow row in dtPeligro.Rows)
            {
                reporte.ResumenPorPeligro.Add(new ResumenPorPeligro
                {
                    Peligro = row["Peligro"]?.ToString(),
                    Total = Convert.ToInt32(row["Total"]),
                    Porcentaje = reporte.TotalRegistros > 0 ? (Convert.ToInt32(row["Total"]) * 100 / reporte.TotalRegistros) : 0
                });
            }

            // Detalle
            string queryDetalle = @"SELECT m.Id, m.Codigo, m.Actividad, m.Peligro, m.Riesgo,
                                        p.Nombre as PuestoNombre, tp.Nombre as TipoPeligroNombre,
                                        e.NivelRiesgo
                                    FROM IPERC_Matriz m
                                    LEFT JOIN PuestosTrabajo p ON m.PuestoId = p.Id
                                    LEFT JOIN TiposPeligro tp ON m.TipoPeligroId = tp.Id
                                    LEFT JOIN IPERC_Evaluacion e ON m.Id = e.IPERCId AND e.Tipo = 'INICIAL'
                                    WHERE m.Activo = 1
                                    ORDER BY e.NivelRiesgo DESC";

            DataTable dtDetalle = DatabaseHelper.ExecuteQuery(queryDetalle, null);
            reporte.Detalle = new List<IPERC_Matriz>();
            foreach (DataRow row in dtDetalle.Rows)
            {
                reporte.Detalle.Add(new IPERC_Matriz
                {
                    Id = Convert.ToInt32(row["Id"]),
                    Codigo = row["Codigo"]?.ToString(),
                    Actividad = row["Actividad"]?.ToString(),
                    Peligro = row["Peligro"]?.ToString(),
                    Riesgo = row["Riesgo"]?.ToString(),
                    PuestoNombre = row["PuestoNombre"]?.ToString(),
                    TipoPeligroNombre = row["TipoPeligroNombre"]?.ToString(),
                    Evaluacion = new IPERC_Evaluacion
                    {
                        NivelRiesgo = row["NivelRiesgo"] != DBNull.Value ? Convert.ToInt32(row["NivelRiesgo"]) : 0
                    }
                });
            }

            return View(reporte);
        }

        // ============================================
        // EXPORTAR A EXCEL
        // ============================================
        public ActionResult ExportarExcel()
        {
            if (!EsAdmin())
                return RedirectToAction("Login", "Account");

            string query = @"SELECT 
                                m.Id as 'ID',
                                m.Codigo as 'Código',
                                p.Nombre as 'Puesto',
                                m.Actividad as 'Actividad',
                                m.Tarea as 'Tarea',
                                tp.Nombre as 'Tipo de Peligro',
                                m.Peligro as 'Peligro',
                                m.Riesgo as 'Riesgo',
                                m.Consecuencia as 'Consecuencia',
                                CASE WHEN m.EsRutinaria = 1 THEN 'Sí' ELSE 'No' END as '¿Rutinaria?',
                                e.PersonasExpuestas as 'Personas Expuestas',
                                e.ProcedimientosExistentes as 'Procedimientos',
                                e.Capacitacion as 'Capacitación',
                                e.ExposicionRiesgo as 'Exposición',
                                e.Probabilidad as 'Probabilidad',
                                e.Severidad as 'Severidad',
                                e.NivelRiesgo as 'Nivel de Riesgo',
                                CASE WHEN e.Significancia = 1 THEN 'SÍ' ELSE 'NO' END as 'Significancia'
                            FROM IPERC_Matriz m
                            LEFT JOIN PuestosTrabajo p ON m.PuestoId = p.Id
                            LEFT JOIN TiposPeligro tp ON m.TipoPeligroId = tp.Id
                            LEFT JOIN IPERC_Evaluacion e ON m.Id = e.IPERCId AND e.Tipo = 'INICIAL'
                            WHERE m.Activo = 1
                            ORDER BY e.NivelRiesgo DESC";

            DataTable dt = DatabaseHelper.ExecuteQuery(query, null);

            // Generar Excel
            var grid = new System.Web.UI.WebControls.GridView();
            grid.DataSource = dt;
            grid.DataBind();

            Response.ClearContent();
            Response.Buffer = true;
            Response.AddHeader("content-disposition", "attachment; filename=IPERC_Reporte.xls");
            Response.ContentType = "application/ms-excel";
            Response.Charset = "";
            Response.ContentEncoding = System.Text.Encoding.UTF8;

            using (var sw = new System.IO.StringWriter())
            using (var htw = new System.Web.UI.HtmlTextWriter(sw))
            {
                grid.RenderControl(htw);
                Response.Write(sw.ToString());
                Response.End();
            }

            return null;
        }

        // ============================================
        // MÉTODOS AUXILIARES
        // ============================================
        private void CargarListas(IPERCViewModel model)
        {
            // Tipos de peligro
            string queryTipos = "SELECT Id, Nombre FROM TiposPeligro WHERE Activo = 1 ORDER BY Nombre";
            DataTable dtTipos = DatabaseHelper.ExecuteQuery(queryTipos, null);
            model.TiposPeligro = new List<TipoPeligro>();
            foreach (DataRow row in dtTipos.Rows)
            {
                model.TiposPeligro.Add(new TipoPeligro
                {
                    Id = Convert.ToInt32(row["Id"]),
                    Nombre = row["Nombre"].ToString()
                });
            }

            // Puestos de trabajo
            string queryPuestos = "SELECT Id, Nombre FROM PuestosTrabajo WHERE Activo = 1 ORDER BY Nombre";
            DataTable dtPuestos = DatabaseHelper.ExecuteQuery(queryPuestos, null);
            model.Puestos = new List<PuestoTrabajo>();
            foreach (DataRow row in dtPuestos.Rows)
            {
                model.Puestos.Add(new PuestoTrabajo
                {
                    Id = Convert.ToInt32(row["Id"]),
                    Nombre = row["Nombre"].ToString()
                });
            }

            // Factores
            string queryFactores = "SELECT Factor, Valor, Descripcion FROM IPERC_Factores ORDER BY Factor, Valor";
            DataTable dtFactores = DatabaseHelper.ExecuteQuery(queryFactores, null);

            model.FactoresPersonas = new List<FactorEvaluacion>();
            model.FactoresProcedimientos = new List<FactorEvaluacion>();
            model.FactoresCapacitacion = new List<FactorEvaluacion>();
            model.FactoresExposicion = new List<FactorEvaluacion>();
            model.FactoresSeveridad = new List<FactorEvaluacion>();

            foreach (DataRow row in dtFactores.Rows)
            {
                var factor = new FactorEvaluacion
                {
                    Factor = row["Factor"].ToString(),
                    Valor = Convert.ToInt32(row["Valor"]),
                    Descripcion = row["Descripcion"].ToString()
                };

                switch (factor.Factor)
                {
                    case "PERSONAS": model.FactoresPersonas.Add(factor); break;
                    case "PROCEDIMIENTOS": model.FactoresProcedimientos.Add(factor); break;
                    case "CAPACITACION": model.FactoresCapacitacion.Add(factor); break;
                    case "EXPOSICION": model.FactoresExposicion.Add(factor); break;
                    case "SEVERIDAD": model.FactoresSeveridad.Add(factor); break;
                }
            }
        }

        private string GenerarCodigo()
        {
            return "IPERC-" + DateTime.Now.ToString("yyyyMMddHHmmss");
        }

        private string GetColorNivel(int nivel)
        {
            if (nivel <= 4) return "#00FF00";
            if (nivel <= 9) return "#FFFF00";
            if (nivel <= 16) return "#FFA500";
            return "#FF0000";
        }
    }
}