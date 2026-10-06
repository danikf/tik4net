using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.mcp;

/// <summary>
/// <c>tik4net.mcp --relay</c>: the in-repository launcher's long-lived half (run-dev.ps1). The MCP client talks to
/// this process for the whole connection; it runs the real server as a child staged from the build output, and
/// when that output is rebuilt it stages the new build, starts it, replays the client's handshake to it and
/// retires the old child — so the next call is answered by the new code without the operator reconnecting.
/// </summary>
/// <remarks>
/// <para>MCP over stdio is newline-delimited JSON-RPC, so the relay works line by line and reads only the envelope
/// (<c>id</c>, <c>method</c>). A swap happens only when a request arrives and nothing is in flight, so no answer is
/// lost or routed to the wrong child; the child's reply to the replayed <c>initialize</c> is swallowed, and the
/// client is sent <c>notifications/tools/list_changed</c> so it can re-read tool descriptions that changed.</para>
/// <para>A build is taken only once its files are at least <see cref="SettleTime"/> old: a copy made while
/// msbuild is still writing would pair a new tik4net.dll with an old tik4net.mcp.dll.</para>
/// <para>This code runs from the copy staged at connect time, so a change to the relay ITSELF still needs a
/// reconnect. It is kept small for that reason.</para>
/// </remarks>
internal static class DevRelay
{
    private static readonly string[] BuildFiles = { "tik4net.dll", "tik4net.mcp.dll" };
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);
    private const string ReplayIdPrefix = "t4n-relay-init-";

    private static readonly object StdoutLock = new object();
    private static readonly Stream ClientOut = Console.OpenStandardOutput();

    private static string _sourceDir = "";
    private static string _stageRoot = "";
    private static Child? _child;
    private static string? _initializeLine;
    private static string? _initializedLine;
    private static int _replays;
    private static int _stagings;   // the fallback's directory per child: a copy must never land on a running child's files

    // Children run from a few fixed slot directories, not a new path per build: Windows Firewall keys its rules on
    // the program's path, so every new path is a new prompt, and a dismissed prompt is a Block rule — which drops
    // MNDP's inbound broadcast without a word (mikrotik_discover finds nothing, MacTelnet by IP "cannot determine
    // MAC"). A slot is taken by holding its lock file open for the child's lifetime; the OS lets go if the relay dies.
    private const int SlotCount = 8;
    private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);

    private sealed class Child
    {
        public Process Process = null!;
        public string StageDir = "";
        public FileStream? SlotLock;
        public DateTime BuiltAt;
        public TaskCompletionSource<bool>? ReplayAnswered;
        public string? ReplayId;
        public volatile bool Retired;
    }

    public static async Task<int> RunAsync()
    {
        _sourceDir = Environment.GetEnvironmentVariable("TIK4NET_MCP_SOURCE_DIR") ?? "";
        if (_sourceDir.Length == 0 || !Directory.Exists(_sourceDir))
        {
            Note("--relay needs TIK4NET_MCP_SOURCE_DIR (set by run-dev.ps1) naming the build output");
            return 1;
        }
        _stageRoot = Path.Combine(Path.GetTempPath(), "tik4net.mcp-dev");
        Environment.SetEnvironmentVariable("TIK4NET_MCP_RELAYED", "1");

        _child = Start(BuildTime());
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        string? line;
        while ((line = await stdin.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            if (line.Length == 0) continue;
            ReadEnvelope(line, out string? id, out string? method);

            if (method == "initialize") _initializeLine = line;
            else if (method == "notifications/initialized") _initializedLine = line;
            else if (method != null) await SwapIfRebuiltAsync().ConfigureAwait(false);

            if (id != null && method != null)
                lock (Pending) Pending.Add(id);
            await SendAsync(_child!, line).ConfigureAwait(false);
        }

        Retire(_child!);
        return 0;
    }

    // ── the swap ─────────────────────────────────────────────────────────────

    private static async Task SwapIfRebuiltAsync()
    {
        var current = _child!;
        bool dead = current.Process.HasExited;
        DateTime built = BuildTime();
        if (!dead && built <= current.BuiltAt.AddSeconds(1)) return;       // not rebuilt
        if (DateTime.Now - built < SettleTime) return;                     // still being written
        if (_initializeLine == null) return;                               // no handshake to replay yet
        lock (Pending) if (Pending.Count > 0 && !dead) return;             // an answer is still owed

        Child next;
        try
        {
            next = Start(built);
            await ReplayHandshakeAsync(next).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Note("could not start the rebuilt server, staying on the previous one: " + ex.Message);
            return;
        }

        _child = next;
        Retire(current);
        lock (Pending) Pending.Clear();
        Note($"now serving the build of {built:yyyy-MM-dd HH:mm:ss} from {next.StageDir}");
        WriteClient("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}");
    }

    private static async Task ReplayHandshakeAsync(Child child)
    {
        string replayId = ReplayIdPrefix + Interlocked.Increment(ref _replays);
        child.ReplayId = JsonSerializer.Serialize(replayId);
        child.ReplayAnswered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var doc = JsonDocument.Parse(_initializeLine!))
        {
            var root = doc.RootElement;
            var sb = new StringBuilder("{\"jsonrpc\":\"2.0\",\"id\":").Append(child.ReplayId)
                .Append(",\"method\":\"initialize\"");
            if (root.TryGetProperty("params", out var p)) sb.Append(",\"params\":").Append(p.GetRawText());
            await SendAsync(child, sb.Append('}').ToString()).ConfigureAwait(false);
        }

        var done = await Task.WhenAny(child.ReplayAnswered.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
        if (done != child.ReplayAnswered.Task)
        {
            Retire(child);
            throw new TimeoutException("the rebuilt server did not answer initialize within 30 s");
        }
        if (_initializedLine != null) await SendAsync(child, _initializedLine).ConfigureAwait(false);
    }

    // ── children ─────────────────────────────────────────────────────────────

    private static Child Start(DateTime built)
    {
        string? stage = TakeSlot(out FileStream? slotLock);
        if (stage == null)
        {
            stage = Path.Combine(_stageRoot,
                $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}-child{Interlocked.Increment(ref _stagings)}");
            Note($"every slot is in use; staging to {stage}, a path the firewall has not seen");
        }
        CopyDirectory(_sourceDir, stage);

        string exe = Path.Combine(stage, "tik4net.mcp.exe");
        var psi = File.Exists(exe)
            ? new ProcessStartInfo(exe)
            : new ProcessStartInfo("dotnet", "\"" + Path.Combine(stage, "tik4net.mcp.dll") + "\"");
        psi.UseShellExecute = false;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;      // stderr is inherited: the child's logs reach the client's log
        psi.StandardOutputEncoding = new UTF8Encoding(false);
        psi.StandardInputEncoding = new UTF8Encoding(false);

        var child = new Child { StageDir = stage, SlotLock = slotLock, BuiltAt = built };
        child.Process = Process.Start(psi) ?? throw new InvalidOperationException("the server process did not start");
        _ = Task.Run(() => PumpAsync(child));
        return child;
    }

    private static async Task PumpAsync(Child child)
    {
        var reader = child.Process.StandardOutput;
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            ReadEnvelope(line, out string? id, out string? method);
            if (id != null && child.ReplayId != null && id == child.ReplayId)
            {
                child.ReplayAnswered?.TrySetResult(true);    // the replayed initialize: the client already has one
                continue;
            }
            if (child.Retired) continue;   // nothing a retired child says is owed to the client
            if (id != null && method == null)
                lock (Pending) Pending.Remove(id);
            WriteClient(line);
        }
    }

    private static void Retire(Child child)
    {
        child.Retired = true;
        try { child.Process.StandardInput.Close(); } catch { }
        try { if (!child.Process.WaitForExit(2000)) child.Process.Kill(entireProcessTree: true); } catch { }
        if (child.SlotLock != null)
            child.SlotLock.Dispose();   // the slot's directory stays: its path is what the firewall rule names
        else
            try { Directory.Delete(child.StageDir, recursive: true); } catch { }   // best effort; pruned later anyway
    }

    // The first free slot, emptied for the copy, with its lock held; null when all are taken. A slot whose files
    // cannot be deleted still has a process running from it (a child outliving a relay that died) and is skipped.
    private static string? TakeSlot(out FileStream? slotLock)
    {
        Directory.CreateDirectory(_stageRoot);
        for (int i = 0; i < SlotCount; i++)
        {
            FileStream lockFile;
            try
            {
                lockFile = new FileStream(Path.Combine(_stageRoot, $"slot-{i}.lock"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { continue; }

            string dir = Path.Combine(_stageRoot, $"slot-{i}");
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                lockFile.Dispose();
                continue;
            }
            slotLock = lockFile;
            return dir;
        }
        slotLock = null;
        return null;
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static DateTime BuildTime()
        => BuildFiles.Select(f => Path.Combine(_sourceDir, f))
                     .Where(File.Exists)
                     .Select(File.GetLastWriteTime)
                     .DefaultIfEmpty(DateTime.MinValue)
                     .Max();

    private static void ReadEnvelope(string line, out string? id, out string? method)
    {
        id = null; method = null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            if (doc.RootElement.TryGetProperty("id", out var i)) id = i.GetRawText();
            if (doc.RootElement.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String) method = m.GetString();
        }
        catch (JsonException) { }   // not ours to judge: passed through unchanged
    }

    private static async Task SendAsync(Child child, string line)
    {
        await child.Process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
        await child.Process.StandardInput.FlushAsync().ConfigureAwait(false);
    }

    private static void WriteClient(string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        lock (StdoutLock)
        {
            ClientOut.Write(bytes, 0, bytes.Length);
            ClientOut.Flush();
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to), overwrite: true);
    }

    private static void Note(string text) => Console.Error.WriteLine("[tik4net.mcp/relay] " + text);
}
