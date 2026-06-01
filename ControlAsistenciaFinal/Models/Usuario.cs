using System;
using System.ComponentModel.DataAnnotations;

namespace ControlAsistenciaFinal.Models
{
    public class Usuario
    {
        public int Id { get; set; }
        public Guid CodigoQR { get; set; }

        [Required(ErrorMessage = "El nombre es requerido")]
        [Display(Name = "Nombre Completo")]
        public string NombreCompleto { get; set; }

        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "Email inválido")]
        [Display(Name = "Correo Electrónico")]
        public string Email { get; set; }

        public string PasswordHash { get; set; }

        [Display(Name = "Contraseña")]
        public string Password { get; set; }

        [Display(Name = "Rol")]
        public string Rol { get; set; }

        [Display(Name = "Horas Mensuales Objetivo")]
        public decimal HorasMensualesObjetivo { get; set; }

        [Display(Name = "Activo")]
        public bool Activo { get; set; }

        public DateTime FechaRegistro { get; set; }

        // NUEVOS CAMPOS
        [Display(Name = "DNI")]
        [StringLength(8, MinimumLength = 8, ErrorMessage = "El DNI debe tener exactamente 8 dígitos")]
        public string DNI { get; set; }

        [Display(Name = "Nombres")]
        public string Nombres { get; set; }

        [Display(Name = "Apellido Paterno")]
        public string ApellidoPaterno { get; set; }

        [Display(Name = "Apellido Materno")]
        public string ApellidoMaterno { get; set; }

        [Display(Name = "Celular")]
        [Phone(ErrorMessage = "Número de celular inválido")]
        [StringLength(9, MinimumLength = 9, ErrorMessage = "El celular debe tener 9 dígitos")]
        public string Celular { get; set; }

        [Display(Name = "Cuenta de Ahorros")]
        [StringLength(20, MinimumLength = 10, ErrorMessage = "La cuenta de ahorros debe tener entre 10 y 20 dígitos")]
        public string CuentaAhorros { get; set; }
        [Display(Name = "Banco")]
        public string Banco { get; set; }  // ← NUEVO CAMPO

        [Display(Name = "Dirección")]
        public string Direccion { get; set; }

        [Display(Name = "Permiso Marcación Excepcional")]
        public bool PermisoMarcacionExcepcional { get; set; }

        [Display(Name = "Tipo de Permiso")]
        public string TipoPermisoExcepcional { get; set; }
    }

    public class LoginViewModel
    {
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "Email inválido")]
        public string Email { get; set; }

        [Required(ErrorMessage = "La contraseña es requerida")]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        public bool Recordarme { get; set; }
    }

    // Modelo para crear usuario con validaciones
  

        public class CrearUsuarioViewModel
        {
            [Required(ErrorMessage = "El DNI es requerido")]
            [Display(Name = "DNI")]
            [StringLength(8, MinimumLength = 8, ErrorMessage = "El DNI debe tener exactamente 8 dígitos")]
            [RegularExpression("^[0-9]{8}$", ErrorMessage = "El DNI debe contener solo números y tener 8 dígitos")]
            public string DNI { get; set; }

            [Required(ErrorMessage = "Los nombres son requeridos")]
            [Display(Name = "Nombres")]
            [StringLength(50, MinimumLength = 2, ErrorMessage = "Los nombres deben tener entre 2 y 50 caracteres")]
            public string Nombres { get; set; }

            [Required(ErrorMessage = "El apellido paterno es requerido")]
            [Display(Name = "Apellido Paterno")]
            [StringLength(50, MinimumLength = 2, ErrorMessage = "El apellido debe tener entre 2 y 50 caracteres")]
            public string ApellidoPaterno { get; set; }

        [Display(Name = "Fecha de Inicio")]
        [DataType(DataType.Date)]
        [DisplayFormat(DataFormatString = "{0:yyyy-MM-dd}", ApplyFormatInEditMode = true)]
        public DateTime FechaInicio { get; set; } = DateTime.Today;  // ← NUEVO CAMPO


        [Display(Name = "Apellido Materno")]
            [StringLength(50, ErrorMessage = "El apellido no puede tener más de 50 caracteres")]
            public string ApellidoMaterno { get; set; }

            [Display(Name = "Rol del Sistema")]
            public string Rol { get; set; }

            [Display(Name = "Rol de Pago")]
            public string RolPago { get; set; }  // ← AGREGAR ESTA LÍNEA

        [Display(Name = "Contraseña")]
        [Required(ErrorMessage = "La contraseña es requerida")]
        [StringLength(20, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre 6 y 20 caracteres")]
        [DataType(DataType.Password)]
        public string Password { get; set; }  // ← NUEVO CAMPO

        [Display(Name = "Permiso Marcación Excepcional")]
        public bool PermisoMarcacionExcepcional { get; set; }

        [Display(Name = "Tipo de Permiso")]
        public string TipoPermisoExcepcional { get; set; }

        [Display(Name = "Tarifa por Hora")]
        public decimal TarifaHora { get; set; }

        [Display(Name = "Concepto de Pago")]
        public int? ConceptoPagoId { get; set; }  // ← NUEVO CAMPO

        [Display(Name = "Celular")]
            [Phone(ErrorMessage = "Número de celular inválido")]
            [StringLength(9, MinimumLength = 9, ErrorMessage = "El celular debe tener exactamente 9 dígitos")]
            [RegularExpression("^[0-9]{9}$", ErrorMessage = "El celular debe contener solo números y tener 9 dígitos")]
            public string Celular { get; set; }

        [Display(Name = "Cuenta de Ahorros")]
        [StringLength(20, MinimumLength = 10, ErrorMessage = "La cuenta de ahorros debe tener entre 10 y 20 dígitos")]
        [RegularExpression("^[0-9]{10,20}$", ErrorMessage = "La cuenta debe contener solo números")]
        public string CuentaAhorros { get; set; }

        [Display(Name = "Banco")]
        public string Banco { get; set; }  // ← NUEVO CAMPO

        [Display(Name = "Dirección")]
            [StringLength(200, ErrorMessage = "La dirección no puede tener más de 200 caracteres")]
            public string Direccion { get; set; }

            [Display(Name = "Horas Mensuales Objetivo")]
            public decimal HorasMensualesObjetivo { get; set; } = 160;
        }
     
}