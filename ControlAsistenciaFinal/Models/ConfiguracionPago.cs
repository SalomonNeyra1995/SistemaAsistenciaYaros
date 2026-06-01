using System;
using System.ComponentModel.DataAnnotations;

namespace ControlAsistenciaFinal.Models
{
    public class ConfiguracionPago
    {
        public int Id { get; set; }

        [Display(Name = "Concepto de Pago")]
        public string ConceptoPago { get; set; }

        [Display(Name = "Tipo")]
        public string Tipo { get; set; }

        [Display(Name = "Monto Base")]
        [DataType(DataType.Currency)]
        public decimal MontoBase { get; set; }

        [Display(Name = "Días Base")]
        public int DiasBase { get; set; }

        [Display(Name = "Valor Día")]
        [DataType(DataType.Currency)]
        public decimal ValorDia { get; set; }

        [Display(Name = "Valor Hora")]
        [DataType(DataType.Currency)]
        public decimal ValorHora { get; set; }

        [Display(Name = "Valor Minuto")]
        [DataType(DataType.Currency)]
        public decimal ValorMinuto { get; set; }

        [Display(Name = "Horas Diarias")]
        public int HorasDiarias { get; set; }

        [Display(Name = "Horas Semana")]
        public int? HorasSemana { get; set; }

        [Display(Name = "Horas Sábado")]
        public int? HorasSabado { get; set; }

        [Display(Name = "Horas Mensuales")]
        public int? HorasMensuales { get; set; }

        public bool Activo { get; set; }

        public DateTime FechaActualizacion { get; set; }
    }
}