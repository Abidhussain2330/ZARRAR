using System.Threading.Channels;
using BURAQ.Repositories.Interface;
using BURAQ.Services.Cache;
using BURAQ.Services.Soc;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Services;

public class FlagChecker(
    ChannelReader<Submission> channelReader,
    ChannelWriter<Submission> channelWriter,
    ILogger<FlagChecker> logger,
    IConfiguration configuration,
    IServiceScopeFactory serviceScopeFactory) : IHostedService
{
    private const int MaxWorkerCount = 4;

    /// <summary>
    /// Key used to fingerprint submitted answers before they reach any log sink.
    /// </summary>
    /// <remarks>
    /// Read from the same platform secret the rest of the application uses so that fingerprints
    /// remain stable across restarts and instances.
    /// </remarks>
    private readonly byte[] _fingerprintKey = configuration["XorKey"]?.ToUTF8Bytes() ?? [];

    private CancellationTokenSource TokenSource { get; set; } = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        TokenSource = new CancellationTokenSource();

        for (var i = 0; i < GetWorkerCount(); ++i)
        {
            await Task.Factory.StartNew(() => Checker(i, TokenSource.Token), cancellationToken,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();

        var submissionRepository = scope.ServiceProvider.GetRequiredService<ISubmissionRepository>();
        var flags = await submissionRepository.GetUncheckedFlags(TokenSource.Token);

        foreach (var item in flags)
            await channelWriter.WriteAsync(item, TokenSource.Token);

        if (flags.Length > 0)
            logger.SystemLog(StaticLocalizer[nameof(Resources.Program.FlagsChecker_Recheck), flags.Length],
                TaskStatus.Pending,
                LogLevel.Debug);

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.FlagsChecker_Started)], TaskStatus.Success,
            LogLevel.Debug);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        TokenSource.Cancel();

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.FlagsChecker_Stopped)], TaskStatus.Exit,
            LogLevel.Debug);

        return Task.CompletedTask;
    }

    internal static int GetWorkerCount()
    {
        // if RAM < 2GiB or CPU <= 3, return 1
        // if RAM < 4GiB or CPU <= 6, return 2
        // otherwise, return 4
        var memoryInfo = GC.GetGCMemoryInfo();
        var freeMemory = memoryInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0;
        var cpuCount = Environment.ProcessorCount;

        if (freeMemory < 2 || cpuCount <= 3)
            return 1;
        if (freeMemory < 4 || cpuCount <= 6)
            return 2;
        return MaxWorkerCount;
    }

    private async Task Checker(int id, CancellationToken token = default)
    {
        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.FlagsChecker_WorkerStarted), id],
            TaskStatus.Pending,
            LogLevel.Debug);

        try
        {
            await foreach (var item in channelReader.ReadAllAsync(token))
            {
                // Never log the raw answer. Log sinks include a database table, the SignalR
                // administrator stream, Loki and OpenTelemetry, so a plaintext flag here reaches
                // far beyond the application host (finding F-004).
                var answerFingerprint = FlagFingerprint.Compute(item.Answer, _fingerprintKey);

                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.FlagsChecker_WorkerStartProcessing), id,
                        answerFingerprint],
                    TaskStatus.Pending, LogLevel.Debug);

                await using var scope = serviceScopeFactory.CreateAsyncScope();

                var cacheHelper = scope.ServiceProvider.GetRequiredService<CacheHelper>();
                var eventRepository =
                    scope.ServiceProvider.GetRequiredService<IGameEventRepository>();
                var instanceRepository =
                    scope.ServiceProvider.GetRequiredService<IGameInstanceRepository>();
                var gameNoticeRepository =
                    scope.ServiceProvider.GetRequiredService<IGameNoticeRepository>();
                var submissionRepository =
                    scope.ServiceProvider.GetRequiredService<ISubmissionRepository>();
                var detectionQueue = scope.ServiceProvider.GetRequiredService<SocDetectionQueue>();

                try
                {
                    var (type, ans) = await instanceRepository.VerifyAnswer(item, token);

                    switch (ans)
                    {
                        case AnswerResult.NotFound:
                            logger.Log(
                                StaticLocalizer[nameof(Resources.Program.FlagChecker_UnknownInstance),
                                    item.TeamName,
                                    item.ChallengeName],
                                item.User,
                                TaskStatus.NotFound, LogLevel.Warning);
                            break;
                        case AnswerResult.Accepted:
                            {
                                logger.Log(
                                    StaticLocalizer[nameof(Resources.Program.FlagChecker_AnswerAccepted),
                                        item.TeamName,
                                        item.ChallengeName,
                                        answerFingerprint],
                                    item.User, TaskStatus.Success, LogLevel.Information);

                                await eventRepository.AddEvent(
                                    GameEvent.FromSubmission(item, type, ans, StaticLocalizer), token);

                                // always flush the scoreboard
                                await cacheHelper.FlushScoreboardCache(item.GameId, token);
                                break;
                            }
                        default:
                            {
                                logger.Log(
                                    StaticLocalizer[nameof(Resources.Program.FlagChecker_AnswerRejected),
                                        item.TeamName,
                                        item.ChallengeName,
                                        answerFingerprint],
                                    item.User, TaskStatus.Failed, LogLevel.Information);

                                await eventRepository.AddEvent(
                                    GameEvent.FromSubmission(item, type, ans, StaticLocalizer), token);

                                var result = await instanceRepository.CheckCheat(item, token);
                                ans = result.AnswerResult;

                                if (ans == AnswerResult.CheatDetected)
                                {
                                    logger.Log(
                                        StaticLocalizer[nameof(Resources.Program.FlagChecker_CheatDetected),
                                            item.TeamName,
                                            item.ChallengeName,
                                            result.SourceTeamName ?? ""],
                                        item.User, TaskStatus.Success, LogLevel.Information);

                                    await eventRepository.AddEvent(
                                        new()
                                        {
                                            Type = EventType.CheatDetected,
                                            Values =
                                                [item.ChallengeName, item.TeamName, result.SourceTeamName ?? ""],
                                            TeamId = item.TeamId,
                                            UserId = item.UserId,
                                            GameId = item.GameId
                                        }, token);
                                }

                                break;
                            }
                    }

                    if (item.Game!.EndTimeUtc > DateTimeOffset.UtcNow
                        && type != SubmissionType.Unaccepted
                        && type != SubmissionType.Normal)
                        await gameNoticeRepository.AddNotice(
                            GameNotice.FromSubmission(item, type, StaticLocalizer), token);

                    item.Status = ans;
                    await submissionRepository.SendSubmission(item);

                    // ZARRAR showdown: stage animation, capture and container teardown.
                    await scope.ServiceProvider.GetRequiredService<Zarrar.ZarrarDuelService>()
                        .OnSubmissionCheckedAsync(item, ans, scope.ServiceProvider, token);
                    await detectionQueue.EnqueueAsync(item.Id, token);
                }
                catch (DbUpdateConcurrencyException)
                {
                    logger.SystemLog(
                        StaticLocalizer[nameof(Resources.Program.FlagChecker_ConcurrencyFailed), item.Id],
                        TaskStatus.Failed,
                        LogLevel.Warning);
                    await channelWriter.WriteAsync(item, token);
                }
                catch (Exception e)
                {
                    logger.SystemLog(
                        StaticLocalizer[nameof(Resources.Program.FlagsChecker_WorkerExceptionOccurred), id],
                        TaskStatus.Failed,
                        LogLevel.Debug);
                    logger.LogErrorMessage(e);
                }

                token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            logger.SystemLog(StaticLocalizer[nameof(Resources.Program.FlagsChecker_WorkerCancelled), id],
                TaskStatus.Exit,
                LogLevel.Debug);
        }
        finally
        {
            logger.SystemLog(StaticLocalizer[nameof(Resources.Program.FlagsChecker_WorkerStopped), id],
                TaskStatus.Exit,
                LogLevel.Debug);
        }
    }
}
