using System;
using System.Security.Cryptography;

class Program
{
    static void Main()
    {
        var password = "password123";
        var saltBytes = new byte[32];
        RandomNumberGenerator.Fill(saltBytes);
        var salt = Convert.ToBase64String(saltBytes);

        Console.WriteLine($"Salt: {salt}");

        string passwordHash;
        using (var pbkdf2 = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), 10000, HashAlgorithmName.SHA256))
        {
            var hash = pbkdf2.GetBytes(32);
            passwordHash = Convert.ToBase64String(hash);
            Console.WriteLine($"Hash: {passwordHash}");
        }

        // Verify it works
        using (var pbkdf2 = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), 10000, HashAlgorithmName.SHA256))
        {
            var hash = pbkdf2.GetBytes(32);
            var verifyHash = Convert.ToBase64String(hash);
            Console.WriteLine($"Verify: {verifyHash}");
            Console.WriteLine($"Match: {passwordHash == verifyHash}");
        }
    }
}
