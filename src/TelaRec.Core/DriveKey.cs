using System.Security.Cryptography;

namespace TelaRec.Core;

public static class DriveKey
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>Gera uma chave aleatória (só letras e números, sem aspas) para colar no script e no TelaRec.</summary>
    public static string Generate(int length = 32)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
