using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace NeedleBase.Mobile.Services.Updates;

/// <summary>A failure with a message that can be shown to the user as is.</summary>
public sealed class UpdateException(string userMessage, Exception? inner = null) : Exception(userMessage, inner);

/// <summary>
/// Downloads the APK into <c>needlebase-&lt;versionCode&gt;.apk.part</c>, resuming with an HTTP Range request after an
/// interruption (GitHub Pages supports ranges; a server that ignores the range just sends the whole file again).
/// The file only gets its final name after its size and SHA-256 match the manifest, so a half or tampered download
/// can never be installed.
/// </summary>
public sealed class UpdateDownloader(HttpClient http, TimeSpan? stallTimeout = null)
{
    private const int BufferSize = 81920;

    /// <summary>No byte for this long = dropped connection (keeps the .part for resume) instead of hanging forever.</summary>
    private readonly TimeSpan _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(60);

    public static string FinalPath(string directory, UpdateManifest manifest) =>
        Path.Combine(directory, $"needlebase-{manifest.VersionCode}.apk");

    public async Task<string> DownloadAsync(UpdateManifest manifest, string directory, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var final = FinalPath(directory, manifest);
        if (File.Exists(final))
        {
            if (new FileInfo(final).Length == manifest.Size && await HashMatchesAsync(final, manifest.Sha256, ct))
            {
                progress?.Report(1);
                return final; // already downloaded and verified earlier
            }

            File.Delete(final);
        }

        var part = final + ".part";
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (existing > manifest.Size)
        {
            File.Delete(part);
            existing = 0;
        }

        if (existing < manifest.Size)
        {
            await FetchAsync(manifest, part, existing, progress, ct);
        }

        var length = new FileInfo(part).Length;
        if (length != manifest.Size)
        {
            File.Delete(part);
            throw new UpdateException("The download was incomplete. Please try again.");
        }

        if (!await HashMatchesAsync(part, manifest.Sha256, ct))
        {
            File.Delete(part);
            throw new UpdateException("The downloaded file failed its security check and was deleted. Please try again later.");
        }

        File.Move(part, final, overwrite: true);
        progress?.Report(1);
        return final;
    }

    private async Task FetchAsync(UpdateManifest manifest, string part, long existing, IProgress<double>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, manifest.ApkUrl);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Couldn't reach the download server. Check your connection and try again.", ex);
        }

        using (response)
        {
            var append = false;
            if (response.StatusCode == HttpStatusCode.PartialContent && existing > 0 &&
                response.Content.Headers.ContentRange is { From: { } from } && from == existing)
            {
                append = true;
            }
            else if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                File.Delete(part); // the .part no longer matches the server file; start over next time
                throw new UpdateException("The download had to restart. Please try again.");
            }
            else if (!response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.PartialContent)
            {
                throw new UpdateException($"The download failed ({(int)response.StatusCode}). Please try again later.");
            }

            var written = append ? existing : 0;
            var oversize = false;
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                var lastReported = -1;
                int read;
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                try
                {
                    while (true)
                    {
                        stall.CancelAfter(_stallTimeout);
                        read = await source.ReadAsync(buffer, stall.Token);
                        if (read <= 0)
                        {
                            break;
                        }

                        written += read;
                        if (written > manifest.Size)
                        {
                            oversize = true; // not the published file: don't keep or resume it
                            break;
                        }

                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        var percent = (int)(written * 100 / manifest.Size);
                        if (percent != lastReported)
                        {
                            lastReported = percent;
                            progress?.Report(written / (double)manifest.Size);
                        }
                    }
                }
                catch (IOException ex) when (ex is not FileNotFoundException)
                {
                    // Connection dropped mid-file: keep the .part so the next attempt resumes from here.
                    throw new UpdateException("The download was interrupted. It will continue where it stopped when you try again.", ex);
                }
                catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    throw new UpdateException("The download stalled. It will continue where it stopped when you try again.", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new UpdateException("The download was interrupted. It will continue where it stopped when you try again.", ex);
                }
            }

            if (oversize)
            {
                File.Delete(part);
                throw new UpdateException("The server sent more data than expected. Please try again later.");
            }
        }
    }

    public static async Task<bool> HashMatchesAsync(string path, string expectedSha256Hex, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(actual.ToLowerInvariant()),
            System.Text.Encoding.ASCII.GetBytes(expectedSha256Hex.Trim().ToLowerInvariant()));
    }
}
