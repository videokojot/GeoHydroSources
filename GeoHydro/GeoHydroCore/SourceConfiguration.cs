using System.Collections.Generic;

namespace GeoHydroCore
{
    internal class SourceConfiguration
    {
        public string Alias { get; set; }
        public Dictionary<string, bool> SourcesUsage { get; set; }
        public Dictionary<string, double> MaxSourceContribution { get; set; }
    }
}