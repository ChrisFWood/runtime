// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Xunit;

namespace System.Threading.RateLimiting.Test
{
    public class TokenBucketRateLimiterTests : BaseRateLimiterTests
    {
        [Fact]
        public override void CanAcquireResource()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            var lease = limiter.Acquire();

            Assert.True(lease.IsAcquired);
            Assert.False(limiter.Acquire().IsAcquired);

            lease.Dispose();
            Assert.False(limiter.Acquire().IsAcquired);
            Assert.True(limiter.TryReplenish());

            Assert.True(limiter.Acquire().IsAcquired);
        }

        [Fact]
        public override void InvalidOptionsThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiterOptions(-1, QueueProcessingOrder.NewestFirst, 1, TimeSpan.FromMinutes(2), 1, autoReplenishment: false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, -1, TimeSpan.FromMinutes(2), 1, autoReplenishment: false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1, TimeSpan.FromMinutes(2), -1, autoReplenishment: false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1, TimeSpan.FromDays(49).Add(TimeSpan.FromMilliseconds(1)), 1, autoReplenishment: false));
        }

        [Fact]
        public override async Task CanAcquireResourceAsync()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));

            using var lease = await limiter.WaitAsync();

            Assert.True(lease.IsAcquired);
            var wait = limiter.WaitAsync();
            Assert.False(wait.IsCompleted);

            Assert.True(limiter.TryReplenish());

            Assert.True((await wait).IsAcquired);
        }

        [Fact]
        public override async Task CanAcquireResourceAsync_QueuesAndGrabsOldest()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.OldestFirst, 2,
                TimeSpan.Zero, 1, autoReplenishment: false));
            var lease = await limiter.WaitAsync();

            Assert.True(lease.IsAcquired);
            var wait1 = limiter.WaitAsync();
            var wait2 = limiter.WaitAsync();
            Assert.False(wait1.IsCompleted);
            Assert.False(wait2.IsCompleted);

            lease.Dispose();
            Assert.True(limiter.TryReplenish());

            lease = await wait1;
            Assert.True(lease.IsAcquired);
            Assert.False(wait2.IsCompleted);

            lease.Dispose();
            Assert.Equal(0, limiter.GetAvailablePermits());
            Assert.True(limiter.TryReplenish());

            lease = await wait2;
            Assert.True(lease.IsAcquired);
        }

        [Fact]
        public override async Task CanAcquireResourceAsync_QueuesAndGrabsNewest()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.NewestFirst, 3,
                TimeSpan.FromMinutes(0), 1, autoReplenishment: false));

            var lease = await limiter.WaitAsync(2);
            Assert.True(lease.IsAcquired);

            var wait1 = limiter.WaitAsync(2);
            var wait2 = limiter.WaitAsync();
            Assert.False(wait1.IsCompleted);
            Assert.False(wait2.IsCompleted);

            lease.Dispose();
            Assert.True(limiter.TryReplenish());

            // second queued item completes first with NewestFirst
            lease = await wait2;
            Assert.True(lease.IsAcquired);
            Assert.False(wait1.IsCompleted);

            lease.Dispose();
            Assert.Equal(0, limiter.GetAvailablePermits());
            Assert.True(limiter.TryReplenish());
            Assert.True(limiter.TryReplenish());

            lease = await wait1;
            Assert.True(lease.IsAcquired);
        }

        [Fact]
        public override async Task FailsWhenQueuingMoreThanLimit()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            using var lease = limiter.Acquire(1);
            var wait = limiter.WaitAsync(1);

            var failedLease = await limiter.WaitAsync(1);
            Assert.False(failedLease.IsAcquired);
            Assert.True(failedLease.TryGetMetadata(MetadataName.RetryAfter, out var timeSpan));
            Assert.Equal(TimeSpan.Zero, timeSpan);
        }

        [Fact]
        public override async Task QueueAvailableAfterQueueLimitHitAndResources_BecomeAvailable()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            var lease = limiter.Acquire(1);
            var wait = limiter.WaitAsync(1);

            var failedLease = await limiter.WaitAsync(1);
            Assert.False(failedLease.IsAcquired);

            limiter.TryReplenish();
            lease = await wait;
            Assert.True(lease.IsAcquired);

            wait = limiter.WaitAsync(1);
            Assert.False(wait.IsCompleted);

            limiter.TryReplenish();
            lease = await wait;
            Assert.True(lease.IsAcquired);
        }

        [Fact]
        public override void ThrowsWhenAcquiringMoreThanLimit()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            Assert.Throws<ArgumentOutOfRangeException>(() => limiter.Acquire(2));
        }

        [Fact]
        public override async Task ThrowsWhenWaitingForMoreThanLimit()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await limiter.WaitAsync(2));
        }

        [Fact]
        public override void ThrowsWhenAcquiringLessThanZero()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            Assert.Throws<ArgumentOutOfRangeException>(() => limiter.Acquire(-1));
        }

        [Fact]
        public override async Task ThrowsWhenWaitingForLessThanZero()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await limiter.WaitAsync(-1));
        }

        [Fact]
        public override void AcquireZero_WithAvailability()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));

            using var lease = limiter.Acquire(0);
            Assert.True(lease.IsAcquired);
        }

        [Fact]
        public override void AcquireZero_WithoutAvailability()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            using var lease = limiter.Acquire(1);
            Assert.True(lease.IsAcquired);

            var lease2 = limiter.Acquire(0);
            Assert.False(lease2.IsAcquired);
            lease2.Dispose();
        }

        [Fact]
        public override async Task WaitAsyncZero_WithAvailability()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));

            using var lease = await limiter.WaitAsync(0);
            Assert.True(lease.IsAcquired);
        }

        [Fact]
        public override async Task WaitAsyncZero_WithoutAvailabilityWaitsForAvailability()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.NewestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            var lease = await limiter.WaitAsync(1);
            Assert.True(lease.IsAcquired);

            var wait = limiter.WaitAsync(0);
            Assert.False(wait.IsCompleted);

            lease.Dispose();
            Assert.True(limiter.TryReplenish());
            using var lease2 = await wait;
            Assert.True(lease2.IsAcquired);
        }

        [Fact]
        public override async Task CanDequeueMultipleResourcesAtOnce()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.OldestFirst, 2,
                TimeSpan.Zero, 2, autoReplenishment: false));
            using var lease = await limiter.WaitAsync(2);
            Assert.True(lease.IsAcquired);

            var wait1 = limiter.WaitAsync(1);
            var wait2 = limiter.WaitAsync(1);
            Assert.False(wait1.IsCompleted);
            Assert.False(wait2.IsCompleted);

            lease.Dispose();
            Assert.True(limiter.TryReplenish());

            var lease1 = await wait1;
            var lease2 = await wait2;
            Assert.True(lease1.IsAcquired);
            Assert.True(lease2.IsAcquired);
        }

        [Fact]
        public override async Task CanCancelWaitAsyncAfterQueuing()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            var lease = limiter.Acquire(1);
            Assert.True(lease.IsAcquired);

            var cts = new CancellationTokenSource();
            var wait = limiter.WaitAsync(1, cts.Token);

            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => wait.AsTask());

            lease.Dispose();
            Assert.True(limiter.TryReplenish());

            Assert.Equal(1, limiter.GetAvailablePermits());
        }

        [Fact]
        public override async Task CanCancelWaitAsyncBeforeQueuing()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            var lease = limiter.Acquire(1);
            Assert.True(lease.IsAcquired);

            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => limiter.WaitAsync(1, cts.Token).AsTask());

            lease.Dispose();
            Assert.True(limiter.TryReplenish());

            Assert.Equal(1, limiter.GetAvailablePermits());
        }

        [Fact]
        public override void NoMetadataOnAcquiredLease()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            using var lease = limiter.Acquire(1);
            Assert.False(lease.TryGetMetadata(MetadataName.RetryAfter, out _));
        }

        [Fact]
        public override void MetadataNamesContainsAllMetadata()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            using var lease = limiter.Acquire(1);
            Assert.Collection(lease.MetadataNames, metadataName => Assert.Equal(metadataName, MetadataName.RetryAfter.Name));
        }

        [Fact]
        public override async Task DisposeReleasesQueuedAcquires()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.OldestFirst, 3,
                TimeSpan.Zero, 1, autoReplenishment: false));
            var lease = limiter.Acquire(1);
            var wait1 = limiter.WaitAsync(1);
            var wait2 = limiter.WaitAsync(1);
            var wait3 = limiter.WaitAsync(1);
            Assert.False(wait1.IsCompleted);
            Assert.False(wait2.IsCompleted);
            Assert.False(wait3.IsCompleted);

            limiter.Dispose();

            lease = await wait1;
            Assert.False(lease.IsAcquired);
            lease = await wait2;
            Assert.False(lease.IsAcquired);
            lease = await wait3;
            Assert.False(lease.IsAcquired);

            // Throws after disposal
            Assert.Throws<ObjectDisposedException>(() => limiter.Acquire(1));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => limiter.WaitAsync(1).AsTask());
        }

        [Fact]
        public override async Task DisposeAsyncReleasesQueuedAcquires()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.OldestFirst, 3,
                TimeSpan.Zero, 1, autoReplenishment: false));
            var lease = limiter.Acquire(1);
            var wait1 = limiter.WaitAsync(1);
            var wait2 = limiter.WaitAsync(1);
            var wait3 = limiter.WaitAsync(1);
            Assert.False(wait1.IsCompleted);
            Assert.False(wait2.IsCompleted);
            Assert.False(wait3.IsCompleted);

            await limiter.DisposeAsync();

            lease = await wait1;
            Assert.False(lease.IsAcquired);
            lease = await wait2;
            Assert.False(lease.IsAcquired);
            lease = await wait3;
            Assert.False(lease.IsAcquired);

            // Throws after disposal
            Assert.Throws<ObjectDisposedException>(() => limiter.Acquire(1));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => limiter.WaitAsync(1).AsTask());
        }

        [Fact]
        public async Task RetryMetadataOnFailedWaitAsync()
        {
            var options = new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.FromSeconds(20), 1, autoReplenishment: false);
            var limiter = new TokenBucketRateLimiter(options);

            using var lease = limiter.Acquire(2);

            var failedLease = await limiter.WaitAsync(2);
            Assert.False(failedLease.IsAcquired);
            Assert.True(failedLease.TryGetMetadata(MetadataName.RetryAfter.Name, out var metadata));
            var metaDataTime = Assert.IsType<TimeSpan>(metadata);
            Assert.Equal(options.ReplenishmentPeriod.Ticks * 2, metaDataTime.Ticks);

            Assert.True(failedLease.TryGetMetadata(MetadataName.RetryAfter, out var typedMetadata));
            Assert.Equal(options.ReplenishmentPeriod.Ticks * 2, typedMetadata.Ticks);
            Assert.Collection(failedLease.MetadataNames, item => item.Equals(MetadataName.RetryAfter.Name));
        }

        [Fact]
        public async Task CorrectRetryMetadataWithQueuedItem()
        {
            var options = new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.FromSeconds(20), 1, autoReplenishment: false);
            var limiter = new TokenBucketRateLimiter(options);

            using var lease = limiter.Acquire(2);
            // Queue item which changes the retry after time for failed items
            var wait = limiter.WaitAsync(1);
            Assert.False(wait.IsCompleted);

            var failedLease = await limiter.WaitAsync(2);
            Assert.False(failedLease.IsAcquired);
            Assert.True(failedLease.TryGetMetadata(MetadataName.RetryAfter, out var typedMetadata));
            Assert.Equal(options.ReplenishmentPeriod.Ticks * 3, typedMetadata.Ticks);
        }

        [Fact]
        public async Task CorrectRetryMetadataWithMultipleTokensPerPeriod()
        {
            var options = new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.FromSeconds(20), 2, autoReplenishment: false);
            var limiter = new TokenBucketRateLimiter(options);

            using var lease = limiter.Acquire(2);
            // Queue item which changes the retry after time for failed waits
            var wait = limiter.WaitAsync(1);
            Assert.False(wait.IsCompleted);

            var failedLease = await limiter.WaitAsync(2);
            Assert.False(failedLease.IsAcquired);
            Assert.True(failedLease.TryGetMetadata(MetadataName.RetryAfter, out var typedMetadata));
            Assert.Equal(options.ReplenishmentPeriod, typedMetadata);
        }

        [Fact]
        public async Task CorrectRetryMetadataWithLargeTokensPerPeriod()
        {
            var options = new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.FromSeconds(20), 100, autoReplenishment: false);
            var limiter = new TokenBucketRateLimiter(options);

            using var lease = limiter.Acquire(2);
            // Queue item which changes the retry after time for failed items
            var wait = limiter.WaitAsync(1);
            Assert.False(wait.IsCompleted);

            var failedLease = await limiter.WaitAsync(2);
            Assert.False(failedLease.IsAcquired);
            Assert.True(failedLease.TryGetMetadata(MetadataName.RetryAfter, out var typedMetadata));
            Assert.Equal(options.ReplenishmentPeriod, typedMetadata);
        }

        [Fact]
        public async Task CorrectRetryMetadataWithNonZeroAvailableItems()
        {
            var options = new TokenBucketRateLimiterOptions(3, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.FromSeconds(20), 1, autoReplenishment: false);
            var limiter = new TokenBucketRateLimiter(options);

            using var lease = limiter.Acquire(2);

            var failedLease = await limiter.WaitAsync(3);
            Assert.False(failedLease.IsAcquired);
            Assert.True(failedLease.TryGetMetadata(MetadataName.RetryAfter, out var typedMetadata));
            Assert.Equal(options.ReplenishmentPeriod.Ticks * 2, typedMetadata.Ticks);
        }

        [Fact]
        public void TryReplenishHonorsTokensPerPeriod()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(7, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.Zero, 3, autoReplenishment: false));
            Assert.True(limiter.Acquire(5).IsAcquired);
            Assert.False(limiter.Acquire(3).IsAcquired);

            Assert.Equal(2, limiter.GetAvailablePermits());
            Assert.True(limiter.TryReplenish());
            Assert.Equal(5, limiter.GetAvailablePermits());

            Assert.True(limiter.TryReplenish());
            Assert.Equal(7, limiter.GetAvailablePermits());
        }

        [Fact]
        public void TryReplenishWithAllTokensAvailable_Noops()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.Zero, 1, autoReplenishment: false));
            Assert.Equal(2, limiter.GetAvailablePermits());
            Assert.True(limiter.TryReplenish());
            Assert.Equal(2, limiter.GetAvailablePermits());
        }

        [Fact]
        public void TryReplenishWithAutoReplenish_ReturnsFalse()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.FromSeconds(1), 1, autoReplenishment: true));
            Assert.Equal(2, limiter.GetAvailablePermits());
            Assert.False(limiter.TryReplenish());
            Assert.Equal(2, limiter.GetAvailablePermits());
        }

        [Fact]
        public async Task AutoReplenish_ReplenishesTokens()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.OldestFirst, 1,
                TimeSpan.FromMilliseconds(1000), 1, autoReplenishment: true));
            Assert.Equal(2, limiter.GetAvailablePermits());
            limiter.Acquire(2);

            var lease = await limiter.WaitAsync(1);
            Assert.True(lease.IsAcquired);
        }

        [Fact]
        public override async Task CanAcquireResourcesWithWaitAsyncWithQueuedItemsIfNewestFirst()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.NewestFirst, 2,
                TimeSpan.Zero, 2, autoReplenishment: false));

            var lease = limiter.Acquire(1);
            Assert.True(lease.IsAcquired);

            var wait = limiter.WaitAsync(2);
            Assert.False(wait.IsCompleted);

            Assert.Equal(1, limiter.GetAvailablePermits());
            lease = await limiter.WaitAsync(1);
            Assert.True(lease.IsAcquired);
            Assert.False(wait.IsCompleted);

            limiter.TryReplenish();

            lease = await wait;
            Assert.True(lease.IsAcquired);
        }

        [Fact]
        public override async Task CannotAcquireResourcesWithWaitAsyncWithQueuedItemsIfOldestFirst()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.OldestFirst, 3,
                TimeSpan.Zero, 2, autoReplenishment: false));

            var lease = limiter.Acquire(1);
            Assert.True(lease.IsAcquired);

            var wait = limiter.WaitAsync(2);
            var wait2 = limiter.WaitAsync(1);
            Assert.False(wait.IsCompleted);
            Assert.False(wait2.IsCompleted);

            limiter.TryReplenish();

            lease = await wait;
            Assert.True(lease.IsAcquired);
            Assert.False(wait2.IsCompleted);

            limiter.TryReplenish();

            lease = await wait2;
            Assert.True(lease.IsAcquired);
        }

        [Fact]
        public override async Task CanAcquireResourcesWithAcquireWithQueuedItemsIfNewestFirst()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.NewestFirst, 3,
                TimeSpan.Zero, 2, autoReplenishment: false));

            var lease = limiter.Acquire(1);
            Assert.True(lease.IsAcquired);

            var wait = limiter.WaitAsync(2);
            Assert.False(wait.IsCompleted);

            lease = limiter.Acquire(1);
            Assert.True(lease.IsAcquired);
            Assert.False(wait.IsCompleted);

            limiter.TryReplenish();

            lease = await wait;
            Assert.True(lease.IsAcquired);
        }

        [Fact]
        public override async Task CannotAcquireResourcesWithAcquireWithQueuedItemsIfOldestFirst()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(2, QueueProcessingOrder.OldestFirst, 3,
                TimeSpan.Zero, 2, autoReplenishment: false));

            var lease = limiter.Acquire(1);
            Assert.True(lease.IsAcquired);

            var wait = limiter.WaitAsync(2);
            Assert.False(wait.IsCompleted);

            lease = limiter.Acquire(1);
            Assert.False(lease.IsAcquired);

            limiter.TryReplenish();

            lease = await wait;
            Assert.True(lease.IsAcquired);
        }

        [Fact]
        public async Task ReplenishWorksWhenTicksWrap()
        {
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions(10, QueueProcessingOrder.OldestFirst, 2,
                TimeSpan.FromMilliseconds(2), 1, autoReplenishment: false));

            var lease = limiter.Acquire(10);
            Assert.True(lease.IsAcquired);

            var wait = limiter.WaitAsync(1);
            Assert.False(wait.IsCompleted);

            var replenishInternalMethod = typeof(TokenBucketRateLimiter).GetMethod("ReplenishInternal", Reflection.BindingFlags.NonPublic | Reflection.BindingFlags.Instance)!;
            // This will set the last tick to the max value
            replenishInternalMethod.Invoke(limiter, new object[] { uint.MaxValue });

            lease = await wait;
            Assert.True(lease.IsAcquired);

            wait = limiter.WaitAsync(1);
            Assert.False(wait.IsCompleted);

            // ticks wrapped, should replenish
            replenishInternalMethod.Invoke(limiter, new object[] { 2U });
            lease = await wait;
            Assert.True(lease.IsAcquired);

            replenishInternalMethod.Invoke(limiter, new object[] { uint.MaxValue });

            wait = limiter.WaitAsync(2);
            Assert.False(wait.IsCompleted);

            // ticks wrapped, but only 1 millisecond passed, make sure the wrapping behaves correctly and replenish doesn't happen
            replenishInternalMethod.Invoke(limiter, new object[] { 1U });
            Assert.False(wait.IsCompleted);
            Assert.Equal(1, limiter.GetAvailablePermits());
        }
    }
}
