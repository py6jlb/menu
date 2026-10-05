using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Минимальный SMTP-сервер на loopback без внешней сети. Позволяет проверить
/// обязательный STARTTLS, отказ по сертификату, тайм-аут и успешный
/// защищённый сценарий, не выходя за пределы процесса.
/// </summary>
internal sealed class SmtpTestServer : IAsyncDisposable
{
    public enum Behaviour
    {
        AdvertiseStartTls,
        NoStartTls,
        Silent
    }

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly X509Certificate2 _certificate;
    private readonly Task _serve;

    private SmtpTestServer(Behaviour behaviour)
    {
        _certificate = CreateSelfSignedCertificate();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _serve = Task.Run(() => ServeAsync(behaviour));
    }

    public int Port { get; }

    public bool MessageReceived { get; private set; }

    public bool TlsUsed { get; private set; }

    public static SmtpTestServer Start(Behaviour behaviour) => new(behaviour);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try
        {
            await _serve;
        }
        catch
        {
        }
        _cts.Dispose();
        _certificate.Dispose();
    }

    private async Task ServeAsync(Behaviour behaviour)
    {
        try
        {
            using var tcp = await _listener.AcceptTcpClientAsync(_cts.Token);
            Stream stream = tcp.GetStream();

            if (behaviour == Behaviour.Silent)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), _cts.Token);
                return;
            }

            await WriteAsync(stream, "220 localhost ESMTP");
            var line = await ReadAsync(stream, _cts.Token);
            while (line is not null)
            {
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(stream, "250-localhost");
                    if (behaviour == Behaviour.AdvertiseStartTls)
                        await WriteAsync(stream, "250-STARTTLS");
                    await WriteAsync(stream, "250 SIZE 10485760");
                }
                else if (line.StartsWith("STARTTLS", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(stream, "220 Ready to start TLS");
                    var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                    await ssl.AuthenticateAsServerAsync(
                        _certificate, false, SslProtocols.None, checkCertificateRevocation: false);
                    stream = ssl;
                    TlsUsed = true;
                }
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(stream, "354 End data with <CR><LF>.<CR><LF>");
                    string? dataLine;
                    while ((dataLine = await ReadAsync(stream, _cts.Token)) is not null)
                        if (dataLine == ".") break;
                    MessageReceived = true;
                    await WriteAsync(stream, "250 OK queued");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(stream, "221 Bye");
                    break;
                }
                else
                {
                    await WriteAsync(stream, "250 OK");
                }

                line = await ReadAsync(stream, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (AuthenticationException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task WriteAsync(Stream stream, string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<string?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>(64);
        var single = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(single, cancellationToken);
            if (read == 0)
                return buffer.Count == 0 ? null : Encoding.ASCII.GetString(buffer.ToArray());
            if (single[0] == (byte)'\n')
            {
                if (buffer.Count > 0 && buffer[^1] == (byte)'\r') buffer.RemoveAt(buffer.Count - 1);
                return Encoding.ASCII.GetString(buffer.ToArray());
            }
            buffer.Add(single[0]);
        }
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
