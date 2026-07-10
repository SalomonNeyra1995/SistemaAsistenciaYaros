using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Owin;
using Owin;
using System;
using System.Web;

[assembly: OwinStartup(typeof(ControlAsistenciaFinal.Startup))]

namespace ControlAsistenciaFinal
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            try
            {
                var connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

                GlobalConfiguration.Configuration
                    .UseSqlServerStorage(connectionString);

                app.UseHangfireDashboard("/hangfire");
                app.UseHangfireServer();

                // Registrar en archivo de log para depuración
                var logPath = HttpContext.Current.Server.MapPath("~/hangfire_startup_log.txt");
                System.IO.File.WriteAllText(logPath, $"{DateTime.Now}: Hangfire iniciado correctamente");
            }
            catch (Exception ex)
            {
                var logPath = HttpContext.Current.Server.MapPath("~/hangfire_error_log.txt");
                System.IO.File.WriteAllText(logPath, $"{DateTime.Now}: ERROR: {ex.Message}\r\n{ex.StackTrace}");
            }
        }
    }
}