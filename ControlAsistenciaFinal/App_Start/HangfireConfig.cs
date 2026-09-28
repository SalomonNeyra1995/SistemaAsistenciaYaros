using Hangfire;
using System;
using System.IO;
using ControlAsistenciaFinal.Services;
using System.Threading;

namespace ControlAsistenciaFinal.App_Start
{
    public class HangfireConfig
    {
        public static void Configure()
        {
            GlobalConfiguration.Configuration
                .UseSqlServerStorage("DefaultConnection");
        }

        public static void ScheduleJobs()
        {
            // Programar backup para el día 11 de cada mes a las 12:00 PM (mediodía)
            RecurringJob.AddOrUpdate(
                "backup-automatico-mensual",
                () => EjecutarBackupMensual(),
                "45 15 11 * *",
                TimeZoneInfo.Local
            );

            // Ejecutar cada FIN DE MES (último día del mes) a las 10 PM (22:00)
            RecurringJob.AddOrUpdate(
                "backup-fin-de-mes",
                () => EjecutarBackupFinDeMes(),
                "0 22 L * *",  // L = Last day of month
                TimeZoneInfo.Local
            );

            // También agregar trabajo de prueba que se ejecute cada hora para verificar
            RecurringJob.AddOrUpdate(
                "backup-verificacion-horaria",
                () => EjecutarBackupPrueba(),
                "0 * * * *", // Cada hora en punto
                TimeZoneInfo.Local
            );

            System.Diagnostics.Debug.WriteLine("Hangfire: Trabajos programados");
        }

        public static void EjecutarBackupPrueba()
        {
            var logPath = @"C:\Users\HP\Desktop\Escritorio\DocGuiaYamiflo\backup_log.txt";
            try
            {
                File.AppendAllText(logPath, $"{DateTime.Now}: Verificación horaria - Hangfire activo\r\n");
            }
            catch { }
        }

        public static void EjecutarBackupMensual()
        {
            var logPath = @"C:\Users\HP\Desktop\Escritorio\DocGuiaYamiflo\backup_log.txt";
            var maxIntentos = 3;

            try
            {
                // Escribir log con retry
                EscribirLogConRetry(logPath, $"{DateTime.Now}: INICIANDO BACKUP AUTOMÁTICO");

                // IMPORTANTE: Usar using correctamente y asegurar que se cierra todo
                using (var backupService = new BackupService())
                {
                    var fechaActual = DateTime.Now;

                    // Ejecutar la tarea asíncrona de manera segura
                    var tarea = backupService.GenerarBackupMensualCompletoAsync(fechaActual.Year, fechaActual.Month);
                    tarea.Wait(); // O mejor usa tarea.GetAwaiter().GetResult()
                    var ruta = tarea.Result;

                    EscribirLogConRetry(logPath, $"{DateTime.Now}: Backup generado en: {ruta}");
                }

                EscribirLogConRetry(logPath, $"{DateTime.Now}: BACKUP COMPLETADO");
            }
            catch (Exception ex)
            {
                // Escribir el error con retry
                EscribirLogConRetry(logPath, $"{DateTime.Now}: ERROR: {ex.Message}");
                EscribirLogConRetry(logPath, $"{DateTime.Now}: STACK: {ex.StackTrace}");

                // Opcional: También escribir en un archivo de error separado
                try
                {
                    var errorPath = Path.Combine(Path.GetDirectoryName(logPath), "backup_error.log");
                    File.AppendAllText(errorPath, $"{DateTime.Now}: ERROR EN BACKUP: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}");
                }
                catch { /* Ignorar errores de log */ }
            }
        }

        // Método auxiliar para escribir logs con reintentos
        private static void EscribirLogConRetry(string logPath, string mensaje, int maxIntentos = 3)
        {
            for (int intento = 0; intento < maxIntentos; intento++)
            {
                try
                {
                    // Usar FileShare.ReadWrite para permitir acceso compartido
                    using (var fileStream = new FileStream(logPath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite))
                    using (var writer = new StreamWriter(fileStream))
                    {
                        writer.WriteLine(mensaje);
                        writer.Flush();
                    }
                    return; // Si funcionó, salir
                }
                catch (IOException) when (intento < maxIntentos - 1)
                {
                    // Esperar antes de reintentar (con backoff exponencial)
                    Thread.Sleep(100 * (intento + 1));
                }
                catch (Exception)
                {
                    // Si hay otro tipo de error, no reintentar
                    break;
                }
            }
        }

        // NUEVO MÉTODO: Ejecutar backup de fin de mes
        public static void EjecutarBackupFinDeMes()
        {
            var logPath = @"C:\Users\HP\Desktop\Escritorio\DocGuiaYamiflo\backup_log.txt";

            try
            {
                File.AppendAllText(logPath, $"{DateTime.Now}: ========================================\r\n");
                File.AppendAllText(logPath, $"{DateTime.Now}: INICIANDO BACKUP DE FIN DE MES\r\n");
                File.AppendAllText(logPath, $"{DateTime.Now}: Fecha actual: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\r\n");

                // Calcular el mes actual
                var fechaActual = DateTime.Now;

                // Aquí puedes llamar a tu servicio de backup o lógica específica de fin de mes
                using (var backupService = new BackupService())
                {
                    // Backup del mes actual
                    var tarea = backupService.GenerarBackupMensualCompletoAsync(fechaActual.Year, fechaActual.Month);
                    tarea.Wait();
                    var ruta = tarea.Result;

                    File.AppendAllText(logPath, $"{DateTime.Now}: Backup de fin de mes generado en: {ruta}\r\n");
                }

                // Opcional: Limpiar backups antiguos (más de 3 meses)
                // LimpiarBackupsAntiguos();

                File.AppendAllText(logPath, $"{DateTime.Now}: BACKUP DE FIN DE MES COMPLETADO\r\n");
                File.AppendAllText(logPath, $"{DateTime.Now}: ========================================\r\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"{DateTime.Now}: ERROR EN BACKUP FIN DE MES: {ex.Message}\r\n");
                File.AppendAllText(logPath, $"{DateTime.Now}: STACK: {ex.StackTrace}\r\n");
            }
        }

        // Método opcional para limpiar backups antiguos
        private static void LimpiarBackupsAntiguos()
        {
            try
            {
                var backupPath = @"C:\Users\HP\Desktop\Escritorio\DocGuiaYamiflo\Backups\";
                if (Directory.Exists(backupPath))
                {
                    var archivos = Directory.GetFiles(backupPath, "*.bak");
                    var fechaLimite = DateTime.Now.AddMonths(-3);

                    foreach (var archivo in archivos)
                    {
                        var fechaArchivo = File.GetCreationTime(archivo);
                        if (fechaArchivo < fechaLimite)
                        {
                            File.Delete(archivo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var logPath = @"C:\Users\HP\Desktop\Escritorio\DocGuiaYamiflo\backup_log.txt";
                File.AppendAllText(logPath, $"{DateTime.Now}: Error limpiando backups antiguos: {ex.Message}\r\n");
            }
        }
    }
}