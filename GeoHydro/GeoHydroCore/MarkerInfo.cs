namespace GeoHydroCore
{
    internal class MarkerInfo
    {
        public MarkerInfo()
        {
            
        }

        public double NormalizationCoefficient { get; set; }
        public string MarkerName { get; set; }
        public double Weight { get; set; } = 1.0;

        public double GetValue(MarkerValue markerValue)
        {
            return markerValue.OriginalValue.Value / NormalizationCoefficient;
        }

        public void SetCoeficient(double absMaxVal)
        {
            NormalizationCoefficient = absMaxVal;
        }

        public override string ToString()
        {
            return $"{MarkerName} | W: {Weight} | div: {NormalizationCoefficient}";
        }
    }
}