using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoHydroCore
{
    class HydroSourceValues
    {
        private List<MarkerInfo> _markerInfos;
        private readonly NAValuesHandling naValuesHandling;
        private List<Source> _sources;

        public HydroSourceValues(List<Source> sources,
                                 List<MarkerInfo> markerInfos, NAValuesHandling naValuesHandling)
        {
            _sources = sources;
            _markerInfos = markerInfos;
            this.naValuesHandling = naValuesHandling;
        }

        public List<Source> GetSourcesList()
        {
            return _sources;
        }

        public List<MarkerInfo> GetMarkerInfos()
        {
            return _markerInfos;
        }

        public void StandardizeValues()
        {
            HandleNAValues();
            foreach (var markerInfo in _markerInfos)
            {
                var markerValues = _sources.Select(x => x[markerInfo]).ToList();
                var absMaxVal = markerValues.Select(mv => Math.Abs(mv.OriginalValue.Value)).Max();
                markerInfo.SetCoeficient(absMaxVal);
            }
        }

        internal void HandleNAValues()
        {
            // either remove those or assign mean of other in data set
            switch (naValuesHandling)
            {
                case NAValuesHandling.Remove:
                    _sources = _sources.Where(x => x.MarkerValues.All(m => m.OriginalValue != null)).ToList();
                    break;
                case NAValuesHandling.SetMean:

                    foreach (var markerInfo in _markerInfos)
                    {
                        var vals = _sources.Select(x => x[markerInfo]).ToList();
                        var validVals = vals.Where(x => x.OriginalValue != null).ToList();
                        var naValues = vals.Where(x => x.OriginalValue == null).ToList();
                        var sum = validVals.Sum(x => x.OriginalValue.Value);
                        var mean = sum / validVals.Count;
                        foreach (var markerValue in naValues)
                        {
                            markerValue.OriginalValue = mean;
                        }
                    }
                    break;
                default: throw new ArgumentOutOfRangeException();
            }
        }
    }
}