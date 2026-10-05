using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web.Script.Serialization;

namespace Dpi.Setup;

internal sealed class Payload
{
	private static readonly string[] names = new string[5] { "DPI.exe", "DpiFilter.sys", "Launch.exe", "Uninstall.exe", "certificate.cer" };

	private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();

	public X509Certificate2 Certificate { get; private set; }

	public string DriverHash => Layout.Hash(files["DpiFilter.sys"]);

	public Payload()
	{
		Dictionary<string, string> dictionary = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(Read("hashes.json")));
		if (dictionary == null || dictionary.Count != names.Length)
		{
			throw new InvalidDataException("The package manifest is invalid.");
		}
		string[] array = names;
		foreach (string text in array)
		{
			byte[] array2 = Read(text);
			if (!dictionary.TryGetValue(text, out var value) || Layout.Hash(array2) != value)
			{
				throw new InvalidDataException("The packaged file is damaged: " + text);
			}
			files.Add(text, array2);
		}
		Certificate = new X509Certificate2(files["certificate.cer"]);
		if (Certificate.HasPrivateKey || Certificate.Subject != "CN=DPI Modifier Local" || DateTime.Now < Certificate.NotBefore || DateTime.Now > Certificate.NotAfter)
		{
			throw new InvalidDataException("The public signing certificate is invalid or the computer clock is incorrect.");
		}
		byte[] array3 = files["DpiFilter.sys"];
		int num = BitConverter.ToInt32(array3, 60);
		if (num < 0 || num > array3.Length - 6 || BitConverter.ToUInt32(array3, num) != 17744 || BitConverter.ToUInt16(array3, num + 4) != 34404)
		{
			throw new InvalidDataException("The packaged driver is not an x64 image.");
		}
	}

	private static byte[] Read(string name)
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dpi.Payload." + name);
		if (stream == null)
		{
			throw new InvalidDataException("A packaged file is missing: " + name);
		}
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	public void Write(string name, string path)
	{
		using (FileStream fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
		{
			byte[] array = files[name];
			fileStream.Write(array, 0, array.Length);
			fileStream.Flush(flushToDisk: true);
		}
		if (Layout.HashFile(path) != Layout.Hash(files[name]))
		{
			throw new IOException("The installed file failed verification: " + name);
		}
	}

	public void Verify(string directory)
	{
		string fullPath = Path.GetFullPath(directory);
		Layout.CheckPath(fullPath);
		if (Directory.Exists(fullPath))
		{
			throw new IOException("The verification directory must not already exist.");
		}
		Directory.CreateDirectory(fullPath);
		string[] array = names;
		foreach (string text in array)
		{
			Write(text, Path.Combine(fullPath, text));
		}
		X509Certificate2 x509Certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(Path.Combine(fullPath, "DPI.exe")));
		if (x509Certificate.Thumbprint != Certificate.Thumbprint)
		{
			throw new InvalidDataException("The embedded application signer does not match.");
		}
		File.WriteAllText(Path.Combine(fullPath, "verify.json"), new JavaScriptSerializer().Serialize(new
		{
			Success = true,
			Files = names,
			DriverHash = DriverHash,
			CertificateThumbprint = Certificate.Thumbprint,
			ContainsPrivateKey = Certificate.HasPrivateKey,
			SecureBoot = Platform.SecureBoot,
			TestMode = Platform.TestMode
		}));
	}
}
