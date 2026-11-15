using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nimbus.AceV4.Web
{
    public sealed record WebResult(string Title, string Url, string Snippet)
    {
        public string Host
        {
            get
            {
                try { return new Uri(Url).Host.Replace("www.", ""); } catch { return ""; }
            }
        }
    }

    public sealed record RecipeLookup(RecipeCard? Recipe, List<WebResult> Results);

    public sealed record WikiSummary(string Title, string Extract, string Url, string ImageUrl, string Description);

    public sealed record Definition(string Word, string Phonetic, List<(string Part, string Meaning, string Example)> Meanings);

    public sealed record WeatherReport(string Place, double TempNow, double FeelsLike, string Sky, double Wind, int Humidity,
                                       bool Fahrenheit, List<(DateTime Day, string Sky, double High, double Low, int Rain)> Days);

    /// <summary>
    /// Ace's way onto the internet: web search, reading pages, Wikipedia, word definitions, weather and recipes.
    /// No account or key needed. Every call has a timeout so Ace never hangs on a slow site.
    /// </summary>
    public static class WebClient
    {
        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 6,
            };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Nimbus-Ace/4");
            c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            return c;
        }

        public static string LastProblem { get; private set; } = "";

        /// <summary>Turns every web request off (offline tests, or "work offline").</summary>
        public static bool Disabled { get; set; }

        private static async Task<string?> GetAsync(string url, CancellationToken ct, int maxChars = 1_500_000)
        {
            if (Disabled) { LastProblem = "the web is turned off"; return null; }
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { LastProblem = $"{(int)resp.StatusCode} from {Host(url)}"; return null; }
                string? type = resp.Content.Headers.ContentType?.MediaType;
                if (type != null && !(type.Contains("html") || type.Contains("json") || type.Contains("text") || type.Contains("xml"))) return null;
                var text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                return text.Length > maxChars ? text[..maxChars] : text;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { LastProblem = Host(url) + " took too long"; return null; }
            catch (Exception ex) { LastProblem = ex.Message; return null; }
        }

        /// <summary>Downloads a page or JSON as text (null if offline or it failed; see LastProblem).</summary>
        public static Task<string?> FetchAsync(string url, CancellationToken ct = default) => GetAsync(url, ct);

        private static string Host(string url) { try { return new Uri(url).Host; } catch { return url; } }

        public static async Task<bool> OnlineAsync(CancellationToken ct = default)
        {
            if (Disabled) return false;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(4));
                using var resp = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "https://www.msftconnecttest.com/connecttest.txt"), cts.Token).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // ===================== Search =====================

        /// <summary>Web results for a question: DuckDuckGo first, Bing if that fails.</summary>
        public static async Task<List<WebResult>> SearchAsync(string query, int take = 8, CancellationToken ct = default)
        {
            var list = await DuckDuckGoAsync(query, ct).ConfigureAwait(false);
            if (list.Count == 0) list = await BingAsync(query, ct).ConfigureAwait(false);
            return list.Where(r => r.Url.StartsWith("http")).GroupBy(r => r.Url).Select(g => g.First()).Take(take).ToList();
        }

        private static async Task<List<WebResult>> DuckDuckGoAsync(string query, CancellationToken ct)
        {
            var list = new List<WebResult>();
            string? html = await GetAsync("https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query), ct).ConfigureAwait(false);
            if (html == null) return list;
            var blocks = Regex.Split(html, @"<div class=""result results_links");
            foreach (var b in blocks.Skip(1))
            {
                if (b.Contains("result--ad")) continue;
                var a = Regex.Match(b, @"<a[^>]*class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline);
                if (!a.Success) continue;
                string href = WebUtility.HtmlDecode(a.Groups[1].Value);
                var real = Regex.Match(href, @"[?&]uddg=([^&]+)");
                if (real.Success) href = Uri.UnescapeDataString(real.Groups[1].Value);
                if (href.StartsWith("//")) href = "https:" + href;
                if (href.Contains("duckduckgo.com/y.js")) continue;   // ads
                var sn = Regex.Match(b, @"class=""result__snippet""[^>]*>(.*?)</(?:a|div)>", RegexOptions.Singleline);
                list.Add(new WebResult(Text(a.Groups[2].Value), href, sn.Success ? Text(sn.Groups[1].Value) : ""));
            }
            return list;
        }

        private static async Task<List<WebResult>> BingAsync(string query, CancellationToken ct)
        {
            var list = new List<WebResult>();
            string? html = await GetAsync("https://www.bing.com/search?form=QBLH&q=" + Uri.EscapeDataString(query), ct).ConfigureAwait(false);
            if (html == null) return list;
            foreach (Match m in Regex.Matches(html, @"<li class=""b_algo""(.*?)</li>", RegexOptions.Singleline))
            {
                string b = m.Groups[1].Value;
                var a = Regex.Match(b, @"<h2[^>]*>\s*<a[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline);
                if (!a.Success) continue;
                string href = WebUtility.HtmlDecode(a.Groups[1].Value);
                // Bing sometimes wraps links: bing.com/ck/a?...&u=a1<base64>
                var u = Regex.Match(href, @"[?&]u=a1([^&]+)");
                if (href.Contains("bing.com/ck/") && u.Success)
                {
                    try
                    {
                        string b64 = u.Groups[1].Value.Replace('-', '+').Replace('_', '/');
                        while (b64.Length % 4 != 0) b64 += "=";
                        href = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                    }
                    catch { }
                }
                var p = Regex.Match(b, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline);
                list.Add(new WebResult(Text(a.Groups[2].Value), href, p.Success ? Text(p.Groups[1].Value) : ""));
            }
            return list;
        }

        /// <summary>DuckDuckGo's instant answer (a one-line answer or a short abstract), when there is one.</summary>
        public static async Task<(string Text, string Source, string Url)?> InstantAnswerAsync(string query, CancellationToken ct = default)
        {
            string? json = await GetAsync("https://api.duckduckgo.com/?format=json&no_html=1&skip_disambig=1&q=" + Uri.EscapeDataString(query), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var r = doc.RootElement;
                string answer = Str(r, "Answer");
                if (answer.Length > 0 && answer.Length < 400) return (answer, "DuckDuckGo", "");
                string abs = Str(r, "AbstractText");
                if (abs.Length > 30) return (abs, Str(r, "AbstractSource"), Str(r, "AbstractURL"));
                string def = Str(r, "Definition");
                if (def.Length > 10) return (def, Str(r, "DefinitionSource"), Str(r, "DefinitionURL"));
            }
            catch { }
            return null;
        }

        // ===================== Reading pages =====================

        /// <summary>The readable text of a page (paragraphs, list items and headings), without menus and scripts.</summary>
        public static async Task<(string Title, string Text, string Html)?> ReadPageAsync(string url, CancellationToken ct = default)
        {
            string? html = await GetAsync(url, ct).ConfigureAwait(false);
            if (html == null) return null;
            return (PageTitle(html), MainText(html), html);
        }

        public static string PageTitle(string html)
        {
            var og = Regex.Match(html, @"<meta[^>]+property=""og:title""[^>]+content=""([^""]*)""", RegexOptions.IgnoreCase);
            if (og.Success) return Text(og.Groups[1].Value);
            var t = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return t.Success ? Text(t.Groups[1].Value) : "";
        }

        public static string MainText(string html)
        {
            string h = Regex.Replace(html, @"<(script|style|noscript|svg|nav|header|footer|aside|form|iframe)\b.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            h = Regex.Replace(h, @"<!--.*?-->", " ", RegexOptions.Singleline);
            var article = Regex.Match(h, @"<(article|main)\b.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (article.Success && article.Length > 1500) h = article.Value;
            var parts = new List<string>();
            foreach (Match m in Regex.Matches(h, @"<(p|li|h[1-4]|dd|td|blockquote)\b[^>]*>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                string t = Text(m.Groups[2].Value);
                if (t.Length < 25 && !m.Groups[1].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase)) continue;
                if (Regex.IsMatch(t, @"cookie|subscribe|sign up|newsletter|all rights reserved|javascript", RegexOptions.IgnoreCase) && t.Length < 160) continue;
                parts.Add(t.EndsWith('.') || t.EndsWith('?') || t.EndsWith('!') || t.EndsWith(':') ? t : t + ".");
            }
            string text = string.Join(" ", parts);
            return text.Length > 60_000 ? text[..60_000] : text;
        }

        /// <summary>HTML fragment to plain text.</summary>
        public static string Text(string html)
        {
            string s = Regex.Replace(html, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]+>", "");
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, @"\[\d+\]", "");     // Wikipedia-style [1] references
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        // ===================== Wikipedia =====================

        public static async Task<WikiSummary?> WikipediaAsync(string topic, CancellationToken ct = default)
        {
            try
            {
                string? found = await GetAsync("https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=1&srsearch=" + Uri.EscapeDataString(topic), ct).ConfigureAwait(false);
                if (found == null) return null;
                string title;
                using (var doc = JsonDocument.Parse(found))
                {
                    var hits = doc.RootElement.GetProperty("query").GetProperty("search");
                    if (hits.GetArrayLength() == 0) return null;
                    title = hits[0].GetProperty("title").GetString() ?? "";
                }
                string? json = await GetAsync("https://en.wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(title.Replace(' ', '_')), ct).ConfigureAwait(false);
                if (json == null) return null;
                using var s = JsonDocument.Parse(json);
                var r = s.RootElement;
                if (Str(r, "type") == "disambiguation") return null;
                string url = r.TryGetProperty("content_urls", out var cu) && cu.TryGetProperty("desktop", out var d) ? Str(d, "page") : "";
                string img = r.TryGetProperty("thumbnail", out var th) ? Str(th, "source") : "";
                return new WikiSummary(Str(r, "title"), Str(r, "extract"), url, img, Str(r, "description"));
            }
            catch { return null; }
        }

        /// <summary>The full plain text of a Wikipedia article (for questions the summary doesn't answer).</summary>
        public static async Task<string?> WikipediaFullAsync(string title, CancellationToken ct = default)
        {
            try
            {
                string? json = await GetAsync("https://en.wikipedia.org/w/api.php?action=query&prop=extracts&explaintext=1&format=json&redirects=1&titles=" + Uri.EscapeDataString(title), ct).ConfigureAwait(false);
                if (json == null) return null;
                using var doc = JsonDocument.Parse(json);
                foreach (var page in doc.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject())
                {
                    string text = Str(page.Value, "extract");
                    if (text.Length > 0) return Regex.Replace(text, @"==+[^=]+==+", " ");
                }
            }
            catch { }
            return null;
        }

        // ===================== Dictionary =====================

        public static async Task<Definition?> DefineAsync(string word, CancellationToken ct = default)
        {
            try
            {
                string? json = await GetAsync("https://api.dictionaryapi.dev/api/v2/entries/en/" + Uri.EscapeDataString(word.Trim().ToLowerInvariant()), ct).ConfigureAwait(false);
                if (json == null) return null;
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;
                var first = doc.RootElement[0];
                string phon = Str(first, "phonetic");
                if (phon.Length == 0 && first.TryGetProperty("phonetics", out var ps))
                    foreach (var p in ps.EnumerateArray()) { phon = Str(p, "text"); if (phon.Length > 0) break; }
                var list = new List<(string, string, string)>();
                foreach (var entry in doc.RootElement.EnumerateArray())
                {
                    if (!entry.TryGetProperty("meanings", out var ms)) continue;
                    foreach (var m in ms.EnumerateArray())
                    {
                        string part = Str(m, "partOfSpeech");
                        if (!m.TryGetProperty("definitions", out var defs)) continue;
                        foreach (var d in defs.EnumerateArray().Take(2))
                            list.Add((part, Str(d, "definition"), Str(d, "example")));
                    }
                }
                return list.Count == 0 ? null : new Definition(Str(first, "word"), phon, list.Take(5).ToList());
            }
            catch { return null; }
        }

        // ===================== Weather =====================

        public static bool UsesFahrenheit => !RegionInfo.CurrentRegion.IsMetric;

        /// <summary>Weather for a place (or for where this PC is, when place is empty). Uses Open-Meteo, which needs no key.</summary>
        public static async Task<WeatherReport?> WeatherAsync(string place, CancellationToken ct = default)
        {
            var loc = string.IsNullOrWhiteSpace(place) ? await HereAsync(ct).ConfigureAwait(false) : await GeocodeAsync(place, ct).ConfigureAwait(false);
            if (loc == null) return null;
            bool f = UsesFahrenheit;
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + loc.Value.Lat.ToString(CultureInfo.InvariantCulture)
                       + "&longitude=" + loc.Value.Lon.ToString(CultureInfo.InvariantCulture)
                       + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,relative_humidity_2m"
                       + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=4"
                       + (f ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "");
            string? json = await GetAsync(url, ct).ConfigureAwait(false);
            if (json == null) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var cur = doc.RootElement.GetProperty("current");
                var daily = doc.RootElement.GetProperty("daily");
                var days = new List<(DateTime, string, double, double, int)>();
                var time = daily.GetProperty("time");
                for (int i = 0; i < time.GetArrayLength(); i++)
                {
                    DateTime.TryParse(time[i].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var day);
                    days.Add((day, Sky(Num(daily.GetProperty("weather_code")[i])), Num(daily.GetProperty("temperature_2m_max")[i]),
                              Num(daily.GetProperty("temperature_2m_min")[i]), (int)Num(daily.GetProperty("precipitation_probability_max")[i])));
                }
                return new WeatherReport(loc.Value.Name, Num(cur.GetProperty("temperature_2m")), Num(cur.GetProperty("apparent_temperature")),
                    Sky(Num(cur.GetProperty("weather_code"))), Num(cur.GetProperty("wind_speed_10m")), (int)Num(cur.GetProperty("relative_humidity_2m")), f, days);
            }
            catch { return null; }
        }

        /// <summary>The time zone of a place ("Tokyo" → Asia/Tokyo) and its proper name.</summary>
        public static async Task<(string Name, string Zone)?> TimeZoneOfAsync(string place, CancellationToken ct = default)
        {
            string? json = await GetAsync("https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&name=" + Uri.EscapeDataString(place.Trim()), ct).ConfigureAwait(false);
            if (json == null) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var rs) || rs.GetArrayLength() == 0) return null;
                string zone = Str(rs[0], "timezone");
                return zone.Length == 0 ? null : (Str(rs[0], "name"), zone);
            }
            catch { return null; }
        }

        private static async Task<(string Name, double Lat, double Lon)?> GeocodeAsync(string place, CancellationToken ct)
        {
            string? json = await GetAsync("https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&name=" + Uri.EscapeDataString(place.Trim()), ct).ConfigureAwait(false);
            if (json == null) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var rs) || rs.GetArrayLength() == 0) return null;
                var r = rs[0];
                string name = Str(r, "name");
                string region = Str(r, "admin1");
                if (region.Length > 0 && region != name) name += ", " + region;
                return (name, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble());
            }
            catch { return null; }
        }

        /// <summary>Where this PC is: Windows location if it's turned on, otherwise a rough guess from the internet connection.</summary>
        private static async Task<(string Name, double Lat, double Lon)?> HereAsync(CancellationToken ct)
        {
            try
            {
                var access = await Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
                if (access == Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed)
                {
                    var geo = new Windows.Devices.Geolocation.Geolocator { DesiredAccuracy = Windows.Devices.Geolocation.PositionAccuracy.Default };
                    var op = geo.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(5)).AsTask(ct);
                    var pos = await op.ConfigureAwait(false);
                    var p = pos.Coordinate.Point.Position;
                    return ("your area", p.Latitude, p.Longitude);
                }
            }
            catch { }
            string? json = await GetAsync("http://ip-api.com/json/?fields=status,city,regionName,lat,lon", ct).ConfigureAwait(false);
            if (json == null) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var r = doc.RootElement;
                if (Str(r, "status") != "success") return null;
                return (Str(r, "city"), r.GetProperty("lat").GetDouble(), r.GetProperty("lon").GetDouble());
            }
            catch { return null; }
        }

        public static string Sky(double code) => (int)code switch
        {
            0 => "clear",
            1 => "mostly clear",
            2 => "partly cloudy",
            3 => "cloudy",
            45 or 48 => "foggy",
            51 or 53 or 55 => "drizzly",
            56 or 57 => "freezing drizzle",
            61 => "light rain",
            63 => "rainy",
            65 => "heavy rain",
            66 or 67 => "freezing rain",
            71 => "light snow",
            73 => "snowy",
            75 => "heavy snow",
            77 => "snow grains",
            80 or 81 => "rain showers",
            82 => "heavy showers",
            85 or 86 => "snow showers",
            95 => "thunderstorms",
            96 or 99 => "thunderstorms with hail",
            _ => "mixed",
        };

        // ===================== Recipes =====================

        private static readonly string[] RecipeSites = { "allrecipes.com", "simplyrecipes.com", "bbcgoodfood.com", "foodnetwork.com", "seriouseats.com", "budgetbytes.com", "tasteofhome.com", "delish.com", "bonappetit.com", "recipetineats.com", "food.com", "epicurious.com", "sallysbakingaddiction.com", "kingarthurbaking.com" };

        /// <summary>Finds a recipe and reads its ingredients and steps (from the recipe data most recipe sites include).</summary>
        public static async Task<RecipeLookup> RecipeAsync(string dish, Action<string>? progress, CancellationToken ct = default)
        {
            var results = await SearchAsync(dish + " recipe", 10, ct).ConfigureAwait(false);
            // Known recipe sites first; they almost always include clean recipe data.
            var ordered = results.OrderByDescending(r => RecipeSites.Any(s => r.Host.EndsWith(s))).ToList();
            int tried = 0;
            foreach (var r in ordered)
            {
                if (ct.IsCancellationRequested || tried >= 5) break;
                tried++;
                progress?.Invoke("Reading " + r.Host);
                string? html = await GetAsync(r.Url, ct).ConfigureAwait(false);
                if (html == null) continue;
                var card = RecipeFromHtml(html, r.Url);
                if (card != null && card.Ingredients.Count >= 2) return new RecipeLookup(card, results);
            }
            return new RecipeLookup(null, results);
        }

        public static RecipeCard? RecipeFromHtml(string html, string url)
        {
            foreach (Match m in Regex.Matches(html, @"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                try
                {
                    using var doc = JsonDocument.Parse(m.Groups[1].Value.Trim(), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    var recipe = FindRecipe(doc.RootElement);
                    if (recipe == null) continue;
                    var r = recipe.Value;
                    var card = new RecipeCard
                    {
                        Title = Text(Str(r, "name")),
                        Url = url,
                        Source = Host(url).Replace("www.", ""),
                        ImageUrl = ImageOf(r),
                        Yield = YieldOf(r),
                        TotalTime = Duration(Str(r, "totalTime")) is { Length: > 0 } t ? t
                                  : Duration(Str(r, "cookTime")) is { Length: > 0 } c ? c : Duration(Str(r, "prepTime")),
                    };
                    if (r.TryGetProperty("recipeIngredient", out var ing) && ing.ValueKind == JsonValueKind.Array)
                        foreach (var i in ing.EnumerateArray()) { string s = Text(i.ToString()); if (s.Length > 0) card.Ingredients.Add(s); }
                    if (r.TryGetProperty("recipeInstructions", out var ins)) Steps(ins, card.Steps);
                    card.Ingredients = card.Ingredients.Distinct().Take(40).ToList();
                    card.Steps = card.Steps.Where(s => s.Length > 3).Take(30).ToList();
                    return card;
                }
                catch { }
            }
            return null;
        }

        private static JsonElement? FindRecipe(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray()) { var f = FindRecipe(x); if (f != null) return f; }
                    break;
                case JsonValueKind.Object:
                    if (e.TryGetProperty("@type", out var type))
                    {
                        bool isRecipe = type.ValueKind == JsonValueKind.String ? type.GetString() == "Recipe"
                                      : type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "Recipe");
                        if (isRecipe) return e;
                    }
                    if (e.TryGetProperty("@graph", out var graph)) { var f = FindRecipe(graph); if (f != null) return f; }
                    if (e.TryGetProperty("mainEntity", out var main)) { var f = FindRecipe(main); if (f != null) return f; }
                    break;
            }
            return null;
        }

        private static void Steps(JsonElement e, List<string> into)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                    foreach (var s in Regex.Split(Text(e.GetString() ?? ""), @"(?<=\.)\s+(?=[A-Z])|\n"))
                        if (s.Trim().Length > 3) into.Add(s.Trim());
                    break;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray()) Steps(x, into);
                    break;
                case JsonValueKind.Object:
                    if (e.TryGetProperty("itemListElement", out var items)) Steps(items, into);
                    else if (e.TryGetProperty("text", out var t)) into.Add(Text(t.ToString()));
                    else if (e.TryGetProperty("name", out var n)) into.Add(Text(n.ToString()));
                    break;
            }
        }

        private static string ImageOf(JsonElement r)
        {
            if (!r.TryGetProperty("image", out var img)) return "";
            return img.ValueKind switch
            {
                JsonValueKind.String => img.GetString() ?? "",
                JsonValueKind.Array when img.GetArrayLength() > 0 => img[0].ValueKind == JsonValueKind.String ? img[0].GetString() ?? "" : Str(img[0], "url"),
                JsonValueKind.Object => Str(img, "url"),
                _ => "",
            };
        }

        private static string YieldOf(JsonElement r)
        {
            if (!r.TryGetProperty("recipeYield", out var y)) return "";
            string s = y.ValueKind == JsonValueKind.Array ? (y.EnumerateArray().Select(v => v.ToString()).OrderByDescending(v => v.Length).FirstOrDefault() ?? "") : y.ToString();
            s = Text(s);
            return Regex.IsMatch(s, @"^\d+$") ? s + " servings" : s;
        }

        /// <summary>"PT1H30M" to "1 hr 30 min".</summary>
        public static string Duration(string iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return "";
            try
            {
                var t = System.Xml.XmlConvert.ToTimeSpan(iso.Trim());
                if (t <= TimeSpan.Zero) return "";
                int hours = (int)t.TotalHours;
                return hours > 0 ? (t.Minutes > 0 ? $"{hours} hr {t.Minutes} min" : $"{hours} hr") : $"{t.Minutes} min";
            }
            catch { return ""; }
        }

        // ===================== Helpers =====================

        private static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static double Num(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0;
    }
}

