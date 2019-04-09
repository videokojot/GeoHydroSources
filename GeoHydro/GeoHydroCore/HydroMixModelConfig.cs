namespace GeoHydroCore
{
    class HydroMixModelConfig
    {
        public int? MinSourcesUsed { get; set; }
        public int? MaxSourcesUsed { get; set; }

        public double MinimalSourceContribution { get; set; }

        public NAValuesHandling NaValuesHandling { get; set; }
    }
}