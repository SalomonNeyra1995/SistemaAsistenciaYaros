using System;
using System.Data;
using System.Data.SqlClient;
using ControlAsistenciaFinal.Models;

namespace ControlAsistenciaFinal.Controllers
{
    public class UsuarioController
    {
        public DataTable ObtenerTodos()
        {
            string query = "SELECT Id, NombreCompleto, Email, Rol, Activo FROM Usuarios ORDER BY NombreCompleto";
            return DatabaseHelper.ExecuteQuery(query, null);
        }

        public DataTable ObtenerPorId(int id)
        {
            string query = "SELECT Id, NombreCompleto, Email, Rol, Activo FROM Usuarios WHERE Id = @Id";
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Id", id)
            };
            return DatabaseHelper.ExecuteQuery(query, parameters);
        }

        public int Crear(string nombre, string email, string password, string rol, decimal horasObjetivo)
        {
            string passwordHash = SecurityHelper.HashPassword(password);

            string query = @"INSERT INTO Usuarios (NombreCompleto, Email, PasswordHash, Rol, HorasMensualesObjetivo, Activo)
                             VALUES (@Nombre, @Email, @Password, @Rol, @Horas, 1);
                             SELECT SCOPE_IDENTITY();";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Nombre", nombre),
                new SqlParameter("@Email", email),
                new SqlParameter("@Password", passwordHash),
                new SqlParameter("@Rol", rol),
                new SqlParameter("@Horas", horasObjetivo)
            };

            object result = DatabaseHelper.ExecuteScalar(query, parameters);
            return result != null ? Convert.ToInt32(result) : 0;
        }

        public bool Actualizar(int id, string nombre, string email, string rol, bool activo)
        {
            string query = @"UPDATE Usuarios 
                             SET NombreCompleto = @Nombre, Email = @Email, Rol = @Rol, Activo = @Activo 
                             WHERE Id = @Id";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Id", id),
                new SqlParameter("@Nombre", nombre),
                new SqlParameter("@Email", email),
                new SqlParameter("@Rol", rol),
                new SqlParameter("@Activo", activo)
            };

            int filas = DatabaseHelper.ExecuteNonQuery(query, parameters);
            return filas > 0;
        }

        public bool Eliminar(int id)
        {
            string query = "DELETE FROM Usuarios WHERE Id = @Id";
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Id", id)
            };

            int filas = DatabaseHelper.ExecuteNonQuery(query, parameters);
            return filas > 0;
        }

        public bool CambiarPassword(int id, string nuevaPassword)
        {
            string nuevoHash = SecurityHelper.HashPassword(nuevaPassword);

            string query = "UPDATE Usuarios SET PasswordHash = @Password WHERE Id = @Id";
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Id", id),
                new SqlParameter("@Password", nuevoHash)
            };

            int filas = DatabaseHelper.ExecuteNonQuery(query, parameters);
            return filas > 0;
        }
    }

}