using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VidShrink.Core.Services;

public record ProcessExecutionResult
{
    public required int ExitCode { get; init; }
    public required string StandardError { get; init; }
    public required bool WasCancelled { get; init; }
}

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        Action<string>? onStandardOutputLine = null,
        Action<string>? onStandardErrorLine = null,
        CancellationToken cancellationToken = default);
}

public class DefaultProcessRunner : IProcessRunner
{
    public async Task<ProcessExecutionResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        Action<string>? onStandardOutputLine = null,
        Action<string>? onStandardErrorLine = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        var stderrBuffer = new StringBuilder();
        bool wasCancelled = false;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                onStandardOutputLine?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (stderrBuffer)
                {
                    if (stderrBuffer.Length < 65536)
                    {
                        stderrBuffer.AppendLine(e.Data);
                    }
                }
                onStandardErrorLine?.Invoke(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using (cancellationToken.Register(() =>
        {
            wasCancelled = true;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Process might already be terminating
            }
        }))
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { }

                try
                {
                    // Wait for process to fully exit and release all OS file locks
                    process.WaitForExit(3000);
                }
                catch { }

                return new ProcessExecutionResult
                {
                    ExitCode = -1,
                    StandardError = stderrBuffer.ToString(),
                    WasCancelled = true
                };
            }
        }

        // Ensure asynchronous output streams have flushed
        try
        {
            process.WaitForExit(3000);
        }
        catch { }

        return new ProcessExecutionResult
        {
            ExitCode = wasCancelled ? -1 : process.ExitCode,
            StandardError = stderrBuffer.ToString(),
            WasCancelled = wasCancelled
        };
    }
}
