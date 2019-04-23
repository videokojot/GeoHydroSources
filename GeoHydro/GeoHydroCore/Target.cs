namespace GeoHydroCore
{
    class Target
    {
        public Source Source { get; }

        public Target(Source source)
        {
            this.Source = source;
        }

        public double this[MarkerInfo marker] { get { return Source[marker].Value; } }

    }
}