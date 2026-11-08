using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;

namespace VidShrink.Core.Services;

public class EncoderException : Exception
{
    public int ExitCode { get; }
    public string StandardError { get; }

    public EncoderException(string message, int exitCode, string standardError)
        : base(message)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }
}

public interface IEncoderService
{
    event EventHandler<string>? MessageLogged;

    Task<EncodeResult> EncodeAsync(
        CompressionSettings settings,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);
}

public class EncoderService : IEncoderService
{
    private readonly IFfmpegLocator _locator;
    private readonly ICommandBuilder _commandBuilder;
    private readonly IProcessRunner _processRunner;
    private readonly IPlatformService _platformService;
    private readonly ILogService? _logService;

    public event EventHandler<string>? MessageLogged;

    public EncoderService(
        IFfmpegLocator? locator = null,
        ICommandBuilder? commandBuilder = null,
        IProcessRunner? processRunner = null,
        IPlatformService? platformService = null,
        ILogService? logService = null)
    {
        _locator = locator ?? new FfmpegLocator();
        _commandBuilder = commandBuilder ?? new CommandBuilder(platformService);
        _processRunner = processRunner ?? new DefaultProcessRunner();
        _platformService = platformService ?? DefaultPlatformService.Instance;
        _logService = logService;
    }

    public async Task<EncodeResult> EncodeAsync(
        CompressionSettings settings,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        var ffmpegExe = _locator.FindExecutable("ffmpeg");
        if (string.IsNullOrEmpty(ffmpegExe))
        {
            throw new FileNotFoundException("Исполняемый файл ffmpeg не найден в системе.");
        }

        if (!File.Exists(settings.InputFilePath))
        {
            throw new FileNotFoundException($"Исходный файл не найден: {settings.InputFilePath}");
        }

        var outputDir = Path.GetDirectoryName(settings.OutputFilePath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var stopwatch = Stopwatch.StartNew();

        return await ExecutePlanWithRetryAsync(
            ffmpegExe,
            settings,
            isRetry: false,
            stopwatch,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<EncodeResult> ExecutePlanWithRetryAsync(
        string ffmpegExe,
        CompressionSettings settings,
        bool isRetry,
        Stopwatch stopwatch,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var plan = _commandBuilder.BuildPlan(settings);
        var parser = new ProgressParser();
        parser.Reset(settings.Duration, currentPass: 1, totalPasses: plan.Commands.Count);

        try
        {
            for (int i = 0; i < plan.Commands.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cmd = plan.Commands[i];
                parser.CurrentPass = cmd.PassIndex;

                var processArguments = InjectProgressArguments(cmd.Arguments);

                var runResult = await _processRunner.RunAsync(
                    ffmpegExe,
                    processArguments,
                    onStandardOutputLine: line =>
                    {
                        var info = parser.FeedLine(line);
                        if (info != null)
                        {
                            progress?.Report(info);
                        }
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (runResult.WasCancelled || cancellationToken.IsCancellationRequested)
                {
                    CleanupIncompleteOutput(cmd.OutputPath, settings.OutputFilePath);
                    CleanupPassLogs(plan.PassLogPrefix);
                    throw new OperationCanceledException("Операция кодирования была отменена пользователем.", cancellationToken);
                }

                if (runResult.ExitCode != 0)
                {
                    CleanupIncompleteOutput(cmd.OutputPath, settings.OutputFilePath);
                    CleanupPassLogs(plan.PassLogPrefix);

                    _logService?.LogFfmpegError(runResult.StandardError, string.Join(" ", processArguments));

                    var errorSnippet = ExtractErrorSnippet(runResult.StandardError);
                    throw new EncoderException(
                        $"FFmpeg завершился с кодом ошибки {runResult.ExitCode}: {errorSnippet}",
                        runResult.ExitCode,
                        runResult.StandardError);
                }
            }

            // Successfully finished all passes for this plan
            CleanupPassLogs(plan.PassLogPrefix);

            if (!File.Exists(settings.OutputFilePath))
            {
                throw new FileNotFoundException($"Выходной файл не был создан: {settings.OutputFilePath}");
            }

            var outputFileInfo = new FileInfo(settings.OutputFilePath);
            long outputSizeBytes = outputFileInfo.Length;

            // Check if output size exceeds target size (auto-retry once with x0.92 bitrate)
            if (!isRetry &&
                settings.Mode == CompressionMode.TargetSize &&
                settings.TargetSizeMb.HasValue &&
                settings.TargetSizeMb.Value > 0)
            {
                long targetMaxBytes = (long)(settings.TargetSizeMb.Value * 1024 * 1024);
                if (outputSizeBytes > targetMaxBytes)
                {
                    var msg = $"Итоговый размер ({outputSizeBytes / (1024.0 * 1024.0):F2} МБ) превысил лимит ({settings.TargetSizeMb.Value:F2} МБ). Автоповтор с битрейтом x0.92.";
                    MessageLogged?.Invoke(this, msg);

                    // Delete current oversized file before retry
                    DeleteFileIfExists(settings.OutputFilePath);

                    int? retryBitrate = null;
                    if (settings.TargetVideoBitrateKbps.HasValue)
                    {
                        retryBitrate = (int)(settings.TargetVideoBitrateKbps.Value * 0.92);
                    }
                    else if (settings.Duration > TimeSpan.Zero)
                    {
                        int effectiveAudio = settings.AudioMode == AudioMode.Mute ? 0 : settings.AudioBitrateKbps;
                        var firstPassBitrate = new BitrateCalculator().Calculate(settings.TargetSizeMb.Value, settings.Duration, effectiveAudio).VideoBitrateKbps;
                        retryBitrate = (int)(firstPassBitrate * 0.92);
                    }

                    var retrySettings = settings with
                    {
                        TargetSizeMb = settings.TargetSizeMb.Value * 0.92,
                        TargetVideoBitrateKbps = retryBitrate
                    };

                    var retryResult = await ExecutePlanWithRetryAsync(
                        ffmpegExe,
                        retrySettings,
                        isRetry: true,
                        stopwatch,
                        progress,
                        cancellationToken).ConfigureAwait(false);

                    return retryResult with
                    {
                        RetriedWithLowerBitrate = true
                    };
                }
            }

            return new EncodeResult
            {
                Success = true,
                OutputFilePath = settings.OutputFilePath,
                OutputSizeBytes = outputSizeBytes,
                ElapsedTime = stopwatch.Elapsed,
                RetriedWithLowerBitrate = isRetry
            };
        }
        catch (OperationCanceledException)
        {
            CleanupIncompleteOutput(settings.OutputFilePath);
            CleanupPassLogs(plan.PassLogPrefix);
            throw;
        }
        catch (Exception)
        {
            CleanupIncompleteOutput(settings.OutputFilePath);
            CleanupPassLogs(plan.PassLogPrefix);
            throw;
        }
    }

    private static IReadOnlyList<string> InjectProgressArguments(IReadOnlyList<string> originalArgs)
    {
        // Avoid duplicate -progress arguments if already present
        for (int i = 0; i < originalArgs.Count; i++)
        {
            if (originalArgs[i] == "-progress")
                return originalArgs;
        }

        var result = new List<string>(originalArgs.Count + 3);
        bool inserted = false;

        for (int i = 0; i < originalArgs.Count; i++)
        {
            result.Add(originalArgs[i]);

            // Insert right after -hide_banner or after -y
            if (!inserted && (originalArgs[i] == "-hide_banner" || (i == 0 && originalArgs[i] == "-y")))
            {
                result.Add("-progress");
                result.Add("pipe:1");
                result.Add("-nostats");
                inserted = true;
            }
        }

        if (!inserted)
        {
            result.Insert(0, "-nostats");
            result.Insert(0, "pipe:1");
            result.Insert(0, "-progress");
        }

        return result;
    }

    private static void CleanupIncompleteOutput(params string?[] paths)
    {
        foreach (var p in paths)
        {
            if (string.IsNullOrEmpty(p))
                continue;

            // Do not delete null devices
            if (string.Equals(p, "NUL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "/dev/null", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DeleteFileIfExists(p);
        }
    }

    private static void CleanupPassLogs(string? passLogPrefix)
    {
        if (string.IsNullOrWhiteSpace(passLogPrefix))
            return;

        try
        {
            var directory = Path.GetDirectoryName(passLogPrefix);
            var filePrefix = Path.GetFileName(passLogPrefix);

            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory) && !string.IsNullOrEmpty(filePrefix))
            {
                var files = Directory.GetFiles(directory, $"{filePrefix}*");
                foreach (var file in files)
                {
                    DeleteFileIfExists(file);
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static void DeleteFileIfExists(string filePath, int retries = 3, int delayMs = 100)
    {
        for (int i = 0; i <= retries; i++)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                return;
            }
            catch (IOException) when (i < retries)
            {
                Thread.Sleep(delayMs);
            }
            catch
            {
                // Ignore final deletion errors on cleanup
                return;
            }
        }
    }

    private static string ExtractErrorSnippet(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return "Неизвестная ошибка FFmpeg.";

        var lines = stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var lastLines = lines.Length <= 5 ? lines : lines[^5..];
        return string.Join(" ", lastLines);
    }
}
