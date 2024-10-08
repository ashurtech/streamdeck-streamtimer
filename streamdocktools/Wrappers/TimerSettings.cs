﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace streamdocktools.Wrappers
{
    internal class TimerSettings
    {
        internal String TimerId { get; set; }

        internal bool ResetOnStart { get; set; }

        internal TimeSpan CounterLength { get; set; }

        internal string FileName { get; set; }

        internal string FileTitlePrefix { get; set; }

        internal string FileCountdownEndText { get; set; }

        internal bool ClearFileOnReset { get; set; }

        internal string TimeFormat { get; set; }
    }
}
