using System.Security.Cryptography;
using System.Text;

namespace ControlAsistenciaFinal.Models
{
    public static class SecurityHelper
    {
        public static string HashPassword(string password)
        {
            using (SHA1Managed sha1 = new SHA1Managed())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder sb = new StringBuilder(hash.Length * 2);

                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("X2"));
                }

                return sb.ToString();
            }
        }
    }
}