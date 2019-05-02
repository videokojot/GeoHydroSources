using System.Collections.Generic;
using System.Linq;

namespace GeoHydroCore
{
    internal class GeoHydroSolutionOutput
    {
        public string TextOutput { get; set; }
        public string ConfigALias { get; set; }
        public string SourcesConfigAlias { get; set; }
        public Dictionary<string, double> ResultingMix { get; set; }
        public double NormalizedError { get; set; }
        public Target Target { get; set; }
        public IOrderedEnumerable<(Source Source, double Contribution)> UsedSources { get; set; }
    }
}