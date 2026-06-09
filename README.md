🚀 Sistema de Control de Asistencia - Yaros Group Peru
Versión .NET SQL Server Licencia

📋 Descripción del Proyecto
Sistema integral de gestión de asistencia y control de personal desarrollado para Yaros Group Peru. Permite el registro de entrada/salida de empleados, control de tardanzas, ausencias, gestión de pagos y reportes automatizados.

🧪 Credenciales de Acceso (Demo/Pruebas)
⚠️ Importante: Estas credenciales son para propósitos de prueba y demostración. En un entorno de producción, cambie todas las contraseñas inmediatamente.

👑 Acceso Administrador
Rol	Email	Contraseña	Permisos
Administrador General	admin@asistenciayaros.com	123456	• Control total del sistema
• Gestión de usuarios
• Configuración global
• Reportes avanzados
👥 Acceso Empleado (Demo)
Rol	Email	Contraseña	Permisos
Empleado / Facilitador	dgamarraj@asistenciayaros.com	72040895	• Registrar asistencia
• Ver historial personal
• Solicitar permisos
• Ver mis reportes
🚀 Inicio Rápido
# Iniciar sesión como Administrador
Email: admin@asistenciayaros.com
Contraseña: 123456

# Iniciar sesión como Empleado
Email: dgamarraj@asistenciayaros.com
Contraseña: 72040895

### 🎯 Características Principales

- ✅ **Registro de Asistencia** - Control de entrada/salida con horarios personalizados
- 📊 **Dashboard Administrativo** - Visualización en tiempo real de indicadores clave
- ⏰ **Control de Tardanzas** - Detección automática y registro de minutos tarde
- 👥 **Gestión de Usuarios** - CRUD completo de empleados con diferentes roles
- 💰 **Control de Pagos** - Gestión de haberes y descuentos por asistencia
- 📈 **Reportes Avanzados** - Exportación a Excel/PDF con filtros personalizados
- 🔔 **Alertas Automáticas** - Notificaciones de horas completadas y metas alcanzadas
- 📱 **Diseño Responsivo** - Compatible con dispositivos móviles, tablets y pantallas curvas

## 🛠️ Tecnologías Utilizadas

### Backend
| Tecnología | Versión | Propósito |
|------------|---------|------------|
| **ASP.NET MVC** | 6.0 | Framework principal |
| **C#** | 10.0 | Lenguaje de programación |
| **Entity Framework** | 6.0 | ORM para base de datos |
| **SQL Server** | 2019 | Base de datos relacional |
| **LINQ** | - | Consultas a base de datos |

### Frontend
| Tecnología | Versión | Propósito |
|------------|---------|------------|
| **Bootstrap** | 5.3 | Framework CSS responsivo |
| **jQuery** | 3.6 | Manipulación del DOM |
| **SweetAlert2** | 11.0 | Alertas y modales modernos |
| **Font Awesome** | 6.0 | Iconografía profesional |
| **HTML5/CSS3** | - | Estructura y estilos |

### Herramientas de Desarrollo
- **Visual Studio 2022** - IDE principal
- **Git & GitHub** - Control de versiones
- **SQL Server Management Studio (SSMS)** - Gestión de BD

## 📦 Estructura del Proyecto
AsistenciaYaros/
├── Controllers/
│ ├── AdminController.cs # Lógica del panel administrativo
│ ├── AsistenciaController.cs # Registro de asistencia
│ └── UsuarioController.cs # Gestión de usuarios
├── Models/
│ ├── Entities/ # Modelos de base de datos
│ ├── ViewModels/ # Modelos para vistas
│ └── Context/ # DbContext de EF
├── Views/
│ ├── Admin/ # Vistas administrativas
│ │ ├── Dashboard.cshtml # Panel principal
│ │ ├── Usuarios.cshtml # CRUD usuarios
│ │ └── Reportes.cshtml # Reportes
│ └── Shared/ # Layouts y partials
├── Content/
│ ├── css/ # Estilos personalizados
│ ├── images/ # Recursos gráficos
│ └── js/ # Scripts cliente
├── Database/
│ └── ScriptBaseDatos.sql # Script de creación de BD
└── Web.config # Configuración del proyecto


## 🚀 Instalación y Configuración

### Requisitos Previos

- ✅ Windows 10/11 o Windows Server 2016+
- ✅ Visual Studio 2022 (Community o superior)
- ✅ SQL Server 2019 Express o superior
- ✅ .NET 6.0 SDK
- ✅ Git (opcional, para clonar repositorio)

### Pasos de Instalación

#### 1️⃣ Clonar el Repositorio
#### Mejoras significativas y actuales 

```bash
# HTTPS
git clone https://github.com/TU-USUARIO/asistencia-yaros.git

# o SSH
git clone git@github.com:TU-USUARIO/asistencia-yaros.git

# Navegar al proyecto
cd asistencia-yaros
