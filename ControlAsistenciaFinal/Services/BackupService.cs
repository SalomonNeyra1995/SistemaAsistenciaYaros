using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Hosting;

namespace ControlAsistenciaFinal.Services
{
    public class BackupService : IDisposable
    {
        private readonly string _backupRootFolder;
        private readonly string _connectionString;
        private readonly string _customBackupPath;
        private bool disposed = false;

        public BackupService()
        {
            _backupRootFolder = Path.Combine(HostingEnvironment.MapPath("~"), "Backups");
            _connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            _customBackupPath = @"C:\Users\HP\Desktop\Escritorio\DocGuiaYamiflo\ArchivosBackup";

            if (!Directory.Exists(_backupRootFolder))
                Directory.CreateDirectory(_backupRootFolder);

            if (!Directory.Exists(_customBackupPath))
                Directory.CreateDirectory(_customBackupPath);
        }

        // ============================================
        // MÉTODOS DE BACKUP
        // ============================================

        public async Task<string> GenerarBackupMensualAsync(int anio, int mes)
        {
            var nombreCarpeta = $"BackAsistencia-{anio:D4}-{mes:D2}";
            var rutaCarpeta = Path.Combine(_backupRootFolder, nombreCarpeta);

            if (!Directory.Exists(rutaCarpeta))
                Directory.CreateDirectory(rutaCarpeta);

            var fechaInicio = new DateTime(anio, mes, 1);
            var fechaFin = fechaInicio.AddMonths(1).AddDays(-1);
            var nombreArchivo = $"Backup_{anio:D4}_{mes:D2}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var rutaArchivo = Path.Combine(rutaCarpeta, nombreArchivo);

            using (var package = new ExcelPackage())
            {
                var dtUsuarios = await Task.Run(() => ObtenerTabla("SELECT * FROM Usuarios"));
                AgregarHojaExcel(package, "Usuarios", dtUsuarios);

                string queryRegistros = @"
                    SELECT * FROM RegistrosAsistencia 
                    WHERE Fecha >= @FechaInicio 
                    AND Fecha <= @FechaFin";

                var parameters = new SqlParameter[]
                {
                    new SqlParameter("@FechaInicio", fechaInicio),
                    new SqlParameter("@FechaFin", fechaFin)
                };

                var dtRegistros = await Task.Run(() => ObtenerTabla(queryRegistros, parameters));
                AgregarHojaExcel(package, "RegistrosAsistencia", dtRegistros);

                string queryExcepcionales = @"
                    SELECT * FROM RegistrosExcepcionales 
                    WHERE FechaRegistro >= @FechaInicio 
                    AND FechaRegistro <= @FechaFin";

                var dtExcepcionales = await Task.Run(() => ObtenerTabla(queryExcepcionales, parameters));
                AgregarHojaExcel(package, "RegistrosExcepcionales", dtExcepcionales);

                var dtConfiguracion = await Task.Run(() => ObtenerTabla("SELECT * FROM Configuracion"));
                AgregarHojaExcel(package, "Configuracion", dtConfiguracion);

                string queryAlertas = @"
                    SELECT * FROM AlertasHoras 
                    WHERE FechaAlerta >= @FechaInicio 
                    AND FechaAlerta <= @FechaFin";

                var dtAlertas = await Task.Run(() => ObtenerTabla(queryAlertas, parameters));
                AgregarHojaExcel(package, "AlertasHoras", dtAlertas);

                string queryNotificaciones = @"
                    SELECT * FROM NotificacionesAdmin 
                    WHERE FechaCreacion >= @FechaInicio 
                    AND FechaCreacion <= @FechaFin";

                var dtNotificaciones = await Task.Run(() => ObtenerTabla(queryNotificaciones, parameters));
                AgregarHojaExcel(package, "NotificacionesAdmin", dtNotificaciones);

                string querySolicitudes = @"
                    SELECT * FROM SolicitudesExcepcionales 
                    WHERE FechaSolicitud >= @FechaInicio 
                    AND FechaSolicitud <= @FechaFin";

                var dtSolicitudes = await Task.Run(() => ObtenerTabla(querySolicitudes, parameters));
                AgregarHojaExcel(package, "SolicitudesExcepcionales", dtSolicitudes);

                var hojaInfo = package.Workbook.Worksheets.Add("Info_Backup");
                hojaInfo.Cells[1, 1].Value = "Fecha Backup:";
                hojaInfo.Cells[1, 2].Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                hojaInfo.Cells[2, 1].Value = "Período:";
                hojaInfo.Cells[2, 2].Value = $"{fechaInicio:dd/MM/yyyy} - {fechaFin:dd/MM/yyyy}";
                hojaInfo.Cells[3, 1].Value = "Tipo Backup:";
                hojaInfo.Cells[3, 2].Value = "MANUAL";
                hojaInfo.Cells[4, 1].Value = "Total Registros Asistencia:";
                hojaInfo.Cells[4, 2].Value = dtRegistros.Rows.Count;
                hojaInfo.Cells[5, 1].Value = "Total Usuarios:";
                hojaInfo.Cells[5, 2].Value = dtUsuarios.Rows.Count;
                hojaInfo.Cells.AutoFitColumns();

                package.SaveAs(new FileInfo(rutaArchivo));
            }

            await RegistrarEventoBackupAsync(0, "BACKUP_MANUAL",
                $"Backup manual generado para {nombreCarpeta}", "EXITOSO");

            return rutaArchivo;
        }

        // ============================================
        // MÉTODOS PRIVADOS
        // ============================================

        private DataTable ObtenerTabla(string query, SqlParameter[] parameters = null)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.CommandType = CommandType.Text;

                    if (parameters != null)
                    {
                        cmd.Parameters.Clear();
                        foreach (var param in parameters)
                        {
                            cmd.Parameters.AddWithValue(param.ParameterName, param.Value ?? DBNull.Value);
                        }
                    }

