using System.Collections.Concurrent;
using System.Linq.Expressions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;

namespace Tubester.IntegrationTests.TestHost;

public class CapturingBackgroundJobClient : IBackgroundJobClient
{
    private readonly ConcurrentDictionary<Type, List<CapturedJob>> _capturedJobs = new();
    private int _nextJobId = 1;

    private bool _failOnCreate;
    private Exception? _failureException;

    /// <summary>
    /// When enabled, the next call to Create will throw the configured exception.
    /// Automatically resets after throwing once.
    /// </summary>
    public void EnableFailure(Exception? exception)
    {
        _failOnCreate = true;
        _failureException = exception ?? new InvalidOperationException("Simulated job enqueue failure.");
    }

    public void DisableFailure()
    {
        _failOnCreate = false;
        _failureException = null;
    }

    public string Create(Job job, IState state)
    {
        if (_failOnCreate)
        {
            _failOnCreate = false;
            var exception = _failureException ?? new InvalidOperationException("Simulated job enqueue failure.");
            _failureException = null;
            throw exception;
        }

        var jobId = _nextJobId++.ToString();
        var capturedJob = new CapturedJob(jobId, job, state);

        _capturedJobs.AddOrUpdate(
            job.Type,
            [capturedJob],
            (_, existing) =>
            {
                existing.Add(capturedJob);
                return existing;
            });

        return jobId;
    }

    public bool ChangeState(string jobId, IState state, string expectedState)
    {
        return true;
    }

    public List<CapturedJob> GetEnqueued<TJob>()
    {
        return _capturedJobs.TryGetValue(typeof(TJob), out var jobs)
            ? jobs.Where(j => j.State is EnqueuedState).ToList()
            : [];
    }

    private List<CapturedJob> GetScheduled<TJob>()
    {
        return _capturedJobs.TryGetValue(typeof(TJob), out var jobs)
            ? jobs.Where(j => j.State is ScheduledState).ToList()
            : [];
    }

    public async Task RunAllAsync<TJob>(IServiceProvider serviceProvider)
        where TJob : notnull
    {
        var capturedJobs = GetEnqueued<TJob>().Concat(GetScheduled<TJob>());

        foreach (var capturedJob in capturedJobs)
        {
            var jobInstance = serviceProvider.GetRequiredService<TJob>();
            var method = capturedJob.Job.Method;
            var args = capturedJob.Job.Args;

            var argsArray = args.ToArray();
            var nullIndex = Array.FindIndex(argsArray, a => a is null);
            argsArray[nullIndex] = new JobCancellationToken(false);
            if (method.ReturnType == typeof(Task))
            {
                var task = (Task)method.Invoke(jobInstance, argsArray)!;
                await task;
            }
            else if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var task = method.Invoke(jobInstance, argsArray);
                await (dynamic)task!;
            }
            else
            {
                method.Invoke(jobInstance, argsArray);
            }
        }
    }

    public void Clear()
    {
        _capturedJobs.Clear();
        _nextJobId = 1;
    }
}

public record CapturedJob(string JobId, Job Job, IState State);

// Extension methods for easier job enqueueing in tests
public static class CapturingBackgroundJobClientExtensions
{
    public static string Enqueue<T>(this CapturingBackgroundJobClient client, Expression<Action<T>> methodCall)
    {
        var job = Job.FromExpression(methodCall);
        return client.Create(job, new EnqueuedState());
    }

    public static string Enqueue<T>(this CapturingBackgroundJobClient client, Expression<Func<T, Task>> methodCall)
    {
        var job = Job.FromExpression(methodCall);
        return client.Create(job, new EnqueuedState());
    }

    public static string Schedule<T>(this CapturingBackgroundJobClient client, Expression<Action<T>> methodCall,
        TimeSpan delay)
    {
        var job = Job.FromExpression(methodCall);
        return client.Create(job, new ScheduledState(delay));
    }

    public static string Schedule<T>(this CapturingBackgroundJobClient client, Expression<Func<T, Task>> methodCall,
        TimeSpan delay)
    {
        var job = Job.FromExpression(methodCall);
        return client.Create(job, new ScheduledState(delay));
    }
}