using Nikse.SubtitleEdit.Logic.Config;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Nikse.SubtitleEdit.Logic;

/// <summary>
/// Optional single-instance mode over a named pipe (a Unix domain socket on Linux/macOS,
/// so one code path covers all platforms). When enabled, a second launch forwards its
/// file arguments to the already-running instance and exits; the running instance picks
/// them up, comes to the front and opens them. "File > New window" is unaffected - extra
/// editor windows live in the same process and never go through the pipe.
/// </summary>
public static class SingleInstanceService
{
    // Per-user name: on Unix the pipe is a socket file in a shared temp folder, so two
    // users on the same machine must not collide. PipeOptions.CurrentUserOnly additionally
    // rejects clients/servers running as a different user on both platforms.
    private static string PipeName => "SubtitleEdit-single-instance-" + Uri.EscapeDataString(Environment.UserName);

    // Message format: "<subtitle path>\n<video path>", either part may be empty.
    // The client writes the message and closes; already-written bytes survive the close
    // on both Windows pipes and Unix domain sockets, so no drain/ack round-trip is needed.
    private const char Separator = '\n';

    /// <summary>
    /// Tries to hand this launch over to an already-running instance. Returns true when a
    /// running instance accepted the request (the caller should exit without showing UI);
    /// false when no instance is listening (the caller should start normally).
    /// </summary>
    public static bool TryForwardToRunningInstance(string? subtitleFileName, string? videoFileName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(300); // ms - fails fast when no instance is listening
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.Write((subtitleFileName ?? string.Empty) + Separator + (videoFileName ?? string.Empty));
            writer.Flush();
            return true;
        }
        catch
        {
            // No server (normal first launch), a different-user instance, or a race with a
            // closing instance - in every case starting up normally is the right fallback.
            return false;
        }
    }

    /// <summary>
    /// Starts the pipe server on a background thread. Each accepted connection delivers one
    /// open request; <paramref name="onOpenRequest"/> is invoked on the pipe thread with the
    /// forwarded subtitle/video paths (null when not supplied), so implementations must
    /// dispatch to the UI thread themselves.
    /// </summary>
    public static void StartServer(Action<string?, string?> onOpenRequest)
    {
        var thread = new Thread(() => RunServerLoop(onOpenRequest))
        {
            IsBackground = true,
            Name = "SE single-instance pipe server",
        };
        thread.Start();
    }

    private static void RunServerLoop(Action<string?, string?> onOpenRequest)
    {
        while (true)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
            }
            catch (Exception exception)
            {
                // Pipe name already taken (another instance won the race) or the platform
                // refused the pipe - single-instance forwarding is best effort, so give up
                // quietly instead of spinning on a name that will keep failing.
                Se.LogError(exception, "Single-instance pipe server could not be created");
                return;
            }

            try
            {
                using (server)
                {
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var message = reader.ReadToEnd();
                    var parts = message.Split(Separator);
                    var subtitleFileName = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : null;
                    var videoFileName = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
                    onOpenRequest(subtitleFileName, videoFileName);
                }
            }
            catch (IOException)
            {
                // Client vanished mid-handshake; keep serving the next launch.
            }
            catch (Exception exception)
            {
                Se.LogError(exception, "Single-instance pipe server failed");
                return;
            }
        }
    }
}
