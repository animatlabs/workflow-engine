﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleWorkflowEngine.Core;

namespace SimpleWorkflowEngine.Steps
{
    /// <summary>
    /// Retries a workflow step a specified number of times using a retry strategy.
    /// </summary>
    public sealed class RetryStep : WorkflowStep
    {
        private readonly IWorkflowStep step;
        private readonly IRetryStrategy retryStrategy;
        private readonly int maxRetries;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryStep"/> class.
        /// </summary>
        /// <param name="step">The workflow step to be retried. Cannot be null.</param>
        /// <param name="maxRetries">
        /// The maximum number of retries. Must be greater than or equal to 0.
        /// </param>
        /// <param name="retryStrategy">The strategy to determine if a retry should be attempted. Cannot be null.</param>
        /// <param name="logger">The logger instance to use for logging (optional).</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="step"/> or <paramref name="retryStrategy"/> is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="maxRetries"/> is less than 0.
        /// </exception>
        public RetryStep(IWorkflowStep step, int maxRetries, IRetryStrategy retryStrategy, ILogger logger)
            : base(logger)
        {
            this.step = step ?? throw new ArgumentNullException(nameof(step));
            this.maxRetries = maxRetries >= 0 ? maxRetries : throw new ArgumentOutOfRangeException(nameof(maxRetries));
            this.retryStrategy = retryStrategy ?? throw new ArgumentNullException(nameof(retryStrategy));
        }

        /// <inheritdoc />
        protected override async Task ExecuteCoreAsync(IWorkflowContext context, CancellationToken cancellationToken)
        {
            for (int retryCount = 0; retryCount < maxRetries; retryCount++)
            {
                using (_ = logger.BeginScope(new Dictionary<string, object> {
                    { "ParentStepId", StepId },
                    { "ParentStepName", Name },
                    { "StepId", step.StepId },
                    { "StepName", step.Name },
                    { "RetryStrategy", retryStrategy.GetType().Name },
                    { "RetryCount", retryCount },
                }))
                {
                    try
                    {
                        await step.ExecuteAsync(context, cancellationToken);
                        return;
                    }
                    catch (Exception ex) when (retryCount < maxRetries - 1)
                    {
                        logger.LogWarning($"Retry {retryCount} failed for step {step.StepId}: {ex.Message}");
                        if (!await retryStrategy.ShouldRetryAsync(retryCount, cancellationToken))
                        {
                            break;
                        }
                    }
                }
            }
            throw new InvalidOperationException($"Step failed after {maxRetries} retries.");
        }

        /// <inheritdoc />
        protected override async Task CompensateCoreAsync(IWorkflowContext context, CancellationToken cancellationToken)
        {
            using (_ = logger.BeginScope(new Dictionary<string, object> {
                    { "ParentStepId", StepId },
                    { "ParentStepName", Name },
                    { "StepId", step.StepId },
                    { "StepName", step.Name },
                }))
            {
                await step.CompensateAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            step.Dispose();
        }
    }
}