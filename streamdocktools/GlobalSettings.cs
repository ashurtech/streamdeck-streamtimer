using Newtonsoft.Json;
using StreamTimer.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace streamdocktools
{
    public class GlobalSettings
    {
        [JsonProperty(PropertyName = "timers")]
        public Dictionary<string, TimerStatus> DicTimers { get; set; }
    }
}
