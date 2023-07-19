#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.NativeUI;
using FTOptix.HMIProject;
using FTOptix.UI;
using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.NetLogic;
using FTOptix.DataLogger;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using Utilities;
using System.Text.RegularExpressions;
#endregion

namespace Utilities
{
    public class FileSignAndVerifyUtilities
    {
        public static string GetFormattedKey(string key)
        {
            string result = "";
            int offset = 0;
            const int LINE_LENGTH = 64;
            while (offset < key.Length)
            {
                var lineEnd = Math.Min(offset + LINE_LENGTH, key.Length);
                result += key.Substring(offset, lineEnd - offset) + Environment.NewLine;
                offset = lineEnd;
            }

            return result.Remove(result.Length - 1, 1);
        }

        public static string ResourceUriValueToAbsoluteFilePath(UAValue value)
        {
            var resourceUri = new ResourceUri(value);
            return resourceUri.Uri;
        }

        public static void ExportPrivateKey(RSA rsa, string outputPath)
        {
            var privateKeyBytes = rsa.ExportRSAPrivateKey();

            const string header = "-----BEGIN RSA PRIVATE KEY-----";
            const string footer = "-----END RSA PRIVATE KEY-----";

            var builder = new StringBuilder();
            builder.AppendLine(header);

            var privateKeyString = Convert.ToBase64String(privateKeyBytes);
            string key = FileSignAndVerifyUtilities.GetFormattedKey(privateKeyString);
            builder.AppendLine(key);
            builder.Append(footer);

            var filePath = outputPath;
            File.WriteAllText(filePath, builder.ToString());
        }

        public static void ExportPublicKey(RSA rsa, string outputPath)
        {
            const string header = "-----BEGIN PUBLIC KEY-----";
            const string footer = "-----END PUBLIC KEY-----";

            var builder = new StringBuilder();
            builder.AppendLine(header);

            var publicKeyBytes = rsa.ExportSubjectPublicKeyInfo();
            var publicKeyString = Convert.ToBase64String(publicKeyBytes);
            builder.AppendLine(FileSignAndVerifyUtilities.GetFormattedKey(publicKeyString));
            builder.Append(footer);

            var filePath = outputPath;
            File.WriteAllText(filePath, builder.ToString());
        }

        public static string SpliceText(string text, int lineLength)
        {
            return Regex.Replace(text, "(.{" + lineLength + "})", "$1" + Environment.NewLine);
        }
    }
}

public class FileSignVerify : BaseNetLogic
{
    public override void Start()
    {
    }

    public override void Stop()
    {
    }

    [ExportMethod]
    public void SignFile(string filePath)
    {
        LoadPrivateKey();
        if (privateKey == null)
        {
            Log.Error("FileSignVerify", "Missing private key. Unable to sign file.");
            return;
        }

        var fileAbsolutePath = FileSignAndVerifyUtilities.ResourceUriValueToAbsoluteFilePath(filePath);
        if (!File.Exists(fileAbsolutePath))
            Log.Error("FileSignVerify", "Unable to find file to sign");

        HashAlgorithm hasher = SHA256.Create();
        byte[] hash = hasher.ComputeHash(File.ReadAllBytes(fileAbsolutePath));
        byte[] signature = privateKey.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var base64String = Convert.ToBase64String(signature);
        var signatureFilePath = fileAbsolutePath + ".sign";
        File.WriteAllText(signatureFilePath, FileSignAndVerifyUtilities.SpliceText(base64String, 64));
    }

    [ExportMethod]
    public void VerifyFileSignature(string filePath, string signatureFilePath, out bool verifyResult)
    {
        LoadPublicKey();
        if (publicKey == null)
        {
            verifyResult = false;
            return;
        }

        var fileAbsolutePath = FileSignAndVerifyUtilities.ResourceUriValueToAbsoluteFilePath(filePath);
        var signatureFileAbsolutePath = FileSignAndVerifyUtilities.ResourceUriValueToAbsoluteFilePath(signatureFilePath);
        if (!File.Exists(fileAbsolutePath))
        {
            Log.Error("FileSignVerify", "Unable to locate file to verify.");
            verifyResult = false;
            return;
        }

        if (!File.Exists(signatureFileAbsolutePath))
        {
            Log.Error("FileSignVerify", "Unable to locate signature file.");
            verifyResult = false;
            return;
        }

        var signature = File.ReadAllText(signatureFileAbsolutePath);
        var result = publicKey.VerifyData(File.ReadAllBytes(fileAbsolutePath),
                                          Convert.FromBase64String(signature),
                                          HashAlgorithmName.SHA256,
                                          RSASignaturePadding.Pkcs1);

        verifyResult = result;
    }

    [ExportMethod]
    public void GeneratePublicAndPrivateKey()
    {
        RSA rsa = RSA.Create(2048);
        FileSignAndVerifyUtilities.ExportPrivateKey(rsa, GetPrivateKeyPath());
        FileSignAndVerifyUtilities.ExportPublicKey(rsa, GetPublicKeyPath());
    }

    private void LoadPrivateKey()
    {
        var privateKeyPath = GetPrivateKeyPath();
        if (!File.Exists(privateKeyPath))
            return;

        privateKey = RSA.Create();
        string privateKeyString = File.ReadAllText(privateKeyPath);
        privateKey.ImportFromPem(privateKeyString.ToCharArray());
    }

    private void LoadPublicKey()
    {
        var publicKeyPath = GetPublicKeyPath();
        if (!File.Exists(publicKeyPath))
        {
            Log.Error("FileSignVerify", "Missing public key. Unable to verify file.");
            return;
        }

        publicKey = RSA.Create();
        string publicKeyString = File.ReadAllText(publicKeyPath);
        publicKey.ImportFromPem(publicKeyString.ToCharArray());
    }

    private string GetPrivateKeyPath()
    {
        return FileSignAndVerifyUtilities.ResourceUriValueToAbsoluteFilePath(LogicObject.GetVariable("PrivateKey").Value);
    }

    private string GetPublicKeyPath()
    {
        return FileSignAndVerifyUtilities.ResourceUriValueToAbsoluteFilePath(LogicObject.GetVariable("PublicKey").Value);
    }

    private RSA privateKey;
    private RSA publicKey;
}
