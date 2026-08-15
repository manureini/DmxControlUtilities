using DmxControlUtilities.Lib.Models;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Holds the timecode shows.
    /// </summary>
    public class TimecodeShowService
    {
        private readonly List<TimecodeShow> mShows = new();
        private readonly object mLock = new();

        public IReadOnlyList<TimecodeShow> Shows
        {
            get
            {
                lock (mLock)
                {
                    return mShows.ToList();
                }
            }
        }

        public TimecodeShow? GetShow(Guid pId)
        {
            lock (mLock)
            {
                return mShows.FirstOrDefault(s => s.Id == pId);
            }
        }

        public void AddShow(TimecodeShow pShow)
        {
            lock (mLock)
            {
                mShows.Add(pShow);
            }
        }

        public void RemoveShow(TimecodeShow pShow)
        {
            lock (mLock)
            {
                mShows.RemoveAll(s => s.Id == pShow.Id);
            }
        }
    }
}
