using System.Security.Cryptography;
using System.Text;

namespace NodeService.WebServer.Extensions;

public static class CryptographyHelper
{
    public static string CalculateFileMD5(string filename)
    {
        using (var md5 = MD5.Create())
        {
            using (var stream = File.OpenRead(filename))
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    public static string CalculateStreamMD5(Stream stream)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public static async Task<string?> CalculateStreamMD5Async(Stream stream)
    {
        using var md5 = MD5.Create();
        var hash = await md5.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public static string CalculateStringMD5(string str)
    {
        using (var md5 = MD5.Create())
        {
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(str));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    public static async Task<string?> CalculateSHA256Async(Stream stream)
    {
        var bytes = await SHA256.HashDataAsync(stream);
        var hash = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        return hash;
    }
}