namespace GeoHydroCore
{
    internal class MarkerInfo
    {
        public MarkerInfo()
        {
            
        }

        private double _divisor;
        public string MarkerName { get; set; }
        public double Weight { get; set; } = 1.0;

        public double GetValue(MarkerValue markerValue)
        {
            return markerValue.OriginalValue.Value / _divisor;
        }

        public void SetCoeficient(double absMaxVal)
        {
            _divisor = absMaxVal;
        }

        public override string ToString()
        {
            return $"{MarkerName} | W: {Weight} | div: {_divisor}";
        }
    }
}