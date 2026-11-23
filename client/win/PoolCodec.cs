using System;
using System.IO;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Veil
{
    // Формат файла пула (блоб на публичном хосте).
    //
    //   "VLP2" | nonce(12) | AES-256-GCM( plaintext )
    //
    //   plaintext = bodyJson + "\n" + base64(ed25519_sign(bodyJson)) + "\n"
    //
    // Ключ шифрования = HKDF-SHA256(psk, salt "veil-pool-v2", info "blob") → 32 байта.
    // PSK уже есть у клиента (он в ключе), поэтому отдельный секрет не нужен.
    // Подпись проверяется публичным ключом, вшитым в приложение: даже если
    // хостинг подменит файл, клиент это увидит.
    public static class PoolCodec
    {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("VLP2");
        static readonly byte[] Salt = Encoding.ASCII.GetBytes("veil-pool-v2");
        static readonly byte[] Info = Encoding.ASCII.GetBytes("blob");

        // ── ключи ───────────────────────────────────────────────────────────

        public static byte[] NewPrivateKey()
        {
            var r = new SecureRandom();
            byte[] k = new byte[32];
            r.NextBytes(k);
            return k;
        }

        public static byte[] PublicFromPrivate(byte[] priv)
        {
            var gen = new Ed25519PrivateKeyParameters(priv, 0);
            return gen.GeneratePublicKey().GetEncoded();
        }

        public static string KeyToBase64(byte[] key)
        {
            return Convert.ToBase64String(key);
        }

        public static byte[] KeyFromBase64(string s)
        {
            return Convert.FromBase64String(s.Trim());
        }

        // ── сборка и разбор ─────────────────────────────────────────────────

        public static byte[] Pack(string bodyJson, byte[] privKey, string psk)
        {
            byte[] body = Encoding.UTF8.GetBytes(bodyJson);

            var signer = new Ed25519Signer();
            signer.Init(true, new Ed25519PrivateKeyParameters(privKey, 0));
            signer.BlockUpdate(body, 0, body.Length);
            string sig = Convert.ToBase64String(signer.GenerateSignature());

            string plain = bodyJson + "\n" + sig + "\n";
            return Encrypt(Encoding.UTF8.GetBytes(plain), DeriveKey(psk));
        }

        public static string Unpack(byte[] blob, byte[] pubKey, string psk)
        {
            string plain = Encoding.UTF8.GetString(Decrypt(blob, DeriveKey(psk))).TrimEnd('\n');
            int sep = plain.LastIndexOf('\n');
            if (sep <= 0) throw new InvalidDataException("пул: нет подписи");

            string body = plain.Substring(0, sep);
            string sig = plain.Substring(sep + 1).Trim();
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            byte[] sigBytes = Convert.FromBase64String(sig);

            var v = new Ed25519Signer();
            v.Init(false, new Ed25519PublicKeyParameters(pubKey, 0));
            v.BlockUpdate(bodyBytes, 0, bodyBytes.Length);
            if (!v.VerifySignature(sigBytes)) throw new InvalidDataException("пул: подпись неверна");
            return body;
        }

        // ── шифрование ──────────────────────────────────────────────────────

        public static byte[] DeriveKey(string psk)
        {
            HkdfBytesGenerator hk = new HkdfBytesGenerator(new Sha256Digest());
            hk.Init(new HkdfParameters(Encoding.UTF8.GetBytes(psk), Salt, Info));
            byte[] okm = new byte[32];
            hk.GenerateBytes(okm, 0, okm.Length);
            return okm;
        }

        public static byte[] Encrypt(byte[] plain, byte[] key)
        {
            var rnd = new SecureRandom();
            byte[] nonce = new byte[12];
            rnd.NextBytes(nonce);

            var gcm = new GcmBlockCipher(new Org.BouncyCastle.Crypto.Engines.AesEngine());
            gcm.Init(true, new AeadParameters(new KeyParameter(key), 128, nonce));
            byte[] ct = new byte[gcm.GetOutputSize(plain.Length)];
            int n = gcm.ProcessBytes(plain, 0, plain.Length, ct, 0);
            n += gcm.DoFinal(ct, n);

            byte[] outBuf = new byte[Magic.Length + nonce.Length + n];
            Buffer.BlockCopy(Magic, 0, outBuf, 0, Magic.Length);
            Buffer.BlockCopy(nonce, 0, outBuf, Magic.Length, nonce.Length);
            Buffer.BlockCopy(ct, 0, outBuf, Magic.Length + nonce.Length, n);
            return outBuf;
        }

        public static byte[] Decrypt(byte[] blob, byte[] key)
        {
            if (blob.Length < Magic.Length + 12 + 16) throw new InvalidDataException("пул: файл слишком короткий");
            for (int i = 0; i < Magic.Length; i++)
                if (blob[i] != Magic[i]) throw new InvalidDataException("пул: неверный заголовок");

            byte[] nonce = new byte[12];
            Buffer.BlockCopy(blob, Magic.Length, nonce, 0, 12);

            var gcm = new GcmBlockCipher(new Org.BouncyCastle.Crypto.Engines.AesEngine());
            gcm.Init(false, new AeadParameters(new KeyParameter(key), 128, nonce));
            int len = blob.Length - Magic.Length - nonce.Length;
            byte[] outBuf = new byte[gcm.GetOutputSize(len)];
            int n = gcm.ProcessBytes(blob, Magic.Length + nonce.Length, len, outBuf, 0);
            try { n += gcm.DoFinal(outBuf, n); }
            catch (InvalidCipherTextException) { throw new InvalidDataException("пул: не расшифровывается (другой PSK?)"); }

            byte[] res = new byte[n];
            Buffer.BlockCopy(outBuf, 0, res, 0, n);
            return res;
        }
    }
}
