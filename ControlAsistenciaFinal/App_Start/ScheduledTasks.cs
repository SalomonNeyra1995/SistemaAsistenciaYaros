using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using ControlAsistenciaFinal.Services;

namespace ControlAsistenciaFinal.App_Start
{
    public class ScheduledTasks : IRegisteredObject
    {
        private Timer _timer;
        private readonly object _lock = new object();

        public ScheduledTasks()
        {
            HostingEnvironment.RegisterObject(this);
            ScheduleNextRun();
        }

        private void ScheduleNextRun()
        {
            var now = DateTime.Now;

            // Calcular la próxima ejecución: día 11 de cada mes a las 11:07 AM
            var nextRun = new DateTime(now.Year, now.Month, 11, 11, 7, 0);

            if (nextRun <= now)
            {
                nextRun = nextRun.AddMonths(1);
            }

            var delay = nextRun - now;

            lock (_lock)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                }

                _timer = new Timer(ExecuteBackup, null, delay, TimeSpan.FromDays(30));
            }

            System.Diagnostics.Debug.WriteLine($"Próximo backup (Timer) programado para: {nextRun:yyyy-MM-dd HH:mm:ss}");
        }

        private void ExecuteBackup(object state)
        {
            Task.Run(async () =>
            {
                try
                {
                    var fechaActual = DateTime.Now;
                    var mesAnterior = fechaActual.AddMonths(-1);

                    using (var backupService = new BackupService())
                    {
                        var ruta = await backupService.GenerarBackupMensualCompletoAsync(mesAnterior.Year, mesAnterior.Month);
                        System.Diagnostics.Debug.WriteLine($"Backup automático (Timer) generado: {ruta}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error en backup automático (Timer): {ex.Message}");
                }
                finally
                {
                    ScheduleNextRun();
                }
            });
        }

        public void Stop(bool immediate)
        {
            lock (_lock)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
            }
            HostingEnvironment.UnregisterObject(this);
        }
    }
}