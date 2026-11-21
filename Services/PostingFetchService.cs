using System.Net;
using System.Net.Sockets;
using System.Text;
using HtmlAgilityPack;

namespace JobHuntHQ.Services;

/// <summary>Result of a posting-page fetch: on success, the page title (if any) and the extracted posting text.</summary>
public sealed record PostingFetchResult(bool Success, string? Title, string? Text)
{
    public static readonly PostingFetchResult Failed = new(false, null, null);
}

/// <summary>
/// Fetches a job posting page server-side and extracts readable text from it
/// (M4 phase 2, "Fetch from URL" on the Add posting page). The fetch only
/// fills the form — it never saves anything; the user reviews and edits.
///
/// SSRF posture (this is a public app fetching arbitrary user-supplied URLs):
/// - http/https only; URLs with embedded credentials are rejected.
/// - The host is resolved and EVERY resolved address must be publicly
///   routable: loopback, private (RFC 1918), link-local, CGNAT shared space,
///   multicast, and IPv6 unique-local/link-local are all rejected.
/// - Redirects are followed manually (the HttpClient handler has
///   auto-redirect disabled) and each redirect target is re-validated the
///   same way before it is requested, so a public URL cannot bounce the
///   fetcher at an internal address.
/// - 10-second overall timeout, 2 MB response cap, HTML content only.
/// Every failure — guard, network, or parse — returns the same friendly
/// message; no internal details leak to the page.
/// </summary>
public class PostingFetchService
{
    public const string FailureMessage = "Couldn't fetch that page — paste the posting text below instead.";
    public const string SuccessMessage = "Fetched the posting from the URL — check the text below and fill in anything missing, then save.";

    private const int MaxRedirects = 5;
    private const int MaxBodyBytes = 2 * 1024 * 1024;
    private const int MaxTextChars = 50_000;
    private const int MaxTitleChars = 200;
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;

    public PostingFetchService(HttpClient http)
    {
        _http = http;
    }

