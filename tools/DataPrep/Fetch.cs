using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DataPrep;

class Fetch
{
    public const string UserAgent = "crosstalk-dogpatch-dataprep/1.0 (+https://github.com/duration-ai/crosstalk-yc-hack)";
    public const string SodaBase = "https://data.sf.gov/resource/";

    static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(6) };
    static readonly string[] Overpass =
    {
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
        "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
    };
    readonly string cacheDir;
    readonly bool refresh;
    public int Requests;
    public readonly List<string> Failures = new();

    public Fetch(string cacheDir, bool refresh)
    {
        this.cacheDir = cacheDir; this.refresh = refresh;
        Directory.CreateDirectory(cacheDir);
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    async Task<byte[]> Cached(string key, string ext, Func<Task<byte[]>> get)
    {
        var path = Path.Combine(cacheDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24] + ext);
        if (!refresh && File.Exists(path)) return File.ReadAllBytes(path);
        Requests++;
        var data = await get();
        File.WriteAllBytes(path, data);
        return data;
    }

    /// <summary>GET/POST with exponential backoff on 429, 408, 5xx, timeouts and transport errors. Other 4xx fail immediately.</summary>
    async Task<byte[]> Retry(string url, Func<HttpRequestMessage> req, int attempts = 6)
    {
        Exception last = null;
        for (int i = 0; i < attempts; i++)
        {
            TimeSpan delay = TimeSpan.FromSeconds(3 * Math.Pow(2, i) + Random.Shared.NextDouble());
            try
            {
                using var resp = await Http.SendAsync(req());
                if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsByteArrayAsync();
                int code = (int)resp.StatusCode;
                last = new Exception($"{code} from {url}");
                if (code != 429 && code != 408 && code < 500) throw new InvalidOperationException(last.Message);
                if (resp.Headers.RetryAfter?.Delta is { } ra) delay = ra + TimeSpan.FromSeconds(1);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { last = e; }
            if (i < attempts - 1) { Console.WriteLine($"  retry {i + 1}/{attempts - 1} in {delay.TotalSeconds:F0}s ({last.Message})"); await Task.Delay(delay); }
        }
        throw last;
    }

    public Task<byte[]> Get(string url, string accept = null) =>
        Cached(url, ".bin", () => Retry(url, () =>
        {
            var r = new HttpRequestMessage(HttpMethod.Get, url);
            if (accept != null) r.Headers.Accept.ParseAdd(accept);
            return r;
        }));

    public async Task<JsonNode> Json(string url, string accept = "application/json") =>
        JsonNode.Parse(await Get(url, accept));

    /// <summary>One Socrata (DataSF) query, bounded by the caller's $where (within_box / lat-lon range).</summary>
    public async Task<JsonArray> Soda(string dataset, string select, string where, string order = null, int limit = 50000)
    {
        var url = SodaBase + dataset + ".json?$select=" + Uri.EscapeDataString(select) + "&$where=" + Uri.EscapeDataString(where) +
                  (order != null ? "&$order=" + Uri.EscapeDataString(order) : "") + "&$limit=" + limit;
        var rows = (await Json(url)).AsArray();
        if (rows.Count >= limit) Console.WriteLine($"WARNING: {dataset} returned the row limit ({limit}); data may be truncated");
        return rows;
    }

    public async Task<JsonNode> Overpassql(string query)
    {
        var data = await Cached(query, ".bin", async () =>
        {
            foreach (var url in Overpass)
            {
                try
                {
                    Console.WriteLine($"Overpass: {url}");
                    var body = await Retry(url, () => new HttpRequestMessage(HttpMethod.Post, url)
                    { Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) }) });
                    var probe = JsonNode.Parse(body);
                    var remark = probe?["remark"]?.GetValue<string>();
                    if (probe?["elements"] == null || (remark != null && remark.Contains("runtime error"))) throw new Exception("Overpass remark: " + remark);
                    return body;
                }
                catch (Exception e) { Console.WriteLine($"  failed: {e.Message}"); Failures.Add($"overpass {url}: {e.Message}"); }
            }
            throw new Exception("All Overpass endpoints failed");
        });
        return JsonNode.Parse(data);
    }
}
