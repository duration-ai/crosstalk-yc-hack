using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DataPrep;

class Fetch
{
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

    public Fetch(string cacheDir, bool refresh)
    {
        this.cacheDir = cacheDir; this.refresh = refresh;
        Directory.CreateDirectory(cacheDir);
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("crosstalk-dataprep/1.0 (hackathon game data prep)");
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

    async Task<byte[]> Retry(string url, Func<HttpRequestMessage> req)
    {
        Exception last = null;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                var resp = await Http.SendAsync(req());
                if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsByteArrayAsync();
                last = new Exception($"{(int)resp.StatusCode} from {url}");
            }
            catch (Exception e) { last = e; }
            await Task.Delay(2000 * (i + 1));
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

    public async Task<JsonNode> Overpassql(string query)
    {
        var data = await Cached(query, ".bin", async () =>
        {
            foreach (var url in Overpass)
            {
                try
                {
                    Console.WriteLine($"Overpass: {url}");
                    return await Retry(url, () => new HttpRequestMessage(HttpMethod.Post, url)
                    { Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) }) });
                }
                catch (Exception e) { Console.WriteLine($"  failed: {e.Message}"); }
            }
            throw new Exception("All Overpass endpoints failed");
        });
        return JsonNode.Parse(data);
    }
}
