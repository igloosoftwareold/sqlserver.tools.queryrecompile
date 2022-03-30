namespace sqlserver.tools.queryrecompile.Models
{
    public sealed class RecompileCounter
    {
        //We start at one.
        private readonly object currentValueLock = new();
        private volatile int currentValue = 0;

        private readonly object currentDateLock = new();
        private DateTime currentDate = DateTime.Now;

        public int QueryThreshold = 35;

        public int NextValue()
        {
            return Interlocked.Increment(ref currentValue);
        }

        public bool CanQueryBeRecompiled(bool OnTheList, int QueryCountLimitNotOnList = 2)
        {
            lock (currentValueLock)
            {
                if (OnTheList && currentValue == 0)
                {
                    return true;
                }
                else if (!OnTheList && currentValue >= QueryCountLimitNotOnList)
                {
                    Reset(OnTheList);
                    return true;
                }
            }
            return false;
        }

        public bool HasThresholdPassed()
        {
            lock (currentDateLock)
            {
                TimeSpan _TimeSpan = DateTime.Now.Subtract(currentDate);
                if (_TimeSpan.TotalSeconds > QueryThreshold)
                {
                    Reset();
                    return true;
                }
            }
            return false;
        }

        public int GetValue()
        {
            lock (currentValueLock)
            {
                return currentValue;
            }
        }

        public void Reset(bool OnTheList = true)
        {
            lock (currentValueLock)
            {
                lock (currentDateLock)
                {
                    currentDate = DateTime.Now;
                    if (OnTheList)
                    {
                        //We set this to 1 for Queries on the list.
                        currentValue = 1;
                    }
                    else
                    {
                        //We set this to 0 for queries not on the list.
                        currentValue = 0;
                    }
                }
            }
        }
        public DateTime StatisticDate
        {
            get
            {
                lock (currentDateLock)
                {
                    return currentDate;
                }
            }
            set
            {
                lock (currentDateLock)
                {
                    currentDate = value;
                }
            }
        }
    }
}
