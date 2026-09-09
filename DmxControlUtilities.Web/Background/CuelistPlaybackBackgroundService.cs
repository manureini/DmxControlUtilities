using DmxControlUtilities.Lib.Services;

namespace DmxControlUtilities.Web.Background
{
    public class CuelistPlaybackBackgroundService : BackgroundService
    {
        private readonly CuelistService mCuelistService;

        public CuelistPlaybackBackgroundService(CuelistService pCuelistService)
        {
            mCuelistService = pCuelistService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                    mCuelistService.Update();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            finally
            {
                mCuelistService.StopAll();
            }
        }
    }
}
