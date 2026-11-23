using System;
using System.IO;
using System.Text;

namespace Veil
{
    // Утилита для пула серверов. Запускается на домашней машине.
    //
    //   pooltool keygen  <priv.key>                         — новая пара подписи
    //   pooltool pub     <priv.key>                         — публичный ключ (base64)
    //   pooltool pack    <pool.json> <out.bin> <priv.key> <psk>
    //   pooltool unpack  <in.bin> <pub.b64|pub.key> <psk>   — проверка блоба
    //   pooltool url     <host> <blob-name> <token>         — готовый путь для ключа
    class PoolTool
    {
        static int Main(string[] args)
        {
            try
            {
                if (args.Length < 2) return Usage();

                switch (args[0])
                {
                    case "keygen":
                    {
                        if (File.Exists(args[1])) { Console.Error.WriteLine("файл уже есть: " + args[1]); return 2; }
                        byte[] priv = PoolCodec.NewPrivateKey();
                        File.WriteAllText(args[1], Convert.ToBase64String(priv) + "\n");
                        byte[] pub = PoolCodec.PublicFromPrivate(priv);
                        File.WriteAllText(args[1] + ".pub", Convert.ToBase64String(pub) + "\n");
                        Console.WriteLine("приватный: " + args[1]);
                        Console.WriteLine("публичный: " + Convert.ToBase64String(pub));
                        return 0;
                    }
                    case "pub":
                    {
                        byte[] priv = PoolCodec.KeyFromBase64(File.ReadAllText(args[1]));
                        Console.WriteLine(Convert.ToBase64String(PoolCodec.PublicFromPrivate(priv)));
                        return 0;
                    }
                    case "pack":
                    {
                        if (args.Length < 5) return Usage();
                        string json = File.ReadAllText(args[1], Encoding.UTF8);
                        byte[] priv = PoolCodec.KeyFromBase64(File.ReadAllText(args[3]));
                        byte[] blob = PoolCodec.Pack(json, priv, args[4]);
                        File.WriteAllBytes(args[2], blob);
                        Console.WriteLine("готово: " + args[2] + " · " + blob.Length + " байт");
                        return 0;
                    }
                    case "unpack":
                    {
                        if (args.Length < 4) return Usage();
                        string pubText = File.Exists(args[2]) ? File.ReadAllText(args[2]) : args[2];
                        byte[] pub = PoolCodec.KeyFromBase64(pubText);
                        byte[] blob = File.ReadAllBytes(args[1]);
                        Console.WriteLine(PoolCodec.Unpack(blob, pub, args[3]));
                        return 0;
                    }
                    case "key":
                    {
                        // pooltool key <sub> <seed> <sni> <psk> [pk]
                        if (args.Length < 5) return Usage();
                        string pk = args.Length > 5 ? args[5] : "";
                        var sb = new StringBuilder("v2");
                        if (pk.Length > 0) sb.Append("|pk=").Append(pk);
                        sb.Append("|sub=").Append(args[1]);
                        sb.Append("|seed=").Append(args[2]);
                        sb.Append("|sni=").Append(args[3]);
                        sb.Append("|fp=chrome");
                        sb.Append("|psk=").Append(args[4]);
                        string raw = "veil://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()))
                            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                        Console.WriteLine(raw);
                        return 0;
                    }
                    case "url":
                    {
                        if (args.Length < 4) return Usage();
                        Console.WriteLine("https://" + args[1] + "/static/data/" + args[2] + "?k=" + args[3]);
                        return 0;
                    }
                }
                return Usage();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ошибка: " + ex.Message);
                return 1;
            }
        }

        static int Usage()
        {
            Console.Error.WriteLine("pooltool keygen <priv.key>");
            Console.Error.WriteLine("pooltool pub <priv.key>");
            Console.Error.WriteLine("pooltool pack <pool.json> <out.bin> <priv.key> <psk>");
            Console.Error.WriteLine("pooltool unpack <in.bin> <pub|pub.key> <psk>");
            Console.Error.WriteLine("pooltool key <sub> <seed> <sni> <psk> [pk]");
            Console.Error.WriteLine("pooltool url <host> <blob-name> <token>");
            return 2;
        }
    }
}
