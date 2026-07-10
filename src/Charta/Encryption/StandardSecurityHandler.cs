using System.Security.Cryptography;
using System.Text;
using Charta.Cos;

namespace Charta.Encryption;

/// <summary>
/// The AES-256 standard security handler (ISO 32000-2 §7.6.4, V5 / revision 6). Derives the file
/// encryption key from the user and owner passwords via Algorithm 2.B, builds the /Encrypt dictionary
/// (/O, /U, /OE, /UE, /Perms), and encrypts strings and streams with AES-256-CBC. Uses only the BCL
/// cryptography stack. Because it generates fresh random salts, keys, and IVs, encrypted output is not
/// byte-reproducible.
/// </summary>
internal sealed class StandardSecurityHandler : IStreamEncryptor
{
    private static readonly byte[] Zero16 = new byte[16];

    private readonly byte[] _fileKey = new byte[32];
    private readonly byte[] _u = new byte[48];
    private readonly byte[] _o = new byte[48];
    private readonly byte[] _ue = new byte[32];
    private readonly byte[] _oe = new byte[32];
    private readonly byte[] _perms = new byte[16];
    private readonly int _p;

    public StandardSecurityHandler(string userPassword, string? ownerPassword, PdfPermissions permissions)
    {
        var user = PasswordBytes(userPassword);
        var owner = PasswordBytes(ownerPassword ?? userPassword);

        // Bits 1–2 must be 0, bits 7–8 and 13–32 must be 1; the granted permission bits are OR-ed in.
        _p = unchecked((int)0xFFFFF0C0) | (int)permissions;

        RandomNumberGenerator.Fill(_fileKey);

        Span<byte> salts = stackalloc byte[16];
        RandomNumberGenerator.Fill(salts);
        var userValidationSalt = salts[..8].ToArray();
        var userKeySalt = salts[8..].ToArray();
        RandomNumberGenerator.Fill(salts);
        var ownerValidationSalt = salts[..8].ToArray();
        var ownerKeySalt = salts[8..].ToArray();

        // /U: validation hash over the user password, then the two salts.
        Hash2B(user, userValidationSalt, []).AsSpan(0, 32).CopyTo(_u);
        userValidationSalt.CopyTo(_u.AsSpan(32));
        userKeySalt.CopyTo(_u.AsSpan(40));
        // /UE: the file key wrapped with the key derived from the user key salt.
        AesCbcNoPad(Hash2B(user, userKeySalt, []), Zero16, _fileKey).CopyTo(_ue.AsSpan());

        // /O: like /U but the owner hashes also cover the 48-byte /U value.
        Hash2B(owner, ownerValidationSalt, _u).AsSpan(0, 32).CopyTo(_o);
        ownerValidationSalt.CopyTo(_o.AsSpan(32));
        ownerKeySalt.CopyTo(_o.AsSpan(40));
        AesCbcNoPad(Hash2B(owner, ownerKeySalt, _u), Zero16, _fileKey).CopyTo(_oe.AsSpan());

        BuildPerms(permissions);
    }

    private void BuildPerms(PdfPermissions permissions)
    {
        Span<byte> block = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(block, _p);
        block[4] = block[5] = block[6] = block[7] = 0xFF;
        block[8] = (byte)'T'; // metadata is encrypted
        block[9] = (byte)'a';
        block[10] = (byte)'d';
        block[11] = (byte)'b';
        RandomNumberGenerator.Fill(block[12..]);
        _ = permissions;

        using var aes = Aes.Create();
        aes.Key = _fileKey;
        aes.EncryptEcb(block, PaddingMode.None).CopyTo(_perms.AsSpan());
    }

