namespace GeoHydroCore
{
    internal class MarkerValue
    {
        public double? OriginalValue { get; set; }

        public MarkerInfo MarkerInfo { get; set; }
        public double Value => MarkerInfo.GetValue(this);
        public Source Source { get; set; }

        public override string ToString()
        {
            return $"{Source.Code,-5} | {MarkerInfo.MarkerName,-6} | Orig: {OriginalValue,-8} | Cur: {Value,-8} ";
        }
    }
}