using System;
using System.Data;
using System.Data.SqlClient;
using System.Configuration;

namespace ControlAsistenciaFinal.Models
{
    public static class DatabaseHelper
    {
        private static string connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        // ============================================
        // MÉTODOS PARA STORED PROCEDURES
        // ============================================

        public static DataTable ExecuteStoredProcedure(string storedProcedureName, SqlParameter[] parameters)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                using (SqlCommand cmd = new SqlCommand(storedProcedureName, conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (SqlParameter param in parameters)
                        {
                            cmd.Parameters.Add(new SqlParameter(param.ParameterName, param.Value ?? DBNull.Value));
                        }
                    }

                    SqlDataAdapter da = new SqlDataAdapter(cmd);
                    DataTable dt = new DataTable();
                    da.Fill(dt);
                    return dt;
                }
            }
        }

        // ============================================
        // MÉTODOS PARA SQL DIRECTO (QUERIES)
        // ============================================

        public static DataTable ExecuteQuery(string query, SqlParameter[] parameters)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (SqlParameter param in parameters)
                        {
                            cmd.Parameters.AddWithValue(param.ParameterName, param.Value ?? DBNull.Value);
                        }
                    }

                    SqlDataAdapter da = new SqlDataAdapter(cmd);
                    DataTable dt = new DataTable();
                    da.Fill(dt);
                    return dt;
                }
            }
        }

        public static int ExecuteNonQuery(string query, SqlParameter[] parameters)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (SqlParameter param in parameters)
                        {
                            cmd.Parameters.AddWithValue(param.ParameterName, param.Value ?? DBNull.Value);
                        }
                    }

                    conn.Open();
                    return cmd.ExecuteNonQuery();
                }
            }
        }

        public static object ExecuteScalar(string query, SqlParameter[] parameters)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (SqlParameter param in parameters)
                        {
                            cmd.Parameters.AddWithValue(param.ParameterName, param.Value ?? DBNull.Value);
                        }
                    }

                    conn.Open();
                    return cmd.ExecuteScalar();
                }
            }
        }
    }
}