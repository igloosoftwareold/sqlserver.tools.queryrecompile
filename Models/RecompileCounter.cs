using System;
using System.Threading;

namespace sqlserver.tools.queryrecompile.Models
{
    public sealed class RecompileCounter
    {
        //We start at one.
        private readonly object currentValueLock = new object();
        private volatile int currentValue = 0;

        private readonly object currentDateLock = new object();
        private DateTime currentDate = DateTime.Now;

        public int QueryThreshold = 35;

        public int NextValue()
        {
            return Interlocked.Increment(ref this.currentValue);
        }

        public bool CanQueryBeRecompiled(bool OnTheList, int QueryCountLimitNotOnList = 2)
        {
            lock (currentValueLock)
            {
                if (OnTheList && this.currentValue == 0)
                {
                    return true;
                } else if (!OnTheList && this.currentValue >= QueryCountLimitNotOnList)
                {
                    this.Reset(OnTheList);
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
                    this.Reset();
                    return true;
                }
            }
            return false;
        }

        public int GetValue()
        {
            lock (currentValueLock)
            {
                return this.currentValue;
            }
        }

        public void Reset(bool OnTheList=true)
        {
            lock (currentValueLock)
            {
                lock (currentDateLock)
                {
                    this.currentDate = DateTime.Now;
                    if (OnTheList)
                    {
                        //We set this to 1 for Queries on the list.
                        this.currentValue = 1;
                    } else
                    {
                        //We set this to 0 for queries not on the list.
                        this.currentValue = 0;
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
                    this.currentDate = (DateTime)value;
                }
            }
        }
    }
}
