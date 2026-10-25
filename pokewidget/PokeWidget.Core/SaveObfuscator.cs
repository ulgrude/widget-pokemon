using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PokeWidget.Core;

/// <summary>
/// Obfuscation des sauvegardes : JSON → GZip → AES-GCM. Format : "PKW1" | nonce (12) | tag (16) | données chiffrées.
/// Le tag d'authentification détecte toute modification (même d'un seul octet).
///
/// Ce n'est PAS un secret : la clé est dérivée de constantes présentes dans l'exécutable. Quelqu'un de déterminé
/// qui décompile l'application pourra toujours la retrouver. Le but est d'empêcher l'édition à la main d'un fichier
/// texte, pas de protéger contre un attaquant.
/// </summary>
public static class SaveObfuscator
{
    private static readonly byte[] Magic = "PKW1"u8.ToArray();
    private const int NonceSize = 12, TagSize = 16, HeaderSize = 4 + NonceSize + TagSize;

    public static bool HasMagic(byte[] data) => data.Length >= Magic.Length && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    private static byte[] Key() =>
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat("poke", "-widget/", "save/v1/", "ulgrude", ":", "7f3a9c1e")));

    public static byte[] Protect(string json)
    {
        byte[] plain = Compress(Encoding.UTF8.GetBytes(json));
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];
        using (var aes = new AesGcm(Key(), TagSize)) aes.Encrypt(nonce, plain, cipher, tag, Magic);

        var output = new byte[HeaderSize + cipher.Length];
        Magic.CopyTo(output, 0);
        nonce.CopyTo(output, 4);
        tag.CopyTo(output, 4 + NonceSize);
        cipher.CopyTo(output, HeaderSize);
        return output;
    }

    /// <summary>Lève InvalidDataException si le fichier n'est pas une sauvegarde, a été modifié ou est corrompu.</summary>
    public static string Unprotect(byte[] data)
    {
        if (!HasMagic(data) || data.Length < HeaderSize) throw new InvalidDataException("fichier de sauvegarde non reconnu");

        byte[] plain = new byte[data.Length - HeaderSize];
        try
        {
            using var aes = new AesGcm(Key(), TagSize);
            aes.Decrypt(data.AsSpan(4, NonceSize), data.AsSpan(HeaderSize), data.AsSpan(4 + NonceSize, TagSize), plain, Magic);
        }
        catch (CryptographicException)
        {
            throw new InvalidDataException("sauvegarde modifiée ou corrompue");
        }
        return Encoding.UTF8.GetString(Decompress(plain));
    }

    private static byte[] Compress(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(raw);
        return ms.ToArray();
    }

    private static byte[] Decompress(byte[] zipped)
    {
        using var gz = new GZipStream(new MemoryStream(zipped), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        int n;
        while ((n = gz.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += n;
            if (total > GameConfig.MaxImportChars * 2L) throw new InvalidDataException("sauvegarde trop volumineuse");
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }
}
