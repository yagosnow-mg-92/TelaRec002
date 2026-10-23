using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TelaRec.Services;

internal sealed record UploadProgress(long Sent, long Total, double BytesPerSec);

internal sealed class UploadResult
{
    public bool Success { get; init; }
    public bool Cancelled { get; init; }
    public string? Link { get; init; }
    public string? Error { get; init; }
}

internal sealed record DriveCheckResult(bool Ok, string Message);

internal sealed class DriveException : Exception
{
    public DriveException(string message) : base(message) { }
}

/// <summary>
/// Envia o MP4 (sem compactar) ao Google Drive. O script do Apps Script apenas abre uma sessão de envio
/// retomável; o arquivo vai direto do computador para o Google, em partes, com progresso e retomada.
/// </summary>
internal static class DriveUploader
{
    private static readonly HttpClient Api = new(new HttpClientHandler { AllowAutoRedirect = true })
    { Timeout = TimeSpan.FromMinutes(2) };

    // Sem redirecionamento automático: o Google responde 308 ("continue") nos envios em partes.
    private static readonly HttpClient Raw = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromMinutes(10) };

    private const int Chunk = 8 * 1024 * 1024; // múltiplo de 256 KiB, exigência do Drive

    private static string Trunc(string s) => s.Length > 200 ? s[..200] : s;

    private static async Task<JsonElement> CallScriptAsync(string url, object payload, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url.Trim())
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "text/plain"),
        };
        using var resp = await Api.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (body.TrimStart().StartsWith("<")) throw new DriveException(DescribeHtml(resp, body));
        try { using var doc = JsonDocument.Parse(body); return doc.RootElement.Clone(); }
        catch { throw new DriveException("Resposta inesperada do script: " + Trunc(body)); }
    }

    /// <summary>Traduz a página HTML devolvida pelo Google em uma explicação do que fazer.</summary>
    private static string DescribeHtml(HttpResponseMessage resp, string body)
    {
        var host = resp.RequestMessage?.RequestUri?.Host ?? "";
        var title = Regex.Match(body, @"<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        var text = Regex.Replace(body, @"<(script|style)[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
        if (text.Length > 220) text = text[..220];

        string tip;
        if (host.Contains("accounts.google", StringComparison.OrdinalIgnoreCase))
            tip = "O Google pediu login: o App da Web não está público. Em Implantar > Gerenciar implantações > lápis, deixe \"Quem pode acessar: Qualquer pessoa\", escolha \"Nova versão\" e clique em Implantar.";
        else if (text.Contains("encontrad", StringComparison.OrdinalIgnoreCase) || text.Contains("not found", StringComparison.OrdinalIgnoreCase))
            tip = "A implantação não tem o código do TelaRec (função não encontrada). Cole o código completo no editor, salve e use Implantar > Gerenciar implantações > lápis > Versão: \"Nova versão\" > Implantar.";
        else if (text.Contains("utoriza", StringComparison.OrdinalIgnoreCase) || text.Contains("uthoriz", StringComparison.OrdinalIgnoreCase))
            tip = "Falta autorizar o script. No editor, escolha a função \"autorizar\", clique em Executar e aceite as permissões; depois crie uma Nova versão da implantação.";
        else
            tip = "Confira se o App da Web está com \"Quem pode acessar: Qualquer pessoa\" e se você criou uma NOVA VERSÃO da implantação depois de colar o código.";

        return $"{tip}\n\nDetalhe técnico: HTTP {(int)resp.StatusCode}, servidor {host}, título \"{title}\": {text}";
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool IsOk(JsonElement e) =>
        e.TryGetProperty("ok", out var v) && v.ValueKind == JsonValueKind.True;

    public static async Task<DriveCheckResult> CheckAsync(string url, string key, string folderId, CancellationToken ct)
    {
        try
        {
            var r = await CallScriptAsync(url, new { key, action = "check", folderId }, ct);
            if (!IsOk(r)) return new DriveCheckResult(false, Str(r, "error") ?? "O script recusou o teste.");
            return new DriveCheckResult(true, $"Conectado! Pasta: \"{Str(r, "folderName")}\" (conta: {Str(r, "owner")}).");
        }
        catch (DriveException ex) { return new DriveCheckResult(false, ex.Message); }
        catch (HttpRequestException ex) { return new DriveCheckResult(false, "Sem conexão com o Google: " + ex.Message); }
        catch (TaskCanceledException) { return new DriveCheckResult(false, "O Google demorou demais para responder."); }
        catch (Exception ex) { return new DriveCheckResult(false, ex.Message); }
    }

    public static async Task<UploadResult> UploadAsync(string scriptUrl, string key, string folderId, string path,
        IProgress<UploadProgress> progress, CancellationToken ct)
    {
        string? session = null;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return new UploadResult { Error = "O arquivo de vídeo não existe mais." };
            long total = fi.Length;
            if (total == 0) return new UploadResult { Error = "O arquivo de vídeo está vazio." };

            var start = await CallScriptAsync(scriptUrl, new { key, action = "start", folderId, name = fi.Name, size = total }, ct);
            if (!IsOk(start)) return new UploadResult { Error = Str(start, "error") ?? "O script recusou o envio." };
            session = Str(start, "sessionUri");
            if (string.IsNullOrEmpty(session)) return new UploadResult { Error = "O script não devolveu o endereço de envio." };

            long offset = 0;
            string? finalJson = null;
            var sw = Stopwatch.StartNew();
            var samples = new Queue<(double T, long B)>();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            var buf = new byte[Chunk];
            progress.Report(new UploadProgress(0, total, 0));

            while (offset < total)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(Chunk, total - offset);
                fs.Seek(offset, SeekOrigin.Begin);
                int read = 0;
                while (read < want)
                {
                    int n = await fs.ReadAsync(buf.AsMemory(read, want - read), ct);
                    if (n <= 0) break;
                    read += n;
                }
                if (read != want) return new UploadResult { Error = "Falha ao ler o arquivo de vídeo." };

                int attempt = 0;
                while (true)
                {
                    try
                    {
                        var (code, next, body) = await PutAsync(session, buf, want, offset, total, ct);
                        if (code is 200 or 201) { finalJson = body; offset = total; break; }
                        if (code == 308) { offset = next; break; }
                        if (code is 404 or 410) return new UploadResult { Error = "A sessão de envio expirou. Tente enviar novamente." };
                        throw new IOException("HTTP " + code);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) when (attempt < 6)
                    {
                        attempt++;
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 3 * attempt)), ct);
                        try
                        {
                            // Descobre até onde o Google recebeu e continua dali.
                            var q = await QueryAsync(session, total, ct);
                            if (q.Done) { finalJson = q.Body; offset = total; }
                            else offset = q.Offset;
                            break;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { /* sem rede ainda: tenta reenviar a mesma parte */ }
                    }
                }

                double t = sw.Elapsed.TotalSeconds;
                samples.Enqueue((t, offset));
                while (samples.Count > 1 && t - samples.Peek().T > 6) samples.Dequeue();
                var first = samples.Peek();
                double speed = t > first.T ? (offset - first.B) / (t - first.T) : 0;
                progress.Report(new UploadProgress(offset, total, speed));
            }

            if (finalJson == null) return new UploadResult { Error = "O Google não confirmou o término do envio." };
            using var doc = JsonDocument.Parse(finalJson);
            var root = doc.RootElement;
            var id = Str(root, "id");
            if (string.IsNullOrEmpty(id)) return new UploadResult { Error = "O Google não devolveu o identificador do arquivo." };
            var sizeStr = Str(root, "size");
            if (long.TryParse(sizeStr, out var remote) && remote != total)
                return new UploadResult { Error = $"O tamanho no Drive ({remote} bytes) difere do arquivo local ({total} bytes). Envie novamente." };
            var link = Str(root, "webViewLink") ?? $"https://drive.google.com/file/d/{id}/view";
            return new UploadResult { Success = true, Link = link };
        }
        catch (OperationCanceledException)
        {
            if (session != null) _ = CancelSessionAsync(session);
            return new UploadResult { Cancelled = true, Error = "Envio cancelado." };
        }
        catch (DriveException ex) { return new UploadResult { Error = ex.Message }; }
        catch (HttpRequestException ex) { return new UploadResult { Error = "Sem conexão com a internet ou com o Google: " + ex.Message }; }
        catch (Exception ex) { return new UploadResult { Error = ex.Message }; }
    }

    private static async Task<(int Code, long Next, string Body)> PutAsync(string session, byte[] buf, int len,
        long offset, long total, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, session) { Content = new ByteArrayContent(buf, 0, len) };
        req.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + len - 1, total);
        using var resp = await Raw.SendAsync(req, ct);
        int code = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync(ct);
        long next = offset + len;
        if (code == 308) next = ParseRange(resp, offset + len);
        return (code, next, body);
    }

    private static async Task<(bool Done, long Offset, string Body)> QueryAsync(string session, long total, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, session) { Content = new ByteArrayContent(Array.Empty<byte>()) };
        req.Content.Headers.ContentRange = new ContentRangeHeaderValue(total); // "bytes */total"
        using var resp = await Raw.SendAsync(req, ct);
        int code = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (code is 200 or 201) return (true, total, body);
        if (code == 308) return (false, ParseRange(resp, 0), body);
        throw new IOException("HTTP " + code);
    }

    private static long ParseRange(HttpResponseMessage resp, long whenMissing)
    {
        // "Range: bytes=0-8388607" → próximo byte é 8388608. Sem o cabeçalho, nada foi recebido ainda.
        if (resp.Headers.TryGetValues("Range", out var vals))
        {
            var v = vals.FirstOrDefault();
            var dash = v?.LastIndexOf('-') ?? -1;
            if (v != null && dash >= 0 && long.TryParse(v[(dash + 1)..], out var last)) return last + 1;
        }
        return whenMissing == 0 ? 0 : whenMissing;
    }

    private static async Task CancelSessionAsync(string session)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var req = new HttpRequestMessage(HttpMethod.Delete, session);
            using var _ = await Raw.SendAsync(req, cts.Token);
        }
        catch { /* melhor esforço */ }
    }
}