    public async Task<PostingFetchResult> FetchAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return PostingFetchResult.Failed;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FetchTimeout);

        try
        {
            var current = uri;
            for (var redirectCount = 0; ; redirectCount++)
            {
                // Validate the initial URL and every redirect target alike.
                if (!await IsPubliclyRoutableAsync(current, timeout.Token))
                {
                    return PostingFetchResult.Failed;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                    "(KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");

                using var response = await _http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                var status = (int)response.StatusCode;
                if (status is >= 300 and < 400)
                {
                    if (redirectCount >= MaxRedirects || response.Headers.Location is null)
                    {
                        return PostingFetchResult.Failed;
                    }
                    current = new Uri(current, response.Headers.Location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return PostingFetchResult.Failed;
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not null
                    && mediaType is not "text/html"
                    && mediaType is not "application/xhtml+xml")
                {
                    return PostingFetchResult.Failed;
                }

                var html = await ReadBodyCappedAsync(response, timeout.Token);
                return Extract(html);
            }
        }
        catch (Exception)
        {
            // Timeout, DNS, TLS, connection refused, malformed HTML — one
            // friendly outcome for all of them.
            return PostingFetchResult.Failed;
        }
    }

    // ---------- SSRF guards ----------

    private static async Task<bool> IsPubliclyRoutableAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Every resolved address must be public — a single private address
        // in the list is enough to reject (DNS rebinding / round-robin).
        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            if (b[0] == 0) return false;                                   // 0.0.0.0/8 — "this host"
            if (b[0] == 10) return false;                                  // 10.0.0.0/8 — private
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;     // 172.16.0.0/12 — private
            if (b[0] == 192 && b[1] == 168) return false;                  // 192.168.0.0/16 — private
            if (b[0] == 169 && b[1] == 254) return false;                  // 169.254.0.0/16 — link-local
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;    // 100.64.0.0/10 — CGNAT shared
            if (b[0] >= 224) return false;                                 // 224.0.0.0/4 — multicast (+ reserved)
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast)
            {
                return false;
            }
            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                       // fc00::/7 — unique local
            return true;
        }

        return false;
    }

    // ---------- Body reading ----------

    private static async Task<string> ReadBodyCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while (buffer.Length < MaxBodyBytes
               && (read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, MaxBodyBytes - buffer.Length)), cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        var charset = response.Content.Headers.ContentType?.CharSet;
        var encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }
        }
        return encoding.GetString(buffer.ToArray());
    }

    // ---------- Readability-lite extraction ----------

    private static PostingFetchResult Extract(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = ExtractTitle(doc);

        // Strip everything that is never posting content.
        foreach (var tag in new[] { "script", "style", "nav", "footer", "header", "aside", "form", "noscript", "iframe", "svg", "button" })
        {
            var nodes = doc.DocumentNode.SelectNodes("//" + tag);
            if (nodes is not null)
            {
                foreach (var node in nodes.ToList())
                {
                    node.Remove();
                }
            }
        }

        // Candidate containers: <article>, <main>, and anything whose class/id
        // smells like a job posting. Take the longest substantial candidate;
        // fall back to the whole body when no candidate qualifies.
        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        var bestText = "";

        var candidates = new List<HtmlNode>();
        var articles = doc.DocumentNode.SelectNodes("//article");
        if (articles is not null)
        {
            candidates.AddRange(articles);
        }
        var main = doc.DocumentNode.SelectSingleNode("//main");
        if (main is not null)
        {
            candidates.Add(main);
        }
        foreach (var node in doc.DocumentNode.Descendants().Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var marker = (node.GetAttributeValue("class", "") + " " + node.GetAttributeValue("id", ""))
                .ToLowerInvariant();
            if (marker.Contains("job") || marker.Contains("posting")
                || marker.Contains("description") || marker.Contains("vacancy"))
            {
                candidates.Add(node);
            }
        }

        foreach (var candidate in candidates.Distinct())
        {
            var text = NodeToText(candidate);
            if (text.Length >= 300 && text.Length > bestText.Length)
            {
                bestText = text;
            }
        }
        if (bestText.Length == 0)
        {
            bestText = NodeToText(body);
        }

        if (string.IsNullOrWhiteSpace(bestText))
        {
            return PostingFetchResult.Failed;
        }
        if (bestText.Length > MaxTextChars)
        {
            bestText = bestText[..MaxTextChars];
        }
        return new PostingFetchResult(true, title, bestText);
    }

    private static string? ExtractTitle(HtmlDocument doc)
    {
        var raw = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")
                      ?.GetAttributeValue("content", null)
                  ?? doc.DocumentNode.SelectSingleNode("//title")?.InnerText;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var title = HtmlEntity.DeEntitize(raw).Trim();
        if (title.Length > MaxTitleChars)
        {
            title = title[..MaxTitleChars];
        }
        return title;
    }

    private static string NodeToText(HtmlNode node)
    {
        var sb = new StringBuilder();
        Walk(node, sb);
        return NormalizeWhitespace(sb.ToString());
    }

    private static void Walk(HtmlNode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                sb.Append(HtmlEntity.DeEntitize(child.InnerText));
            }
            else if (child.NodeType == HtmlNodeType.Element)
            {
                var name = child.Name.ToLowerInvariant();
                if (name == "br")
                {
                    sb.Append('\n');
                    continue;
                }
                var isBlock = name is "p" or "div" or "section" or "article" or "li" or "ul" or "ol"
                    or "tr" or "table" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                    or "blockquote" or "pre";
                if (isBlock)
                {
                    sb.Append('\n');
                }
                if (name == "li")
                {
                    sb.Append("• ");
                }
                Walk(child, sb);
                if (isBlock)
                {
                    sb.Append('\n');
                }
            }
        }
    }

    private static string NormalizeWhitespace(string text)
    {
        var sb = new StringBuilder();
        var blankPending = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = string.Join(" ", rawLine.Split(
                new[] { ' ', '\t', '\r', ' ' },
                StringSplitOptions.RemoveEmptyEntries));
            if (line.Length == 0)
            {
                blankPending = sb.Length > 0;
                continue;
            }
            if (blankPending)
            {
                sb.AppendLine();
                blankPending = false;
            }
            sb.AppendLine(line);
        }
        return sb.ToString().Trim();
    }
}
