using System.Collections.Generic;
using System.Linq;

namespace GeoHydroCore
{
    internal class Source
    {
        public List<MarkerValue> MarkerValues { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public double MaxSourceContribution { get; set; } = 1.0;

        public MarkerValue this[MarkerInfo index]
        {
            get { return MarkerValues.Single(x => x.MarkerInfo == index); }
        }

        public override string ToString()
        {
            return $"{Code}";
        }
    }
}