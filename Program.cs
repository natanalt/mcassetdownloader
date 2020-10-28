using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO.Compression;
using System.IO;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace McAssetDownloader
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var dm = new DownloadHelper();

            if (args.Length != 2 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Usage: McAssetDownloader.exe [version, e.g. 1.16.3] [target zip path]");
                Console.WriteLine("Note: Versions below 1.7.3 will not work due to a different asset format");
                Console.WriteLine();
                Console.WriteLine("https://github.com/natanalt/mcassetdownloader <3");
                return;
            }

            var versionName = args[0];
            var archiveName = args[1];

            if (File.Exists(archiveName))
            {
                Console.Write($"File {archiveName} already exists. Overwrite it? [Y/N] ");
                var key = Console.ReadKey();
                Console.WriteLine();
                if (key.Key != ConsoleKey.Y)
                {
                    Console.WriteLine("Aborting.");
                    return;
                }
            }

            await dm.RetrieveVersionData();

            var version = dm.CachedVersions.Find(x => x.Id == versionName);
            if (version == null)
            {
                Console.WriteLine($"Version {versionName} not found");
                return;
            }

            using var client = new HttpClient();
            using var targetArchive = new ZipArchive(
                new FileStream(
                    archiveName,
                    FileMode.Create,
                    FileAccess.ReadWrite),
                ZipArchiveMode.Create);

            var meta = await dm.GetVersionMeta(versionName);
            var assets = await dm.GetAssetIndexFor(meta);

            Console.WriteLine("Retrieving client jar...");
            using var clientRequest = await client.GetAsync(meta["downloads"]["client"].Value<string>("url"));
            clientRequest.EnsureSuccessStatusCode();
            using var clientStream = await clientRequest.Content.ReadAsStreamAsync();
            using var clientArchive = new ZipArchive(clientStream, ZipArchiveMode.Read);

            var readTotal = 0;
            var totalFiles = assets.Count;
            foreach (var entry in clientArchive.Entries)
            {
                var name = entry.FullName;
                if (name != "assets/.mcassetsroot" && name.StartsWith("assets/"))
                    totalFiles += 1;
            }
            totalFiles += 1; // pack.png

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            void WriteStatus(string filename)
            {
                var percentage = (float)readTotal / totalFiles * 100;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"[{readTotal + 1}/{totalFiles} {(int)percentage}%] ");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"Writing {filename}");
                readTotal += 1;
            }

            foreach (var kv in assets)
            {
                var path = kv.Key == "pack.mcmeta" ? kv.Key : $"assets/{kv.Key}";
                WriteStatus(path);
                using var stream = targetArchive.CreateEntry(path).Open();
                using var request = await client.GetAsync(dm.GetAssetUri(kv.Value));
                request.EnsureSuccessStatusCode();
                await request.Content.CopyToAsync(stream);
            }

            WriteStatus("pack.png");
            using (var stream = targetArchive.CreateEntry("pack.png").Open())
                await clientArchive.GetEntry("pack.png").Open().CopyToAsync(stream);

            foreach (var entry in clientArchive.Entries)
            {
                var name = entry.FullName;
                if (name == "assets/.mcassetsroot" || !name.StartsWith("assets/"))
                    continue;

                WriteStatus(name);
                using (var stream = targetArchive.CreateEntry(name).Open())
                    await clientArchive.GetEntry(name).Open().CopyToAsync(stream);
            }

            Console.WriteLine($"Done! Took {(int)stopwatch.Elapsed.TotalSeconds} seconds");
        }
    }

    class VersionInfo
    {
        public string Id;
        public string Type;
        public Uri MetaUri;
        public DateTime Time;
        public DateTime ReleaseTime;
    }

    class DownloadHelper
    {
        public Uri VersionManifestUri { get; protected set; }
        public List<VersionInfo> CachedVersions { get; protected set; }

        public DownloadHelper(Uri versionManifestUri = null)
        {
            VersionManifestUri = versionManifestUri
                ?? new Uri("https://launchermeta.mojang.com/mc/game/version_manifest.json");
            CachedVersions = new List<VersionInfo>();
        }

        public async Task RetrieveVersionData()
        {
            using var client = new HttpClient();

            Console.WriteLine("Retrieving version data... Version manifest URI:");
            Console.WriteLine(VersionManifestUri);

            using var request = await client.GetAsync(VersionManifestUri);
            request.EnsureSuccessStatusCode();
            var contentBody = await request.Content.ReadAsStringAsync();

            CachedVersions.Clear();
            foreach (var entry in JObject.Parse(contentBody).Value<JArray>("versions"))
            {
                CachedVersions.Add(new VersionInfo
                {
                    Id = entry.Value<string>("id"),
                    Type = entry.Value<string>("type"),
                    MetaUri = new Uri(entry.Value<string>("url")),
                    Time = DateTime.Parse(entry.Value<string>("time")),
                    ReleaseTime = DateTime.Parse(entry.Value<string>("releaseTime")),
                });
            }
        }

        public async Task<JObject> GetVersionMeta(string versionId)
        {
            using var client = new HttpClient();
            using var metaRequest = await client.GetAsync(CachedVersions.Find(x => x.Id == versionId).MetaUri);
            metaRequest.EnsureSuccessStatusCode();
            return JObject.Parse(await metaRequest.Content.ReadAsStringAsync());
        }

        public async Task<Dictionary<string, string>> GetAssetIndexFor(JObject meta)
        {
            var versionId = meta.Value<string>("id");

            Console.WriteLine($"Retrieving asset index URI for version {versionId}");
            if (meta.Value<string>("assets") == "pre-1.6" || meta.Value<string>("assets") == "legacy")
            {
                Console.WriteLine($"Legacy or pre-1.6 asset index for version {versionId} detected.");
                Console.WriteLine($"Legacy asset formats aren't supported by McAssetDownloader");
                return null;
            }

            var totalSize = meta["assetIndex"].Value<int>("totalSize");
            Console.WriteLine($"Found asset index - total assets size: {totalSize / 1024} KiB");

            using var client = new HttpClient();
            using var indexRequest = await client.GetAsync(meta["assetIndex"].Value<string>("url"));
            indexRequest.EnsureSuccessStatusCode();

            var result = new Dictionary<string, string>();
            var index = JObject.Parse(await indexRequest.Content.ReadAsStringAsync()).Value<JObject>("objects");
            foreach (var kv in index)
                result[kv.Key] = kv.Value.Value<string>("hash");

            return result;
        }

        public Uri GetAssetUri(string hash) => new Uri($"http://resources.download.minecraft.net/{hash.Substring(0, 2)}/{hash}");
    }
}
