using OrleansVoting.Grains;

namespace OrleansVoting.BoxOffice;

public sealed partial class BoxOfficeService
{
    private sealed class EventWatcherSubscription : IAsyncDisposable
    {
        // Observer subscriptions expire after a minute, so renew well before then
        private static readonly TimeSpan RenewalPeriod = TimeSpan.FromSeconds(30);

        private readonly CancellationTokenSource _cancellation = new();
        private readonly IGrainFactory _grains;
        private readonly WeakReference<IEventWatcher> _watcher;
        private readonly IEventInventoryGrain _inventory;
        private readonly IEventWatcher _reference;
        private readonly Task _renewal;

        public EventWatcherSubscription(IGrainFactory grains, IEventWatcher watcher, IEventInventoryGrain inventory, IEventWatcher reference)
        {
            _grains = grains;
            _watcher = new(watcher);
            _inventory = inventory;
            _reference = reference;
            _renewal = Task.Run(RenewAsync);
        }

        private async Task RenewAsync()
        {
            using var timer = new PeriodicTimer(RenewalPeriod);

            // Stop renewing once disposed, or once the page that owns the watcher has been garbage collected
            do
            {
                if (!_watcher.TryGetTarget(out _))
                {
                    break;
                }

                try
                {
                    // Push the current state on every (re)subscription, so changes made before the subscription
                    // existed, or while the grain moved to another silo, still reach the page
                    Push(await _inventory.StartWatching(_reference));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A transient failure (for example, while the grain moves between silos) is retried on the next tick
                }
            }
            while (await WaitForNextTickAsync(timer));

            // Wait for the grain to drop the subscription before deleting the reference, so no notification
            // arrives for an observer that no longer exists
            try
            {
                await _inventory.StopWatching(_reference);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The grain may be deactivating or moving; its subscription then expires on its own
            }

            _grains.DeleteObjectReference<IEventWatcher>(_reference);
        }

        // Kept out of the async loop so the subscription never holds a strong reference to the watcher
        private void Push(EventInventorySnapshot snapshot)
        {
            if (_watcher.TryGetTarget(out var watcher))
            {
                watcher.OnInventoryChanged(snapshot);
            }
        }

        private async Task<bool> WaitForNextTickAsync(PeriodicTimer timer)
        {
            try
            {
                return await timer.WaitForNextTickAsync(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cancellation.CancelAsync();
            await _renewal;
            _cancellation.Dispose();
        }
    }
}
