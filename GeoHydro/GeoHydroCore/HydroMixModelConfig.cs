﻿using System.Collections.Generic;

namespace GeoHydroCore
{
    class HydroMixModelConfig
    {
        public int? MinSourcesUsed { get; set; }
        public int? MaxSourcesUsed { get; set; }

        public double MinimalSourceContribution { get; set; }

        public NAValuesHandling NaValuesHandling { get; set; }
        public string ConfigAlias { get; set; }

        public Dictionary<string,double> MarkerWeigths { get; set; }
    }
}