    /// <summary>Encrypts one string or stream: a random 16-byte IV followed by AES-256-CBC ciphertext.</summary>
    public byte[] EncryptData(ReadOnlySpan<byte> data)
    {
        Span<byte> iv = stackalloc byte[16];
        RandomNumberGenerator.Fill(iv);

        using var aes = Aes.Create();
        aes.Key = _fileKey;
        var cipher = aes.EncryptCbc(data, iv, PaddingMode.PKCS7);

        var result = new byte[16 + cipher.Length];
        iv.CopyTo(result);
        cipher.CopyTo(result.AsSpan(16));
        return result;
    }

    /// <summary>Builds the /Encrypt dictionary. Its strings are written verbatim (already ciphertext).</summary>
    public CosDictionary BuildDictionary()
    {
        var stdCf = new CosDictionary
        {
            [CosName.Get("CFM")] = CosName.Get("AESV3"),
            [CosName.Get("AuthEvent")] = CosName.Get("DocOpen"),
            [CosName.Get("Length")] = new CosInteger(32),
        };
        var cf = new CosDictionary { [CosName.Get("StdCF")] = stdCf };

        return new CosDictionary
        {
            [CosName.Get("Filter")] = CosName.Get("Standard"),
            [CosName.Get("V")] = new CosInteger(5),
            [CosName.Get("R")] = new CosInteger(6),
            [CosName.Get("Length")] = new CosInteger(256),
            [CosName.Get("CF")] = cf,
            [CosName.Get("StmF")] = CosName.Get("StdCF"),
            [CosName.Get("StrF")] = CosName.Get("StdCF"),
            [CosName.Get("O")] = new CosString(_o) { Encrypt = false },
            [CosName.Get("U")] = new CosString(_u) { Encrypt = false },
            [CosName.Get("OE")] = new CosString(_oe) { Encrypt = false },
            [CosName.Get("UE")] = new CosString(_ue) { Encrypt = false },
            [CosName.Get("Perms")] = new CosString(_perms) { Encrypt = false },
            [CosName.Get("P")] = new CosInteger(_p),
            [CosName.Get("EncryptMetadata")] = CosBoolean.True,
        };
    }

    /// <summary>The revision-6 password hash (Algorithm 2.B): AES-cycled SHA-256/384/512 rounds.</summary>
    private static byte[] Hash2B(byte[] password, byte[] salt, byte[] udata)
    {
        var k = SHA256.HashData(Concat(password, salt, udata));
        var round = 0;
        byte[] e;
        do
        {
            round++;
            var block = Concat(password, k, udata);
            var k1 = new byte[block.Length * 64];
            for (var i = 0; i < 64; i++)
            {
                block.CopyTo(k1.AsSpan(i * block.Length));
            }

            // 64 copies of a block make a length divisible by 16, so no padding is needed.
            e = AesCbcNoPad128(k.AsSpan(0, 16), k.AsSpan(16, 16), k1);

            var sum = 0;
            for (var i = 0; i < 16; i++)
            {
                sum += e[i];
            }

            k = (sum % 3) switch
            {
                0 => SHA256.HashData(e),
                1 => SHA384.HashData(e),
                _ => SHA512.HashData(e),
            };
        }
        while (round < 64 || e[^1] > round - 32);

        return k[..32];
    }

    private static byte[] AesCbcNoPad(byte[] key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(data, iv, PaddingMode.None);
    }

    private static byte[] AesCbcNoPad128(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = key.ToArray();
        return aes.EncryptCbc(data, iv, PaddingMode.None);
    }

    private static byte[] Concat(byte[] a, ReadOnlySpan<byte> b, byte[] c)
    {
        var result = new byte[a.Length + b.Length + c.Length];
        a.CopyTo(result.AsSpan());
        b.CopyTo(result.AsSpan(a.Length));
        c.CopyTo(result.AsSpan(a.Length + b.Length));
        return result;
    }

    // ISO 32000-2: the password is the UTF-8 encoding (SASLprep'd), truncated to 127 bytes.
    private static byte[] PasswordBytes(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        return bytes.Length <= 127 ? bytes : bytes[..127];
    }
}
