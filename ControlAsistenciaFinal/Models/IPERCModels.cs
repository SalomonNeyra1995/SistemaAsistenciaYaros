using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ControlAsistenciaFinal.Models
{
    // ============================================
    // MODELO PRINCIPAL IPERC
    // ============================================
    public class IPERC_Matriz
    {
        public int Id { get; set; }

        [Display(Name = "Código")]
        public string Codigo { get; set; }

        [Required(ErrorMessage = "Seleccione el puesto")]
        [Display(Name = "Puesto de Trabajo")]
        public int PuestoId { get; set; }

        [Display(Name = "Puesto")]
        public string PuestoNombre { get; set; }

        [Required(ErrorMessage = "Ingrese la actividad")]
        [Display(Name = "Actividad")]
        public string Actividad { get; set; }

        [Required(ErrorMessage = "Ingrese la tarea")]
        [Display(Name = "Tarea")]
        public string Tarea { get; set; }

        [Required(ErrorMessage = "Seleccione el tipo de peligro")]
        [Display(Name = "Tipo de Peligro")]
        public int TipoPeligroId { get; set; }

        [Display(Name = "Tipo de Peligro")]
        public string TipoPeligroNombre { get; set; }

        [Display(Name = "Base Legal")]
        public string BaseLegal { get; set; }

        [Required(ErrorMessage = "Ingrese el peligro")]
        [Display(Name = "Peligro")]
        public string Peligro { get; set; }

        [Required(ErrorMessage = "Ingrese el riesgo")]
        [Display(Name = "Riesgo")]
        public string Riesgo { get; set; }

        [Display(Name = "Consecuencia")]
        public string Consecuencia { get; set; }

        [Display(Name = "¿Es rutinaria?")]
        public bool EsRutinaria { get; set; } = true;

        [Display(Name = "¿Es emergencia?")]
        public bool EsEmergencia { get; set; } = false;

        [Display(Name = "Grupo Vulnerable")]
        public string GrupoVulnerable { get; set; }

        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaActualizacion { get; set; }
        public int? CreadoPor { get; set; }
        public bool Activo { get; set; } = true;

        // Evaluación (relación uno a uno)
        public IPERC_Evaluacion Evaluacion { get; set; }
    }

    // ============================================
    // MODELO DE EVALUACIÓN
    // ============================================
    public class IPERC_Evaluacion
    {
        public int Id { get; set; }
        public int IPERCId { get; set; }
        public string Tipo { get; set; } = "INICIAL";

        [Display(Name = "Personas Expuestas")]
        [Range(1, 4, ErrorMessage = "Seleccione un valor entre 1 y 4")]
        public int PersonasExpuestas { get; set; }

        [Display(Name = "Procedimientos Existentes")]
        [Range(1, 4, ErrorMessage = "Seleccione un valor entre 1 y 4")]
        public int ProcedimientosExistentes { get; set; }

        [Display(Name = "Capacitación")]
        [Range(1, 4, ErrorMessage = "Seleccione un valor entre 1 y 4")]
        public int Capacitacion { get; set; }

        [Display(Name = "Exposición al Riesgo")]
        [Range(1, 4, ErrorMessage = "Seleccione un valor entre 1 y 4")]
        public int ExposicionRiesgo { get; set; }

        public int Probabilidad { get; set; }

        [Display(Name = "Severidad")]
        [Range(1, 4, ErrorMessage = "Seleccione un valor entre 1 y 4")]
        public int Severidad { get; set; }

        public int NivelRiesgo { get; set; }
        public bool Significancia { get; set; }
        public DateTime FechaEvaluacion { get; set; }

        // Controles
        public List<IPERC_Control> Controles { get; set; }
    }

    // ============================================
    // MODELO DE CONTROL
    // ============================================
    public class IPERC_Control
    {
        public int Id { get; set; }
        public int EvaluacionId { get; set; }

        [Display(Name = "Tipo de Control")]
        public string TipoControl { get; set; }

        [Display(Name = "Descripción")]
        public string Descripcion { get; set; }

        public int Orden { get; set; }
    }

    // ============================================
    // MODELOS AUXILIARES
    // ============================================
    public class TipoPeligro
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public bool Activo { get; set; }
    }

    public class PuestoTrabajo
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public bool Activo { get; set; }
    }

    public class FactorEvaluacion
    {
        public int Id { get; set; }
        public string Factor { get; set; }
        public int Valor { get; set; }
        public string Descripcion { get; set; }
    }

    // ============================================
    // VIEWMODELS
    // ============================================
    public class IPERCViewModel
    {
        public IPERC_Matriz Matriz { get; set; }
        public IPERC_Evaluacion Evaluacion { get; set; }
        public List<IPERC_Control> Controles { get; set; }

        // Listas para selects
        public List<TipoPeligro> TiposPeligro { get; set; }
        public List<PuestoTrabajo> Puestos { get; set; }
        public List<FactorEvaluacion> FactoresPersonas { get; set; }
        public List<FactorEvaluacion> FactoresProcedimientos { get; set; }
        public List<FactorEvaluacion> FactoresCapacitacion { get; set; }
        public List<FactorEvaluacion> FactoresExposicion { get; set; }
        public List<FactorEvaluacion> FactoresSeveridad { get; set; }

        // Resultados de cálculo
        public string ColorNivelRiesgo
        {
            get
            {
                if (Evaluacion == null) return "";
                int nivel = Evaluacion.NivelRiesgo;
                if (nivel <= 4) return "bg-success";
                if (nivel <= 9) return "bg-warning";
                if (nivel <= 16) return "bg-orange";
                return "bg-danger";
            }
        }

        public string TextoSignificancia
        {
            get
            {
                return Evaluacion?.Significancia == true ? "SÍ" : "NO";
            }
        }
    }

    // ============================================
    // REPORTE VIEWMODEL
    // ============================================
    public class IPERCReporteViewModel
    {
        public int TotalRegistros { get; set; }
        public int RiesgosAltos { get; set; }
        public int RiesgosMuyAltos { get; set; }
        public int RiesgosMedios { get; set; }
        public int RiesgosBajos { get; set; }
        public List<ResumenPorPuesto> ResumenPorPuesto { get; set; }
        public List<ResumenPorPeligro> ResumenPorPeligro { get; set; }
        public List<IPERC_Matriz> Detalle { get; set; }
    }

    public class ResumenPorPuesto
    {
        public string Puesto { get; set; }
        public int Total { get; set; }
        public int Altos { get; set; }
    }

    public class ResumenPorPeligro
    {
        public string Peligro { get; set; }
        public int Total { get; set; }
        public int Porcentaje { get; set; }
    }
}