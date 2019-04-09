namespace GeoHydroCore
{
    class Target
    {
        private readonly Source s;

        public Target(Source s)
        {
            this.s = s;
        }

        public double this[MarkerInfo marker] { get { return s[marker].Value; } }

    }
}