                    SqlDataAdapter da = new SqlDataAdapter(cmd);
                    DataTable dt = new DataTable();
                    da.Fill(dt);
                    return dt;
                }
            }
        }

        private void AgregarHojaExcel(ExcelPackage package, string nombreHoja, DataTable dt)
        {
            var worksheet = package.Workbook.Worksheets.Add(nombreHoja);

            for (int i = 0; i < dt.Columns.Count; i++)
                worksheet.Cells[1, i + 1].Value = dt.Columns[i].ColumnName;

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                for (int j = 0; j < dt.Columns.Count; j++)
                {
                    worksheet.Cells[i + 2, j + 1].Value = dt.Rows[i][j]?.ToString();
                }
            }

            worksheet.Cells.AutoFitColumns();
        }

        // ============================================
        // MÉTODOS PARA OBTENER BACKUPS DISPONIBLES
        // ============================================

        public List<BackupInfo> ObtenerBackupsDisponibles()
        {
            var backups = new List<BackupInfo>();

            if (!Directory.Exists(_backupRootFolder))
                return backups;

            var carpetas = Directory.GetDirectories(_backupRootFolder, "BackAsistencia-*");

            foreach (var carpeta in carpetas)
            {
                var archivos = Directory.GetFiles(carpeta, "Backup_*.xlsx");
                var nombreCarpeta = Path.GetFileName(carpeta);
                var partes = nombreCarpeta.Replace("BackAsistencia-", "").Split('-');

                if (partes.Length == 2 && int.TryParse(partes[0], out int anio) && int.TryParse(partes[1], out int mes))
                {
                    backups.Add(new BackupInfo
                    {
                        Anio = anio,
                        Mes = mes,
                        RutaCarpeta = carpeta,
                        Archivos = archivos.Select(f => Path.GetFileName(f)).ToList(),
                        FechaCreacion = Directory.GetCreationTime(carpeta)
                    });
                }
            }

            return backups.OrderByDescending(b => b.Anio).ThenByDescending(b => b.Mes).ToList();
        }

        public string ObtenerRutaArchivoBackup(int anio, int mes, string nombreArchivo)
        {
            var nombreCarpeta = $"BackAsistencia-{anio:D4}-{mes:D2}";
            var rutaCarpeta = Path.Combine(_backupRootFolder, nombreCarpeta);
            return Path.Combine(rutaCarpeta, nombreArchivo);
        }

        // ============================================
        // CONVERSIÓN DE FECHAS
        // ============================================

        private DateTime? ConvertirFechaForzada(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return null;

            try
            {
                valor = valor.Trim();

                if (valor.ToLower() == "true" || valor.ToLower() == "false")
                    return null;

                string fechaLimpia = valor.Replace("-", "/").Replace(".", "/");
                string fechaParte = fechaLimpia;
                string horaParte = "00:00:00";
                int espacioIndex = fechaLimpia.IndexOf(' ');
                if (espacioIndex > 0)
                {
                    fechaParte = fechaLimpia.Substring(0, espacioIndex);
                    horaParte = fechaLimpia.Substring(espacioIndex + 1).Trim();
                }

                string[] partesFecha = fechaParte.Split('/');
                if (partesFecha.Length != 3) return null;

                int dia = 0, mes = 0, anio = 0;
                if (!int.TryParse(partesFecha[0], out dia)) return null;
                if (!int.TryParse(partesFecha[1], out mes)) return null;
                if (!int.TryParse(partesFecha[2], out anio)) return null;

                if (anio < 100) anio = 2000 + anio;
                if (dia < 1 || dia > 31) return null;
                if (mes < 1 || mes > 12) return null;
                if (anio < 1900 || anio > 2100) return null;

                int horas = 0, minutos = 0, segundos = 0;
                string[] partesHora = horaParte.Split(':');
                if (partesHora.Length >= 1) int.TryParse(partesHora[0], out horas);
                if (partesHora.Length >= 2) int.TryParse(partesHora[1], out minutos);
                if (partesHora.Length >= 3) int.TryParse(partesHora[2], out segundos);

                if (horas < 0 || horas > 23) horas = 0;
                if (minutos < 0 || minutos > 59) minutos = 0;
                if (segundos < 0 || segundos > 59) segundos = 0;

                return new DateTime(anio, mes, dia, horas, minutos, segundos);
            }
            catch
            {
                return null;
            }
        }

        private object ConvertirFechaParaSql(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return DBNull.Value;
            try
            {
                var fechaConvertida = ConvertirFechaForzada(valor);
                if (fechaConvertida.HasValue) return fechaConvertida.Value;
            }
            catch { }
            return DBNull.Value;
        }

        // ============================================
        // MÉTODOS AUXILIARES PARA OBTENER VALORES DEL EXCEL
        // ============================================

        private string ObtenerValorCelda(ExcelWorksheet worksheet, int row, string nombreColumna)
        {
            int colIndex = ObtenerIndiceColumna(worksheet, nombreColumna);
            if (colIndex <= 0) return "";
            return worksheet.Cells[row, colIndex].Text;
        }

        private decimal ObtenerValorDecimal(ExcelWorksheet worksheet, int row, string nombreColumna)
        {
            string valor = ObtenerValorCelda(worksheet, row, nombreColumna);
            if (string.IsNullOrEmpty(valor)) return 0;
            decimal resultado;
            if (decimal.TryParse(valor, out resultado)) return resultado;
            return 0;
        }

        private int ObtenerValorEntero(ExcelWorksheet worksheet, int row, string nombreColumna)
        {
            string valor = ObtenerValorCelda(worksheet, row, nombreColumna);
            if (string.IsNullOrEmpty(valor)) return 0;
            int resultado;
            if (int.TryParse(valor, out resultado)) return resultado;
            return 0;
        }

        private bool ObtenerValorBooleano(ExcelWorksheet worksheet, int row, string nombreColumna)
        {
            string valor = ObtenerValorCelda(worksheet, row, nombreColumna);
            if (string.IsNullOrEmpty(valor)) return false;
            return valor.ToLower() == "true" || valor == "1" || valor.ToLower() == "verdadero" || valor.ToLower() == "si" || valor.ToLower() == "yes";
        }

        private DateTime? ObtenerValorFecha(ExcelWorksheet worksheet, int row, string nombreColumna)
        {
            string valor = ObtenerValorCelda(worksheet, row, nombreColumna);
            if (string.IsNullOrEmpty(valor)) return null;
            return ConvertirFechaForzada(valor);
        }

        private int ObtenerIndiceColumna(ExcelWorksheet worksheet, string nombreColumna)
        {
            if (worksheet.Dimension == null) return -1;

            for (int col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                try
                {
                    var cellValue = worksheet.Cells[1, col].Text;
                    if (cellValue != null && cellValue.Equals(nombreColumna, StringComparison.OrdinalIgnoreCase))
                        return col;
                }
                catch { }
            }
            return -1;
        }

        private string SanitizarNombre(string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return nombre;

            return nombre
                .Replace(" ", "_")
                .Replace(".", "_")
                .Replace("-", "_")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
                .Replace("ñ", "n").Replace("Á", "A").Replace("É", "E").Replace("Í", "I").Replace("Ó", "O")
                .Replace("Ú", "U").Replace("Ñ", "N")
                .Replace("(", "_").Replace(")", "_").Replace("[", "_").Replace("]", "_")
                .Replace("{", "_").Replace("}", "_").Replace("'", "_").Replace("\"", "_")
                .Replace(":", "_").Replace(";", "_").Replace("!", "_").Replace("?", "_")
                .Replace("@", "_").Replace("#", "_").Replace("$", "_").Replace("%", "_")
                .Replace("&", "_").Replace("*", "_").Replace("+", "_").Replace("=", "_")
                .Replace("~", "_").Replace("`", "_").Replace("|", "_").Replace("<", "_")
                .Replace(">", "_").Replace(",", "_");
        }

        // ============================================
        // MÉTODO DE RESTAURACIÓN
        // ============================================

        public async Task<RestoreDetailedResult> RestaurarDesdeBackup(string rutaArchivo, List<string> tablasSeleccionadas)
        {
            var resultado = new RestoreDetailedResult();
            resultado.RegistrosPorTabla = new Dictionary<string, int>();
            resultado.OmitidosPorTabla = new Dictionary<string, int>();
            resultado.ErroresDetallados = new List<string>();
            resultado.RegistrosOmitidosDetalle = new List<string>();
            resultado.TablasEstado = new Dictionary<string, string>();

            try
            {
                using (var package = new ExcelPackage(new FileInfo(rutaArchivo)))
                {
                    DateTime fechaInicio = DateTime.Now.AddMonths(-1);
                    DateTime fechaFin = DateTime.Now;

                    var infoSheet = package.Workbook.Worksheets["Info_Backup"];
                    if (infoSheet != null)
                    {
                        var periodoText = infoSheet.Cells[2, 2]?.Text;
                        if (!string.IsNullOrEmpty(periodoText))
                        {
                            var partes = periodoText.Split(new[] { " - " }, StringSplitOptions.None);
                            if (partes.Length == 2)
                            {
                                if (DateTime.TryParse(partes[0].Trim(), out fechaInicio) &&
                                    DateTime.TryParse(partes[1].Trim(), out fechaFin))
                                {
                                }
                            }
                        }
                    }

                    var tablasOrdenadas = new List<string>();
                    if (tablasSeleccionadas.Contains("Usuarios"))
                        tablasOrdenadas.Insert(0, "Usuarios");
                    if (tablasSeleccionadas.Contains("Configuracion"))
                        tablasOrdenadas.Add("Configuracion");

                    var tablasDependientes = new List<string> {
                        "RegistrosAsistencia",
                        "RegistrosExcepcionales",
                        "AlertasHoras",
                        "NotificacionesAdmin",
                        "SolicitudesExcepcionales"
                    };

                    foreach (var tabla in tablasDependientes)
                    {
                        if (tablasSeleccionadas.Contains(tabla))
                            tablasOrdenadas.Add(tabla);
                    }

                    foreach (var tabla in tablasOrdenadas)
                    {
                        if (package.Workbook.Worksheets[tabla] != null)
                        {
                            resultado.TablasEstado[tabla] = "Procesando...";

                            var worksheet = package.Workbook.Worksheets[tabla];
                            if (worksheet.Dimension == null || worksheet.Dimension.Rows <= 1)
                            {
                                resultado.TablasEstado[tabla] = "Sin datos";
                                resultado.ErroresDetallados.Add($"La hoja '{tabla}' no contiene datos para restaurar");
                                continue;
                            }

                            await RestaurarTablaIndependienteAsync(package, tabla, fechaInicio, fechaFin, resultado);
                            resultado.TablasEstado[tabla] = "Completado";
                        }
                        else
                        {
                            resultado.TablasEstado[tabla] = "No encontrada";
                            resultado.ErroresDetallados.Add($"Tabla '{tabla}' no encontrada en el archivo");
                        }
                    }

                    resultado.Exito = true;
                    resultado.Mensaje = $"Restauración completada. {resultado.RegistrosInsertados} registros insertados, {resultado.RegistrosOmitidos} omitidos.";
                    resultado.TotalProcesados = resultado.RegistrosInsertados + resultado.RegistrosOmitidos + resultado.RegistrosConError;
                }
            }
            catch (Exception ex)
            {
                resultado.Exito = false;
                resultado.Mensaje = "Error en la restauración";
                resultado.Error = ex.Message;
                resultado.ErroresDetallados.Add($"Error general: {ex.Message}");
                if (ex.InnerException != null)
                {
                    resultado.ErroresDetallados.Add($"Inner Exception: {ex.InnerException.Message}");
                }
            }

            return resultado;
        }

        private async Task RestaurarTablaIndependienteAsync(ExcelPackage package, string nombreTabla, DateTime fechaInicio, DateTime fechaFin, RestoreDetailedResult resultado)
        {
            try
            {
                var worksheet = package.Workbook.Worksheets[nombreTabla];
                if (worksheet == null || worksheet.Dimension == null)
                {
                    resultado.ErroresDetallados.Add($"La hoja '{nombreTabla}' está vacía o no existe");
                    return;
                }

                if (worksheet.Dimension.Rows <= 1)
                {
                    resultado.ErroresDetallados.Add($"La hoja '{nombreTabla}' no contiene datos para restaurar");
                    return;
                }

                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            await RestaurarTablaConDetalleAsync(conn, transaction, worksheet, nombreTabla, fechaInicio, fechaFin, resultado);
                            transaction.Commit();
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            resultado.ErroresDetallados.Add($"Error en tabla '{nombreTabla}': {ex.Message}");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                resultado.ErroresDetallados.Add($"Error procesando tabla '{nombreTabla}': {ex.Message}");
                resultado.TablasEstado[nombreTabla] = "Error";
            }
        }

        private async Task RestaurarTablaConDetalleAsync(SqlConnection conn, SqlTransaction transaction, ExcelWorksheet worksheet, string nombreTabla, DateTime fechaInicio, DateTime fechaFin, RestoreDetailedResult resultado)
        {
            var columnasCompletas = new List<string>();
            var columnasNoIdentity = new List<string>();
            var esTablaIdentity = false;
            var esTablaConfiguracion = false;
            var esTablaRegistrosAsistencia = false;
            var esTablaUsuarios = false;
            var esTablaRegistrosExcepcionales = false;

            esTablaRegistrosAsistencia = nombreTabla.Equals("RegistrosAsistencia", StringComparison.OrdinalIgnoreCase);
            esTablaConfiguracion = nombreTabla.Equals("Configuracion", StringComparison.OrdinalIgnoreCase);
            esTablaUsuarios = nombreTabla.Equals("Usuarios", StringComparison.OrdinalIgnoreCase);
            esTablaRegistrosExcepcionales = nombreTabla.Equals("RegistrosExcepcionales", StringComparison.OrdinalIgnoreCase);

            for (int col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                var nombreColumna = worksheet.Cells[1, col].Text;
                if (!string.IsNullOrEmpty(nombreColumna))
                {
                    var lowerName = nombreColumna.ToLower();
                    columnasCompletas.Add(nombreColumna);

                    if (lowerName != "id" && lowerName != "fecha")
                    {
                        columnasNoIdentity.Add(nombreColumna);
                    }
                }
            }

            try
            {
                using (var cmdCheck = new SqlCommand($@"
                    SELECT COLUMNPROPERTY(OBJECT_ID('{nombreTabla}'), 'Id', 'IsIdentity') AS IsIdentity",
                    conn, transaction))
                {
                    var result = await cmdCheck.ExecuteScalarAsync();
                    esTablaIdentity = result != DBNull.Value && Convert.ToInt32(result) == 1;
                }
            }
            catch
            {
                esTablaIdentity = false;
            }

            var columnasInsert = esTablaIdentity ? columnasNoIdentity : columnasCompletas;

            if (esTablaRegistrosAsistencia)
            {
                var columnasPermitidas = new List<string> { "UsuarioId", "TipoRegistro", "FechaHora", "Comentario" };
                columnasInsert = columnasInsert
                    .Where(c => columnasPermitidas.Contains(c, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            if (columnasInsert.Count == 0)
            {
                resultado.ErroresDetallados.Add($"No hay columnas para insertar en '{nombreTabla}'");
                return;
            }

            if (esTablaConfiguracion)
            {
                await ActualizarConfiguracionAsync(conn, transaction, worksheet, resultado);
                return;
            }

            // Obtener usuarios existentes
            var usuariosExistentes = new Dictionary<string, int>();
            var usuariosIdsExistentes = new HashSet<int>();

            if (esTablaUsuarios || esTablaRegistrosAsistencia || esTablaRegistrosExcepcionales)
            {
                try
                {
                    string queryUsuarios = "SELECT Id, Email FROM Usuarios";
                    using (var cmdUsuarios = new SqlCommand(queryUsuarios, conn, transaction))
                    {
                        using (var reader = await cmdUsuarios.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var id = Convert.ToInt32(reader["Id"]);
                                var email = reader["Email"]?.ToString()?.ToLower()?.Trim() ?? "";
                                if (!string.IsNullOrEmpty(email))
                                {
                                    usuariosExistentes[email] = id;
                                    usuariosIdsExistentes.Add(id);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Obtener registros de asistencia existentes
            var registrosAsistenciaExistentes = new Dictionary<int, HashSet<DateTime>>();
            if (esTablaRegistrosAsistencia)
            {
                try
                {
                    string queryRegistros = @"
                        SELECT UsuarioId, Fecha 
                        FROM RegistrosAsistencia 
                        WHERE Fecha >= @FechaInicio AND Fecha <= @FechaFin";

                    using (var cmdRegistros = new SqlCommand(queryRegistros, conn, transaction))
                    {
                        cmdRegistros.Parameters.AddWithValue("@FechaInicio", fechaInicio);
                        cmdRegistros.Parameters.AddWithValue("@FechaFin", fechaFin);

                        using (var reader = await cmdRegistros.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var usuarioId = Convert.ToInt32(reader["UsuarioId"]);
                                var fecha = Convert.ToDateTime(reader["Fecha"]).Date;

                                if (!registrosAsistenciaExistentes.ContainsKey(usuarioId))
                                    registrosAsistenciaExistentes[usuarioId] = new HashSet<DateTime>();

                                registrosAsistenciaExistentes[usuarioId].Add(fecha);
                            }
                        }
                    }
                }
                catch { }
            }

            var idsExistentes = new HashSet<string>();
            if (!esTablaIdentity && !esTablaUsuarios && !esTablaRegistrosAsistencia && !esTablaConfiguracion && !esTablaRegistrosExcepcionales)
            {
                try
                {
                    string queryIds = $"SELECT Id FROM {nombreTabla}";
                    using (var cmdIds = new SqlCommand(queryIds, conn, transaction))
                    {
                        using (var reader = await cmdIds.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                idsExistentes.Add(reader[0]?.ToString() ?? "");
                            }
                        }
                    }
                }
                catch { }
            }

            int insertados = 0;
            int omitidos = 0;
            int errores = 0;
            var emailToNewId = new Dictionary<string, int>();

            int idColIndex = ObtenerIndiceColumna(worksheet, "Id");
            int emailColIndex = ObtenerIndiceColumna(worksheet, "Email");
            int usuarioIdColIndex = ObtenerIndiceColumna(worksheet, "UsuarioId");
            int fechaHoraColIndex = ObtenerIndiceColumna(worksheet, "FechaHora");
            int tipoRegistroColIndex = ObtenerIndiceColumna(worksheet, "TipoRegistro");
            int comentarioColIndex = ObtenerIndiceColumna(worksheet, "Comentario");

            for (int row = 2; row <= worksheet.Dimension.Rows; row++)
            {
                try
                {
                    // ============================================
                    // CASO 1: TABLA USUARIOS - Usando SP
                    // ============================================
                    if (esTablaUsuarios)
                    {
                        if (emailColIndex <= 0)
                        {
                            errores++;
                            resultado.ErroresDetallados.Add($"No se encontró columna Email en fila {row}");
                            continue;
                        }

                        var email = worksheet.Cells[row, emailColIndex].Text?.ToLower()?.Trim() ?? "";

                        if (!string.IsNullOrEmpty(email) && usuariosExistentes.ContainsKey(email))
                        {
                            omitidos++;
                            resultado.RegistrosOmitidosDetalle.Add($"Usuario con Email '{email}' ya existe (ID: {usuariosExistentes[email]}) - OMITIDO");
                            continue;
                        }

                        // Obtener todos los valores
                        string codigoQR = ObtenerValorCelda(worksheet, row, "CodigoQR");
                        string nombreCompleto = ObtenerValorCelda(worksheet, row, "NombreCompleto");
                        string passwordHash = ObtenerValorCelda(worksheet, row, "PasswordHash");
                        string rol = ObtenerValorCelda(worksheet, row, "Rol");
                        decimal horasMensualesObjetivo = ObtenerValorDecimal(worksheet, row, "HorasMensualesObjetivo");
                        bool activo = ObtenerValorBooleano(worksheet, row, "Activo");
                        DateTime? fechaRegistro = ObtenerValorFecha(worksheet, row, "FechaRegistro");
                        string celular = ObtenerValorCelda(worksheet, row, "Celular");
                        string cuentaAhorros = ObtenerValorCelda(worksheet, row, "CuentaAhorros");
                        string direccion = ObtenerValorCelda(worksheet, row, "Direccion");
                        string dni = ObtenerValorCelda(worksheet, row, "DNI");
                        string nombres = ObtenerValorCelda(worksheet, row, "Nombres");
                        string apellidoPaterno = ObtenerValorCelda(worksheet, row, "ApellidoPaterno");
                        string apellidoMaterno = ObtenerValorCelda(worksheet, row, "ApellidoMaterno");
                        string rolPago = ObtenerValorCelda(worksheet, row, "RolPago");
                        decimal tarifaHora = ObtenerValorDecimal(worksheet, row, "TarifaHora");
                        string banco = ObtenerValorCelda(worksheet, row, "Banco");
                        int conceptoPagoId = ObtenerValorEntero(worksheet, row, "ConceptoPagoId");
                        bool permisoMarcacionExcepcional = ObtenerValorBooleano(worksheet, row, "PermisoMarcacionExcepcional");
                        string tipoPermisoExcepcional = ObtenerValorCelda(worksheet, row, "TipoPermisoExcepcional");
                        DateTime? fechaInicioCampo = ObtenerValorFecha(worksheet, row, "FechaInicio");
                        int cicloActualId = ObtenerValorEntero(worksheet, row, "CicloActualId");
                        decimal horasExcedentesAcumuladas = ObtenerValorDecimal(worksheet, row, "HorasExcedentesAcumuladas");
                        DateTime? ultimaFechaProcesada = ObtenerValorFecha(worksheet, row, "UltimaFechaProcesada");
                        DateTime? ultimoCalculo = ObtenerValorFecha(worksheet, row, "UltimoCalculo");
                        int ciclosCompletados = ObtenerValorEntero(worksheet, row, "CiclosCompletados");
                        DateTime? ultimaFechaCorte = ObtenerValorFecha(worksheet, row, "UltimaFechaCorte");
                        DateTime? fechaInicio2 = ObtenerValorFecha(worksheet, row, "FechaInicio2");

                        try
                        {
                            using (var cmd = new SqlCommand("sp_InsertarUsuarioConValidacion", conn))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;
                                cmd.Transaction = transaction;

                                cmd.Parameters.AddWithValue("@CodigoQR", string.IsNullOrEmpty(codigoQR) ? DBNull.Value : (object)codigoQR);
                                cmd.Parameters.AddWithValue("@NombreCompleto", string.IsNullOrEmpty(nombreCompleto) ? DBNull.Value : (object)nombreCompleto);
                                cmd.Parameters.AddWithValue("@Email", email);
                                cmd.Parameters.AddWithValue("@PasswordHash", string.IsNullOrEmpty(passwordHash) ? DBNull.Value : (object)passwordHash);
                                cmd.Parameters.AddWithValue("@Rol", string.IsNullOrEmpty(rol) ? DBNull.Value : (object)rol);
                                cmd.Parameters.AddWithValue("@HorasMensualesObjetivo", horasMensualesObjetivo);
                                cmd.Parameters.AddWithValue("@Activo", activo);
                                cmd.Parameters.AddWithValue("@FechaRegistro", fechaRegistro.HasValue ? (object)fechaRegistro.Value : DBNull.Value);
                                cmd.Parameters.AddWithValue("@Celular", string.IsNullOrEmpty(celular) ? DBNull.Value : (object)celular);
                                cmd.Parameters.AddWithValue("@CuentaAhorros", string.IsNullOrEmpty(cuentaAhorros) ? DBNull.Value : (object)cuentaAhorros);
                                cmd.Parameters.AddWithValue("@Direccion", string.IsNullOrEmpty(direccion) ? DBNull.Value : (object)direccion);
                                cmd.Parameters.AddWithValue("@DNI", string.IsNullOrEmpty(dni) ? DBNull.Value : (object)dni);
                                cmd.Parameters.AddWithValue("@Nombres", string.IsNullOrEmpty(nombres) ? DBNull.Value : (object)nombres);
                                cmd.Parameters.AddWithValue("@ApellidoPaterno", string.IsNullOrEmpty(apellidoPaterno) ? DBNull.Value : (object)apellidoPaterno);
                                cmd.Parameters.AddWithValue("@ApellidoMaterno", string.IsNullOrEmpty(apellidoMaterno) ? DBNull.Value : (object)apellidoMaterno);
                                cmd.Parameters.AddWithValue("@RolPago", string.IsNullOrEmpty(rolPago) ? DBNull.Value : (object)rolPago);
                                cmd.Parameters.AddWithValue("@TarifaHora", tarifaHora);
                                cmd.Parameters.AddWithValue("@Banco", string.IsNullOrEmpty(banco) ? DBNull.Value : (object)banco);
                                cmd.Parameters.AddWithValue("@ConceptoPagoId", conceptoPagoId == 0 ? DBNull.Value : (object)conceptoPagoId);
                                cmd.Parameters.AddWithValue("@PermisoMarcacionExcepcional", permisoMarcacionExcepcional);
                                cmd.Parameters.AddWithValue("@TipoPermisoExcepcional", string.IsNullOrEmpty(tipoPermisoExcepcional) ? DBNull.Value : (object)tipoPermisoExcepcional);
                                cmd.Parameters.AddWithValue("@FechaInicio", fechaInicioCampo.HasValue ? (object)fechaInicioCampo.Value : DBNull.Value);
                                cmd.Parameters.AddWithValue("@CicloActualId", cicloActualId == 0 ? DBNull.Value : (object)cicloActualId);
                                cmd.Parameters.AddWithValue("@HorasExcedentesAcumuladas", horasExcedentesAcumuladas);
                                cmd.Parameters.AddWithValue("@UltimaFechaProcesada", ultimaFechaProcesada.HasValue ? (object)ultimaFechaProcesada.Value : DBNull.Value);
                                cmd.Parameters.AddWithValue("@UltimoCalculo", ultimoCalculo.HasValue ? (object)ultimoCalculo.Value : DBNull.Value);
                                cmd.Parameters.AddWithValue("@CiclosCompletados", ciclosCompletados);
                                cmd.Parameters.AddWithValue("@UltimaFechaCorte", ultimaFechaCorte.HasValue ? (object)ultimaFechaCorte.Value : DBNull.Value);
                                cmd.Parameters.AddWithValue("@FechaInicio2", fechaInicio2.HasValue ? (object)fechaInicio2.Value : DBNull.Value);

                                var outputId = new SqlParameter("@NuevoId", SqlDbType.Int) { Direction = ParameterDirection.Output };
                                cmd.Parameters.Add(outputId);

                                var outputMensaje = new SqlParameter("@Mensaje", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };
                                cmd.Parameters.Add(outputMensaje);

                                await cmd.ExecuteNonQueryAsync();

                                int nuevoId = Convert.ToInt32(outputId.Value);
                                string mensaje = outputMensaje.Value?.ToString() ?? "";

                                if (nuevoId > 0)
                                {
                                    emailToNewId[email] = nuevoId;
                                    usuariosExistentes[email] = nuevoId;
                                    usuariosIdsExistentes.Add(nuevoId);
                                    insertados++;
                                }
                                else
                                {
                                    errores++;
                                    resultado.ErroresDetallados.Add($"Error insertando fila {row}: {mensaje}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errores++;
                            resultado.ErroresDetallados.Add($"Error insertando fila {row} de {nombreTabla}: {ex.Message}");
                        }
                        continue;
                    }

                    // ============================================
                    // CASO 2: TABLA REGISTROSASISTENCIA - Usando SP
                    // ============================================
                    // ============================================
                    // CASO 2: TABLA REGISTROSASISTENCIA - CON VALIDACIÓN DE DUPLICADOS
                    // ============================================
                    if (esTablaRegistrosAsistencia)
                    {
                        if (usuarioIdColIndex <= 0 || fechaHoraColIndex <= 0)
                        {
                            errores++;
                            resultado.ErroresDetallados.Add($"Fila {row}: Faltan columnas UsuarioId o FechaHora");
                            continue;
                        }

                        var usuarioIdText = worksheet.Cells[row, usuarioIdColIndex].Text;
                        if (string.IsNullOrEmpty(usuarioIdText))
                        {
                            omitidos++;
                            resultado.RegistrosOmitidosDetalle.Add($"Fila {row}: UsuarioId vacío - OMITIDO");
                            continue;
                        }

                        if (!int.TryParse(usuarioIdText, out int usuarioIdExcel))
                        {
                            omitidos++;
                            resultado.RegistrosOmitidosDetalle.Add($"Fila {row}: UsuarioId inválido - OMITIDO");
                            continue;
                        }

                        bool usuarioExiste = usuariosIdsExistentes.Contains(usuarioIdExcel);

                        if (!usuarioExiste)
                        {
                            if (emailColIndex > 0)
                            {
                                var email = worksheet.Cells[row, emailColIndex].Text?.ToLower()?.Trim() ?? "";
                                if (!string.IsNullOrEmpty(email) && emailToNewId.ContainsKey(email))
                                {
                                    usuarioIdExcel = emailToNewId[email];
                                    usuarioExiste = true;
                                }
                            }
                        }

                        if (!usuarioExiste)
                        {
                            omitidos++;
                            resultado.RegistrosOmitidosDetalle.Add($"Fila {row}: Usuario ID {usuarioIdText} no existe - OMITIDO");
                            continue;
                        }

                        var fechaHoraText = worksheet.Cells[row, fechaHoraColIndex].Text;
                        if (string.IsNullOrEmpty(fechaHoraText))
                        {
                            omitidos++;
                            resultado.RegistrosOmitidosDetalle.Add($"Fila {row}: FechaHora vacío - OMITIDO");
                            continue;
                        }

                        DateTime? fechaHoraConvertida = ConvertirFechaForzada(fechaHoraText);
                        if (!fechaHoraConvertida.HasValue)
                        {
                            omitidos++;
                            resultado.RegistrosOmitidosDetalle.Add($"Fila {row}: FechaHora inválido - {fechaHoraText}");
                            continue;
                        }

                        DateTime fechaHoraCompleta = fechaHoraConvertida.Value;

                        string tipoRegistro = "";
                        if (tipoRegistroColIndex > 0)
                        {
                            tipoRegistro = worksheet.Cells[row, tipoRegistroColIndex].Text;
                        }

                        string comentario = "";
                        if (comentarioColIndex > 0)
                        {
                            comentario = worksheet.Cells[row, comentarioColIndex].Text;
                        }

                        // Si no hay TipoRegistro, intentar asignar un valor por defecto
                        if (string.IsNullOrEmpty(tipoRegistro))
                        {
                            // Si es el primer registro del día, podría ser Entrada, pero mejor omitir
                            tipoRegistro = "Entrada";
                        }

                        try
                        {
                            using (var cmd = new SqlCommand("sp_InsertarRegistroAsistencia", conn))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;
                                cmd.Transaction = transaction;

                                cmd.Parameters.AddWithValue("@UsuarioId", usuarioIdExcel);
                                cmd.Parameters.AddWithValue("@TipoRegistro", tipoRegistro);
                                cmd.Parameters.AddWithValue("@FechaHora", fechaHoraCompleta);
                                cmd.Parameters.AddWithValue("@Comentario", string.IsNullOrEmpty(comentario) ? DBNull.Value : (object)comentario);

                                var outputMensaje = new SqlParameter("@Mensaje", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };
                                cmd.Parameters.Add(outputMensaje);

                                await cmd.ExecuteNonQueryAsync();

                                string mensaje = outputMensaje.Value?.ToString() ?? "";

                                if (mensaje.Contains("DUPLICADO"))
                                {
                                    omitidos++;
                                    resultado.RegistrosOmitidosDetalle.Add($"Fila {row}: {mensaje}");
                                }
                                else if (mensaje.Contains("correctamente") || string.IsNullOrEmpty(mensaje))
                                {
                                    insertados++;
                                }
                                else
                                {
                                    omitidos++;
                                    resultado.RegistrosOmitidosDetalle.Add($"Fila {row}: {mensaje}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errores++;
                            resultado.ErroresDetallados.Add($"Error insertando fila {row} de {nombreTabla}: {ex.Message}");
                        }
                        continue;
                    }

                    // ============================================
                    // CASO 3: OTRAS TABLAS
                    // ============================================
                    if (idColIndex > 0 && !esTablaIdentity)
                    {
                        var idValor = worksheet.Cells[row, idColIndex].Text;
                        if (!string.IsNullOrEmpty(idValor) && idsExistentes.Contains(idValor))
                        {
                            omitidos++;
                            resultado.RegistrosOmitidosDetalle.Add($"Fila {row}: ID '{idValor}' ya existe en {nombreTabla} - OMITIDO");
                            continue;
                        }
                    }

                    var columnasStr2 = string.Join(", ", columnasInsert);
                    var valoresStr2 = string.Join(", ", columnasInsert.Select(c => "@" + SanitizarNombre(c)));
                    var insertQuery2 = $"INSERT INTO {nombreTabla} ({columnasStr2}) VALUES ({valoresStr2})";

                    using (var cmdInsert = new SqlCommand(insertQuery2, conn, transaction))
                    {
                        foreach (var columna in columnasInsert)
                        {
                            int colIndex = ObtenerIndiceColumna(worksheet, columna);
                            if (colIndex <= 0) continue;

                            var valor = worksheet.Cells[row, colIndex].Text;
                            var paramName = "@" + SanitizarNombre(columna);

                            if (columna.ToLower().Contains("fecha") || columna.ToLower().Contains("date"))
                            {
                                var fechaConvertida = ConvertirFechaForzada(valor);
                                if (fechaConvertida.HasValue)
                                {
                                    cmdInsert.Parameters.AddWithValue(paramName, fechaConvertida.Value);
                                }
                                else
                                {
                                    cmdInsert.Parameters.AddWithValue(paramName, DBNull.Value);
                                }
                            }
                            else
                            {
                                cmdInsert.Parameters.AddWithValue(paramName,
                                    string.IsNullOrEmpty(valor) ? DBNull.Value : (object)valor);
                            }
                        }

                        int rowsAffected = await cmdInsert.ExecuteNonQueryAsync();
                        if (rowsAffected > 0)
                        {
                            insertados++;
                            if (idColIndex > 0)
                            {
                                var idValor = worksheet.Cells[row, idColIndex].Text;
                                if (!string.IsNullOrEmpty(idValor))
                                    idsExistentes.Add(idValor);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errores++;
                    resultado.ErroresDetallados.Add($"Error en fila {row} de {nombreTabla}: {ex.Message}");
                }
            }

            resultado.RegistrosInsertados += insertados;
            resultado.RegistrosOmitidos += omitidos;
            resultado.RegistrosConError += errores;
            resultado.TotalDuplicados += omitidos;

            if (!resultado.RegistrosPorTabla.ContainsKey(nombreTabla))
                resultado.RegistrosPorTabla[nombreTabla] = 0;
            resultado.RegistrosPorTabla[nombreTabla] += insertados;

            if (!resultado.OmitidosPorTabla.ContainsKey(nombreTabla))
                resultado.OmitidosPorTabla[nombreTabla] = 0;
            resultado.OmitidosPorTabla[nombreTabla] += omitidos;
        }

        // ============================================
        // ACTUALIZAR CONFIGURACIÓN
        // ============================================

        private async Task ActualizarConfiguracionAsync(SqlConnection conn, SqlTransaction transaction, ExcelWorksheet worksheet, RestoreDetailedResult resultado)
        {
            try
            {
                string horaEntrada = ObtenerValorCelda(worksheet, 2, "HoraEntrada");
                string horaSalida = ObtenerValorCelda(worksheet, 2, "HoraSalida");
                string horaAlmuerzoInicio = ObtenerValorCelda(worksheet, 2, "HoraAlmuerzoInicio");
                string horaAlmuerzoFin = ObtenerValorCelda(worksheet, 2, "HoraAlmuerzoFin");
                int toleranciaMinutos = ObtenerValorEntero(worksheet, 2, "ToleranciaMinutos");

                using (var cmd = new SqlCommand("sp_InsertarConfiguracion", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Transaction = transaction;

                    TimeSpan horaEntradaTs = TimeSpan.TryParse(horaEntrada, out var he) ? he : TimeSpan.Zero;
                    TimeSpan horaSalidaTs = TimeSpan.TryParse(horaSalida, out var hs) ? hs : TimeSpan.Zero;
                    TimeSpan horaAlmuerzoInicioTs = TimeSpan.TryParse(horaAlmuerzoInicio, out var hai) ? hai : TimeSpan.Zero;
                    TimeSpan horaAlmuerzoFinTs = TimeSpan.TryParse(horaAlmuerzoFin, out var haf) ? haf : TimeSpan.Zero;

                    cmd.Parameters.AddWithValue("@HoraEntrada", horaEntradaTs);
                    cmd.Parameters.AddWithValue("@HoraSalida", horaSalidaTs);
                    cmd.Parameters.AddWithValue("@HoraAlmuerzoInicio", horaAlmuerzoInicioTs);
                    cmd.Parameters.AddWithValue("@HoraAlmuerzoFin", horaAlmuerzoFinTs);
                    cmd.Parameters.AddWithValue("@ToleranciaMinutos", toleranciaMinutos);

                    var outputMensaje = new SqlParameter("@Mensaje", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };
                    cmd.Parameters.Add(outputMensaje);

                    await cmd.ExecuteNonQueryAsync();

                    string mensaje = outputMensaje.Value?.ToString() ?? "";
                    if (mensaje.Contains("correctamente"))
                    {
                        if (!resultado.RegistrosPorTabla.ContainsKey("Configuracion"))
                            resultado.RegistrosPorTabla["Configuracion"] = 0;
                        resultado.RegistrosPorTabla["Configuracion"] += 1;
                        resultado.RegistrosInsertados += 1;
                    }
                }
            }
            catch (Exception ex)
            {
                resultado.ErroresDetallados.Add($"Error en Configuracion: {ex.Message}");
            }
        }

        // ============================================
        // OTROS MÉTODOS
        // ============================================

        public async Task<int> RegistrarEventoBackupAsync(int usuarioId, string tipoEvento, string descripcion, string resultado)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                using (SqlCommand cmd = new SqlCommand("sp_RegistrarEventoBackup", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@UsuarioId", usuarioId);
                    cmd.Parameters.AddWithValue("@TipoEvento", tipoEvento);
                    cmd.Parameters.AddWithValue("@Descripcion", descripcion);
                    cmd.Parameters.AddWithValue("@Resultado", resultado);

                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();

                    return 1;
                }
            }
        }

        public async Task<string> GenerarBackupMensualCompletoAsync(int anio, int mes)
        {
            var nombreCarpeta = $"BackAsistencia-{anio:D4}-{mes:D2}";
            var rutaCarpeta = Path.Combine(_customBackupPath, nombreCarpeta);

            if (!Directory.Exists(rutaCarpeta))
                Directory.CreateDirectory(rutaCarpeta);

            var fechaInicio = new DateTime(anio, mes, 1);
            var fechaFin = fechaInicio.AddMonths(1).AddDays(-1);

            var nombreArchivo = $"Backup_Completo_{anio:D4}_{mes:D2}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var rutaArchivo = Path.Combine(rutaCarpeta, nombreArchivo);

            using (var package = new ExcelPackage())
            {
                var dtUsuarios = await Task.Run(() => ObtenerTabla("SELECT * FROM Usuarios"));
                AgregarHojaExcel(package, "Usuarios", dtUsuarios);

                string queryRegistros = @"
                    SELECT * FROM RegistrosAsistencia 
                    WHERE Fecha >= @FechaInicio 
                    AND Fecha <= @FechaFin";

                var parameters = new SqlParameter[]
                {
                    new SqlParameter("@FechaInicio", fechaInicio),
                    new SqlParameter("@FechaFin", fechaFin)
                };

                var dtRegistros = await Task.Run(() => ObtenerTabla(queryRegistros, parameters));
                AgregarHojaExcel(package, "RegistrosAsistencia", dtRegistros);

                string queryExcepcionales = @"
                    SELECT * FROM RegistrosExcepcionales 
                    WHERE FechaRegistro >= @FechaInicio 
                    AND FechaRegistro <= @FechaFin";

                var dtExcepcionales = await Task.Run(() => ObtenerTabla(queryExcepcionales, parameters));
                AgregarHojaExcel(package, "RegistrosExcepcionales", dtExcepcionales);

                var dtConfiguracion = await Task.Run(() => ObtenerTabla("SELECT * FROM Configuracion"));
                AgregarHojaExcel(package, "Configuracion", dtConfiguracion);

                string queryAlertas = @"
                    SELECT * FROM AlertasHoras 
                    WHERE FechaAlerta >= @FechaInicio 
                    AND FechaAlerta <= @FechaFin";

                var dtAlertas = await Task.Run(() => ObtenerTabla(queryAlertas, parameters));
                AgregarHojaExcel(package, "AlertasHoras", dtAlertas);

                string queryNotificaciones = @"
                    SELECT * FROM NotificacionesAdmin 
                    WHERE FechaCreacion >= @FechaInicio 
                    AND FechaCreacion <= @FechaFin";

                var dtNotificaciones = await Task.Run(() => ObtenerTabla(queryNotificaciones, parameters));
                AgregarHojaExcel(package, "NotificacionesAdmin", dtNotificaciones);

                string querySolicitudes = @"
                    SELECT * FROM SolicitudesExcepcionales 
                    WHERE FechaSolicitud >= @FechaInicio 
                    AND FechaSolicitud <= @FechaFin";

                var dtSolicitudes = await Task.Run(() => ObtenerTabla(querySolicitudes, parameters));
                AgregarHojaExcel(package, "SolicitudesExcepcionales", dtSolicitudes);

                var hojaInfo = package.Workbook.Worksheets.Add("Info_Backup");
                hojaInfo.Cells[1, 1].Value = "Fecha Backup:";
                hojaInfo.Cells[1, 2].Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                hojaInfo.Cells[2, 1].Value = "Período:";
                hojaInfo.Cells[2, 2].Value = $"{fechaInicio:dd/MM/yyyy} - {fechaFin:dd/MM/yyyy}";
                hojaInfo.Cells[3, 1].Value = "Tipo Backup:";
                hojaInfo.Cells[3, 2].Value = "AUTOMÁTICO - MES COMPLETO";
                hojaInfo.Cells[4, 1].Value = "Días del período:";
                hojaInfo.Cells[4, 2].Value = (fechaFin - fechaInicio).Days + 1;
                hojaInfo.Cells[5, 1].Value = "Total Registros Asistencia:";
                hojaInfo.Cells[5, 2].Value = dtRegistros.Rows.Count;
                hojaInfo.Cells[6, 1].Value = "Total Usuarios:";
                hojaInfo.Cells[6, 2].Value = dtUsuarios.Rows.Count;
                hojaInfo.Cells.AutoFitColumns();

                package.SaveAs(new FileInfo(rutaArchivo));
            }

            await RegistrarEventoBackupAsync(0, "BACKUP_AUTOMATICO",
                $"Backup automático del mes {mes:00}/{anio} generado en {_customBackupPath}", "EXITOSO");

            return rutaArchivo;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                }
                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    // ============================================
    // CLASES AUXILIARES
    // ============================================

    public class BackupInfo
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string RutaCarpeta { get; set; }
        public List<string> Archivos { get; set; }
        public DateTime FechaCreacion { get; set; }

        public string NombreMes => new DateTime(Anio, Mes, 1).ToString("MMMM", new System.Globalization.CultureInfo("es-ES"));
    }

    public class RestoreResult
    {
        public bool Exito { get; set; }
        public string Mensaje { get; set; }
        public string Error { get; set; }
        public int RegistrosInsertados { get; set; }
    }

    public class RestoreDetailedResult : RestoreResult
    {
        public int RegistrosOmitidos { get; set; }
        public int RegistrosConError { get; set; }
        public Dictionary<string, int> RegistrosPorTabla { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> OmitidosPorTabla { get; set; } = new Dictionary<string, int>();
        public List<string> ErroresDetallados { get; set; } = new List<string>();
        public List<string> RegistrosOmitidosDetalle { get; set; } = new List<string>();
        public int TotalProcesados { get; set; }
        public int TotalDuplicados { get; set; }
        public Dictionary<string, string> TablasEstado { get; set; } = new Dictionary<string, string>();
    }
}