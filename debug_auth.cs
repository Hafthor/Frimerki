using System;
using System.Security.Cryptography;

class DebugAuth
{
    static void Main()
    {
        var password = "password123";

        // Generate a salt
        var saltBytes = new byte[32];
        RandomNumberGenerator.Fill(saltBytes);
        var salt = Convert.ToBase64String(saltBytes);

        // Hash the password - match service implementation
        string passwordHash;
        using (var pbkdf2 = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), 10000, HashAlgorithmName.SHA256))
        {
            var hash = pbkdf2.GetBytes(32);
            passwordHash = Convert.ToBase64String(hash);
        }

        Console.WriteLine($"Salt: {salt}");
        Console.WriteLine($"Hash: {passwordHash}");

        // Verify the password
        string verifyHash;
        using (var pbkdf2 = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), 10000, HashAlgorithmName.SHA256))
        {
            var hash = pbkdf2.GetBytes(32);
            verifyHash = Convert.ToBase64String(hash);
        }

        Console.WriteLine($"Verify Hash: {verifyHash}");
        Console.WriteLine($"Match: {passwordHash == verifyHash}");
    }
}
