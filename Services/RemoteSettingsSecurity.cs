using System;
using System.Security.Cryptography;
using System.Text;

namespace ShabiLite.Services
{
    internal static class RemoteSettingsSecurity
    {
        private const string PasswordSalt = "ShabiRemoteSettings:";

        public static string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(PasswordSalt + (password ?? string.Empty));
                return Convert.ToBase64String(sha256.ComputeHash(bytes));
            }
        }

        public static bool VerifyPassword(string password, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                return true;
            }

            byte[] expected;
            byte[] actual;
            try
            {
                expected = Convert.FromBase64String(expectedHash.Trim());
                actual = Convert.FromBase64String(HashPassword(password));
            }
            catch (FormatException)
            {
                return false;
            }

            if (expected.Length != actual.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < expected.Length; index++)
            {
                difference |= expected[index] ^ actual[index];
            }
            return difference == 0;
        }
    }
}
