using ControlAsistenciaFinal.App_Start;
using Hangfire;
using System.Web.Mvc;
using System.Web.Routing;

namespace ControlAsistenciaFinal
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            // Inicializar tareas programadas (Timer)
            new ScheduledTasks();

            // ============================================
            // CONFIGURAR HANGFIRE PARA BACKUP AUTOMÁTICO
            // ============================================
            HangfireConfig.Configure();

            // Programar trabajos recurrentes
            HangfireConfig.ScheduleJobs();
        }

        protected void Application_End()
        {
            // Limpiar recursos si es necesario
            // Hangfire se detiene automáticamente
        }
    }
}