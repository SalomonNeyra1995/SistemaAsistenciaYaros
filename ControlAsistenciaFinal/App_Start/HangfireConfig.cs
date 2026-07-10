using Hangfire;
using System;
using System.IO;
using ControlAsistenciaFinal.Services;

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

            try
            {
                File.AppendAllText(logPath, $"{DateTime.Now}: INICIANDO BACKUP AUTOMÁTICO\r\n");

                using (var backupService = new BackupService())
                {
                    var fechaActual = DateTime.Now;
                    var tarea = backupService.GenerarBackupMensualCompletoAsync(fechaActual.Year, fechaActual.Month);
                    tarea.Wait();
                    var ruta = tarea.Result;

                    File.AppendAllText(logPath, $"{DateTime.Now}: Backup generado en: {ruta}\r\n");
                }

                File.AppendAllText(logPath, $"{DateTime.Now}: BACKUP COMPLETADO\r\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"{DateTime.Now}: ERROR: {ex.Message}\r\n");
                File.AppendAllText(logPath, $"{DateTime.Now}: STACK: {ex.StackTrace}\r\n");
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